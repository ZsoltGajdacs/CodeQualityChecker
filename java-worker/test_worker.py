"""Behavioral checks against the actual JavaParser worker process (standard library only)."""
import json
import io
import hashlib
import os
from pathlib import Path
import subprocess
import tempfile
import unittest
import xml.etree.ElementTree as ET
import zipfile


JAR = Path(__file__).parent.resolve() / "target/java-worker.jar"


class JavaWorkerTests(unittest.TestCase):
    def analyze(self, files, evidence_directory=None, *, source_only=False):
        with tempfile.TemporaryDirectory() as directory:
            root = Path(directory)
            for name, source in files.items():
                path = root / name
                path.parent.mkdir(parents=True, exist_ok=True)
                if isinstance(source, bytes):
                    path.write_bytes(source)
                else:
                    path.write_text(source)
            version = "3" if source_only else "2"
            snapshot = {"root": directory, "files": sorted(files), "sourceFingerprint": "fixture",
                        "schemaVersion": version, "sourceOnly": source_only}
            if evidence_directory is not None:
                snapshot["referenceEvidenceDirectory"] = str(root / evidence_directory)
            process = subprocess.run(["java", "-jar", str(JAR)], input=json.dumps(snapshot),
                                     text=True, capture_output=True, timeout=30, check=True)
            self.assertEqual("", process.stderr)
            self.assertEqual(version, json.loads(process.stdout)["schemaVersion"])
            return json.loads(process.stdout)

    def test_overloads_nested_types_and_exact_complexity(self):
        facts = self.analyze({"src/demo/Outer.java": """package demo;
class Outer {
  Outer() {}
  int choose(int x) {
    if (x > 0 && x < 3) return x;
    return 0;
  }
  int choose(String x) { return x.length(); }
  static class Inner { int answer() { return 42; } }
}
"""})
        self.assertFalse(facts["semanticComplete"])
        methods = {method["qualifiedName"]: method for method in facts["methods"]}
        self.assertEqual(3, methods["demo.Outer.choose(int)"]["complexity"])
        self.assertEqual(4, methods["demo.Outer.choose(int)"]["location"]["startLine"])
        self.assertIn("demo.Outer.choose(java.lang.String)", methods)
        self.assertIn("demo.Outer..ctor()", methods)
        self.assertIn("demo.Outer.Inner.answer()", methods)

    def test_resolved_helpers_and_assertions(self):
        facts = self.analyze({"src/demo/Example.java": """package demo;
class Example {
  void helper(int value) { assert value == 2; }
  void exercise() { helper(2); }
}
"""})
        methods = {method["qualifiedName"]: method for method in facts["methods"]}
        self.assertEqual(1, methods["demo.Example.helper(int)"]["assertions"])
        self.assertEqual(["pom.xml#main::demo.Example.helper(int)"], methods["demo.Example.exercise()"]["calls"])
        self.assertTrue(methods["demo.Example.exercise()"]["evidence"]["calls"])

    def test_missing_symbols_never_claim_semantic_completeness(self):
        facts = self.analyze({"Example.java": "class Example { Missing value; void run() { value.perform(); } }"})
        self.assertFalse(facts["semanticComplete"])
        self.assertFalse(facts["methods"][0]["complete"])
        self.assertTrue(any(d["code"] == "java.unresolved_type" for d in facts["diagnostics"]))

    def test_structural_fingerprints_ignore_local_names_and_literals(self):
        facts = self.analyze({"Example.java": """class Example {
  int first(int value) {
    int result = value + 1;
    if (result > 3) result = 4;
    return result;
  }
  int second(int item) {
    int answer = item + 2;
    if (answer > 8) answer = 9;
    return answer;
  }
}"""})
        self.assertEqual(facts["methods"][0]["fingerprints"], facts["methods"][1]["fingerprints"])
        self.assertGreaterEqual(facts["methods"][0]["nodeCount"], 20)

    def test_wiring_constructors_ignore_names_and_unresolved_dependency_types(self):
        facts = self.analyze({"Wiring.java": """class Wiring {
  Missing first;
  Missing second;
  Missing third;
  Missing fourth;
  Wiring(Missing renamed, Missing b, Missing c, Missing d) {
    // Wiring has no shared behavior to extract.
    this.first = renamed;
    second = b;
    third = c;
    fourth = d;
  }
  void wire(Missing renamed, Missing b, Missing c, Missing d) {
    this.first = renamed;
    second = b;
    third = c;
    fourth = d;
  }
}"""})
        constructor = next(m for m in facts["methods"] if "..ctor(" in m["qualifiedName"])
        method = next(m for m in facts["methods"] if ".wire(" in m["qualifiedName"])
        self.assertEqual([], constructor["fingerprints"])
        self.assertGreaterEqual(constructor["nodeCount"], 20)
        self.assertEqual(1, constructor["complexity"])
        self.assertEqual(4, constructor["executableLines"])
        self.assertTrue(constructor["evidence"]["syntax"])
        self.assertTrue(method["fingerprints"])

    def test_explicit_fields_allow_parameter_shadowing_but_empty_constructors_are_unchanged(self):
        facts = self.analyze({"Example.java": """class Example {
  int value;
  Example() {}
  Example(int value) { this.value = value; }
}"""})
        constructors = {m["qualifiedName"]: m for m in facts["methods"]}
        self.assertEqual([], constructors["Example..ctor(int)"]["fingerprints"])
        self.assertEqual(["BlockStmt()"], constructors["Example..ctor()"]["fingerprints"])

    def test_substantive_or_ambiguous_constructors_keep_fingerprints(self):
        bodies = ["this.value = input + 1;", "this.value = Math.abs(input);",
                  "if (input < 0) throw new IllegalArgumentException(); this.value = input;",
                  "this.value += input;", "this.value = 1;", "this.value = (input);",
                  "super(); this.value = input;", "this(); this.value = input;",
                  "{ this.value = input; }", "; this.value = input;",
                  "input = input;", "other.value = input;"]
        for body in bodies:
            with self.subTest(body=body):
                facts = self.analyze({"Example.java": "class Example { int value; Example other; "
                                      "Example() {} Example(int input) { " + body + " } }"})
                constructor = next(m for m in facts["methods"] if "..ctor(int)" in m["qualifiedName"])
                self.assertTrue(constructor["fingerprints"])

    def test_static_inherited_duplicate_and_outer_fields_are_not_direct_wiring(self):
        sources = ["class Example { static int value; Example(int input) { this.value = input; } }",
                   "class Base { int value; } class Example extends Base { Example(int input) { value = input; } }",
                   "class Example { int value; int value; Example(int input) { this.value = input; } }",
                   "class Example { int value; class Inner { Inner(int input) { Example.this.value = input; } } }",
                   "class Example { int value; Example(int value) { value = value; } }"]
        for source in sources:
            with self.subTest(source=source):
                facts = self.analyze({"Example.java": source})
                constructor = next(m for m in facts["methods"] if "..ctor(int)" in m["qualifiedName"])
                self.assertTrue(constructor["fingerprints"])

    def test_deterministic_facts(self):
        source = {"Example.java": "class Example { int value(int x) { return x + 1; } }"}
        self.assertEqual(self.analyze(source), self.analyze(source))

    def manifest(self, contexts):
        return json.dumps({"schemaVersion": "3", "sourceFingerprint": "fixture", "contexts": contexts})

    def context(self, module="pom.xml", source_set="main", **options):
        return {"id": module + "#" + source_set, "modulePath": module, "sourceSet": source_set,
                "sourceRoots": options.get("roots", ["src/main/java"]), "classpath": options.get("classpath", []),
                "reachableContexts": options.get("reachable", [])}

    def test_semantic_failures_preserve_syntax_and_fingerprints(self):
        facts = self.analyze({"Example.java": "class Example { Missing value; int read(int n) { if(n > 0) return value.get(); return 0; } }"})
        method = facts["methods"][0]
        self.assertTrue(method["evidence"]["syntax"])
        self.assertFalse(method["evidence"]["calls"])
        self.assertEqual(2, method["complexity"])
        self.assertTrue(method["fingerprints"])

    def test_verified_main_and_test_are_isolated_and_ids_do_not_collide(self):
        contexts = [self.context(roots=["src/main/java"]),
                    self.context(source_set="test", roots=["src/test/java"], reachable=["pom.xml#main"]),
                    self.context(module="other/pom.xml", roots=["other/src/main/java"])]
        facts = self.analyze({
            "src/main/java/demo/Thing.java": "package demo; class Thing { void main() { TestOnly.check(); } }",
            "src/test/java/demo/TestOnly.java": "package demo; class TestOnly { static void check() {} }",
            "other/src/main/java/demo/Thing.java": "package demo; class Thing { int number() { return 3; } }",
            ".cqc/java-contexts.json": self.manifest(contexts)})
        methods = {m["id"]: m for m in facts["methods"]}
        self.assertFalse(methods["pom.xml#main::demo.Thing.main()"]["evidence"]["calls"])
        self.assertTrue(methods["other/pom.xml#main::demo.Thing.number()"]["evidence"]["verifiedContext"])
        self.assertTrue(methods["other/pom.xml#main::demo.Thing.number()"]["complete"])

    def test_explicit_foreign_test_artifacts_are_visible_but_own_tests_are_rejected(self):
        contexts = [self.context(module="service/pom.xml", roots=["service/src/main/java"], reachable=["api/pom.xml#test"]),
                    self.context(module="api/pom.xml", source_set="test", roots=["api/src/test/java"])]
        files = {"service/src/main/java/demo/Service.java": "package demo; class Service { int value() { return TestSupport.value(); } }",
                 "api/src/test/java/demo/TestSupport.java": "package demo; public class TestSupport { public static int value() { return 3; } }",
                 ".cqc/java-contexts.json": self.manifest(contexts)}
        facts = self.analyze(files)
        service = next(m for m in facts["methods"] if m["qualifiedName"] == "demo.Service.value()")
        self.assertTrue(service["evidence"]["calls"])
        self.assertEqual(["api/pom.xml#test::demo.TestSupport.value()"], service["calls"])
        contexts[0]["reachableContexts"] = ["service/pom.xml#test"]
        contexts.append(self.context(module="service/pom.xml", source_set="test", roots=["service/src/test/java"]))
        files[".cqc/java-contexts.json"] = self.manifest(contexts)
        facts = self.analyze(files)
        self.assertTrue(any(d["code"] == "java.stale_classpath" for d in facts["diagnostics"]))
        self.assertFalse(any(c["verified"] for c in facts["contexts"]))

    def test_corrupt_context_does_not_poison_other_module(self):
        contexts = [self.context(classpath=[{"path": ".cqc/missing.jar", "kind": "jar", "sha256": "0" * 64}]),
                    self.context(module="other/pom.xml", roots=["other/src/main/java"])]
        facts = self.analyze({
            "src/main/java/A.java": "class A { int value() { return 1; } }",
            "other/src/main/java/B.java": "class B { int value() { return 2; } }",
            ".cqc/java-contexts.json": self.manifest(contexts)})
        types = {t["qualifiedName"]: t for t in facts["types"]}
        self.assertFalse(types["A"]["evidence"]["verifiedContext"])
        self.assertTrue(types["B"]["evidence"]["verifiedContext"])

    def test_parse_gaps_are_explicit_without_discarding_healthy_syntax(self):
        facts = self.analyze({"Broken.java": "class Broken { ??? }", "Good.java": "class Good { int x() { return 1; } }"})
        self.assertEqual("Broken.java", facts["syntaxGaps"][0]["file"])
        self.assertEqual("pom.xml#main", facts["syntaxGaps"][0]["contextId"])
        self.assertTrue(facts["syntaxGaps"][0]["unknownEntities"])
        self.assertTrue(facts["methods"][0]["evidence"]["syntax"])

    def artifact(self, version, **options):
        output = io.BytesIO()
        with zipfile.ZipFile(output, "w") as archive:
            archive.writestr("META-INF/maven/demo/library/pom.properties",
                             f"groupId=demo\nartifactId=library\nversion={version}\n")
            for name, content in options.get("classes", {}).items():
                archive.writestr(name, content)
        return output.getvalue()

    def class_bytes(self, value, name="Library"):
        with tempfile.TemporaryDirectory() as directory:
            source = Path(directory) / (name + ".java")
            source.write_text(f"package demo; public class {name} {{ public int value() {{ return {value}; }} }}")
            subprocess.run(["javac", "-d", directory, str(source)], check=True, capture_output=True, timeout=30)
            return (Path(directory) / "demo" / (name + ".class")).read_bytes()

    def test_disjoint_classifier_jars_and_identical_classes_are_not_conflicts(self):
        library = self.class_bytes(1)
        support = self.class_bytes(1, "Support")
        jars = [self.artifact("1", classes={"demo/Library.class": library}),
                self.artifact("1", classes={"demo/Support.class": support}),
                self.artifact("2", classes={"demo/Library.class": library}),
                self.artifact("3")]
        entries = [{"path": f"repo/{index}.jar", "kind": "jar", "sha256": hashlib.sha256(content).hexdigest()} for index, content in enumerate(jars)]
        files = {entry["path"]: content for entry, content in zip(entries, jars)}
        files["src/main/java/A.java"] = "class A { int x() { return 1; } }"
        files[".cqc/java-contexts.json"] = self.manifest([self.context(classpath=entries)])
        facts = self.analyze(files)
        self.assertTrue(facts["semanticComplete"], facts["diagnostics"])
        self.assertFalse(any(d["code"] == "java.classpath_conflict" for d in facts["diagnostics"]))

    def test_different_module_versions_are_valid_but_same_context_conflicts(self):
        one = self.artifact("1", classes={"demo/Library.class": self.class_bytes(1)})
        two = self.artifact("2", classes={"demo/Library.class": self.class_bytes(2)})
        first = {"path": "repo/one.jar", "kind": "jar", "sha256": hashlib.sha256(one).hexdigest()}
        second = {"path": "repo/two.jar", "kind": "jar", "sha256": hashlib.sha256(two).hexdigest()}
        files = {"src/main/java/A.java": "class A { int x() { return 1; } }",
                 "other/src/main/java/B.java": "class B { int x() { return 2; } }",
                 first["path"]: one, second["path"]: two}
        contexts = [self.context(classpath=[first]),
                    self.context(module="other/pom.xml", roots=["other/src/main/java"], classpath=[second])]
        files[".cqc/java-contexts.json"] = self.manifest(contexts)
        self.assertTrue(self.analyze(files)["semanticComplete"])
        contexts[0]["classpath"].append(second)
        files[".cqc/java-contexts.json"] = self.manifest(contexts)
        facts = self.analyze(files)
        types = {t["qualifiedName"]: t for t in facts["types"]}
        self.assertFalse(types["A"]["evidence"]["verifiedContext"])
        self.assertTrue(types["B"]["evidence"]["verifiedContext"])
        self.assertTrue(any(d["code"] == "java.classpath_conflict" for d in facts["diagnostics"]))

    def test_class_directory_resolves_generated_types_and_verifies_current_bytes(self):
        classes = {"demo/Library.class": self.class_bytes(1), "demo/Support.class": self.class_bytes(2, "Support")}
        digest = hashlib.sha256()
        for path, content in sorted(classes.items()):
            digest.update(path.encode() + b"\0" + hashlib.sha256(content).digest())
        directory = "target/classes"
        entry = {"path": directory, "kind": "directory", "sha256": digest.hexdigest()}
        files = {f"{directory}/{path}": content for path, content in classes.items()}
        files["src/main/java/A.java"] = "import demo.Library; class A { int x() { return new Library().value(); } }"
        files[".cqc/java-contexts.json"] = self.manifest([self.context(classpath=[entry])])
        self.assertTrue(self.analyze(files)["semanticComplete"])

        conflicting_jar = self.artifact("2", classes={"demo/Library.class": self.class_bytes(3)})
        jar = {"path": "repo/conflicting.jar", "kind": "jar", "sha256": hashlib.sha256(conflicting_jar).hexdigest()}
        files[jar["path"]] = conflicting_jar
        files[".cqc/java-contexts.json"] = self.manifest([self.context(classpath=[entry, jar])])
        self.assertTrue(any(d["code"] == "java.classpath_conflict" for d in self.analyze(files)["diagnostics"]))

        files[".cqc/java-contexts.json"] = self.manifest([self.context(classpath=[entry])])
        files[f"{directory}/demo/Support.class"] = b"changed"
        facts = self.analyze(files)
        self.assertFalse(facts["contexts"][0]["verified"])
        self.assertTrue(any(d["code"] == "java.classpath_invalid" for d in facts["diagnostics"]))

    def test_staged_contexts_take_precedence_without_using_preexisting_evidence(self):
        files = {"src/main/java/A.java": "class A { int x() { return 1; } }",
                 ".cqc/java-contexts.json": self.manifest([self.context()])}
        self.assertTrue(self.analyze(files)["contexts"][0]["verified"])
        self.assertFalse(self.analyze(files, "run")["contexts"][0]["verified"])
        files["run/java-contexts.json"] = files.pop(".cqc/java-contexts.json")
        self.assertTrue(self.analyze(files, "run")["contexts"][0]["verified"])

    def test_reactor_edges_and_helper_calls_use_declaring_module_identity(self):
        contexts = [self.context(module="api/pom.xml", roots=["api/src/main/java"]),
                    self.context(module="service/pom.xml", roots=["service/src/main/java"], reachable=["api/pom.xml#main"])]
        facts = self.analyze({
            "api/src/main/java/demo/Api.java": "package demo; public class Api { public static int value() { return 3; } }",
            "service/src/main/java/demo/Service.java": "package demo; class Service { int value() { return Api.value(); } }",
            ".cqc/java-contexts.json": self.manifest(contexts)})
        service = next(m for m in facts["methods"] if m["qualifiedName"] == "demo.Service.value()")
        self.assertEqual(["api/pom.xml#main::demo.Api.value()"], service["calls"])
        self.assertTrue(any(e["from"] == "service/pom.xml#main::demo.Service" and e["to"] == "api/pom.xml#main::demo.Api" for e in facts["dependencies"]))
        self.assertTrue(facts["semanticComplete"], facts["diagnostics"])

    def test_stale_metadata_and_legacy_classpaths_are_never_verified(self):
        files = {"src/main/java/A.java": "class A { int x() { return 1; } }",
                 ".cqc/java-contexts.json": self.manifest([self.context()]).replace('"fixture"', '"old"')}
        facts = self.analyze(files)
        self.assertTrue(facts["methods"][0]["evidence"]["syntax"])
        self.assertFalse(facts["methods"][0]["evidence"]["verifiedContext"])
        self.assertTrue(any(d["code"] == "java.stale_classpath" for d in facts["diagnostics"]))
        files[".cqc/java-contexts.json"] = self.manifest([self.context()]).replace('"3"', '"2"')
        facts = self.analyze(files)
        self.assertFalse(facts["contexts"][0]["verified"])
        self.assertTrue(any(d["code"] == "java.stale_classpath" for d in facts["diagnostics"]))
        del files[".cqc/java-contexts.json"]
        files[".cqc/java-classpath.txt"] = "old.jar"
        self.assertTrue(any(d["code"] == "java.legacy_classpath" for d in self.analyze(files)["diagnostics"]))

    def test_generic_coverage_alias_uses_erased_parameters(self):
        facts = self.analyze({"Generic.java": """import java.util.List;
class Generic<T> {
    void accept(List<String> values, T item) { }
}
"""})
        method = facts["methods"][0]
        self.assertEqual("Generic.accept(java.util.List<java.lang.String>,T)", method["qualifiedName"])
        self.assertEqual("Generic.accept(java.util.List,java.lang.Object)", method["coverageSignature"])

    def test_jvm_coverage_alias_erases_bounds_and_includes_only_required_constructor_parameters(self):
        facts = self.analyze({"Signatures.java": """import java.util.List;
class Signatures<T extends Number> {
    <U extends T, V extends List<String>> void generic(V value, U[] array, U... extra) { }
    void nested(Inner owner, java.util.Map.Entry<String, Integer> entry) { }
    enum Mode {
        FIRST(1), SECOND("second", 1, 2);
        Mode(int value) { }
        Mode(String name, int order, int value) { }
    }
    class Inner { Inner(int value) { } }
    static class Nested { Nested(Signatures<?> owner, int value) { } }
}
"""})
        aliases = {method["qualifiedName"]: method["coverageSignature"] for method in facts["methods"]}
        self.assertEqual("Signatures.generic(java.util.List,java.lang.Number[],java.lang.Number[])",
                         aliases["Signatures.generic(V,U[],U[])"])
        self.assertEqual("Signatures.nested(Signatures$Inner,java.util.Map$Entry)",
                         aliases["Signatures.nested(Signatures.Inner,java.util.Map.Entry<java.lang.String, java.lang.Integer>)"])
        self.assertEqual("Signatures$Mode..ctor(java.lang.String,int,int)", aliases["Signatures.Mode..ctor(int)"])
        self.assertEqual("Signatures$Mode..ctor(java.lang.String,int,java.lang.String,int,int)",
                         aliases["Signatures.Mode..ctor(java.lang.String,int,int)"])
        self.assertEqual("Signatures$Inner..ctor(Signatures,int)", aliases["Signatures.Inner..ctor(int)"])
        self.assertEqual("Signatures$Nested..ctor(Signatures,int)",
                         aliases["Signatures.Nested..ctor(Signatures<?>,int)"])

    def test_static_imported_fields_have_semantic_dependency_edges(self):
        facts = self.analyze({
            "demo/Values.java": "package demo; public class Values { public static int VALUE = 3; }",
            "demo/Client.java": "package demo; import static demo.Values.VALUE; class Client { int read() { return VALUE; } }",
        })
        self.assertFalse(facts["semanticComplete"])
        self.assertTrue(any(edge["from"] == "pom.xml#main::demo.Client" and edge["to"] == "pom.xml#main::demo.Values" for edge in facts["dependencies"]))

    def test_initialization_supports_fields_blocks_and_enum_bodies(self):
        files = {"Example.java": """enum Example {
  FIRST(flag() ? 1 : 4) {
    int field = flag() ? 1 : 2;
    { if (flag()) field++; }
    @Override int value() { if (flag()) return 2; return 3; }
  },
  SECOND(2);
  static int count;
  static { if (flag() || count == 2) count = 1; }
  { if (flag()) count++; }
  int member = flag()
      ? 1
      : 2;
  Example(int number) {}
  int value() { return 0; }
  static boolean flag() { return true; }
}"""}
        for source_only in (False, True):
            with self.subTest(source_only=source_only):
                facts = self.analyze(files, source_only=source_only)
                self.assertEqual([], facts["syntaxGaps"])
                self.assertTrue(facts["types"][0]["evidence"]["syntax"])
                units = {unit["location"]["startLine"]: unit for unit in facts["executables"]}
                expected = {2: ("enumArgument", 2), 3: ("fieldInitializer", 2), 4: ("instanceInitializer", 2),
                            5: ("nestedMethod", 2), 7: ("enumArgument", 1), 9: ("staticInitializer", 3),
                            10: ("instanceInitializer", 2), 11: ("fieldInitializer", 2)}
                self.assertEqual(expected, {line: (unit["kind"], unit["complexity"]) for line, unit in units.items()})
                self.assertEqual(13, units[11]["location"]["endLine"])
                self.assertTrue(all(unit["fingerprints"] for unit in units.values()))
                self.assertTrue(all(unit["typeId"] == facts["types"][0]["id"] for unit in units.values()))
                self.assertEqual(3, len(facts["methods"]))
                self.assertEqual(facts["executables"], self.analyze(files, source_only=source_only)["executables"])

    def test_initializer_boundaries_partition_nested_executables(self):
        facts = self.analyze({"Example.java": """class Example {
  Runnable callback = () -> {
    class Local {
      Runnable another = () -> { if (flag()) yieldResult(); };
      void run() { if (flag()) yieldResult(); }
    }
    Runnable anonymous = new Runnable() {
      { if (flag()) yieldResult(); }
      public void run() { if (flag()) yieldResult(); }
    };
    if (flag()) anonymous.run();
  };
  static boolean flag() { return true; }
  static void yieldResult() {}
}"""}, source_only=True)
        units = {unit["id"]: unit for unit in facts["executables"]}
        self.assertEqual(7, len(units))
        self.assertEqual([1, 1, 2, 2, 2, 2, 2], sorted(unit["complexity"] for unit in units.values()))
        self.assertEqual(2, len(facts["types"]))
        local_run = next(unit for unit in units.values() if ".Local@" in unit["typeId"] and unit["kind"] == "nestedMethod")
        self.assertEqual(2, local_run["complexity"])
        self.assertEqual({"Example.flag()", "Example.yieldResult()"}, {method["qualifiedName"] for method in facts["methods"]})
        self.assert_field_fingerprints_exclude_nested_branches(units.values())

    def assert_field_fingerprints_exclude_nested_branches(self, units):
        for unit in units:
            if unit["kind"] == "fieldInitializer":
                self.assertNotIn("IfStmt", " ".join(unit["fingerprints"]))

    def test_ordinary_method_excludes_local_and_anonymous_initializer_bodies(self):
        facts = self.analyze({"Example.java": """class Example {
  void outer() {
    Runnable ordinaryLambda = () -> { if (flag()) work(); };
    class Local {
      int field = flag() ? 1 : 2;
      { if (flag()) work(); }
    }
    Runnable anonymous = new Runnable() {
      int field = flag() ? 1 : 2;
      { if (flag()) work(); }
      public void run() { if (flag()) work(); }
    };
  }
  boolean flag() { return true; }
  void work() {}
}"""})
        methods = {method["qualifiedName"]: method for method in facts["methods"]}
        outer = methods["Example.outer()"]
        self.assertEqual(2, outer["complexity"])
        self.assertNotIn("ConditionalExpr", " ".join(outer["fingerprints"]))
        self.assertEqual({"Example.outer()", "Example.flag()", "Example.work()"}, methods.keys())
        units = {unit["location"]["startLine"]: (unit["kind"], unit["complexity"]) for unit in facts["executables"]}
        self.assertEqual({5: ("fieldInitializer", 2), 6: ("instanceInitializer", 2),
                          9: ("fieldInitializer", 2), 10: ("instanceInitializer", 2),
                          11: ("nestedMethod", 2)}, units)

    def test_compact_record_constructor_uses_canonical_parameter_signature(self):
        files = {"Pair.java": """import java.util.List;
record Pair(List<String> values, String... labels) {
  Pair {
    Runnable callback = () -> { if (labels.length == 0) System.out.println(values); };
    if (values.isEmpty()) callback.run();
  }
  Pair(List<String> values) { this(values, ""); }
}"""}
        facts = self.analyze(files)
        self.assertEqual([], facts["syntaxGaps"])
        compact = next(method for method in facts["methods"] if method["location"]["startLine"] == 3)
        self.assertEqual("Pair..ctor(java.util.List,java.lang.String[])", compact["coverageSignature"])
        self.assertTrue(compact["evidence"]["identity"])
        self.assertEqual(2, compact["complexity"])
        self.assertEqual([("lambda", 2)], [(unit["kind"], unit["complexity"]) for unit in facts["executables"]])
        source = self.analyze(files, source_only=True)
        compact = next(method for method in source["methods"] if method["location"]["startLine"] == 3)
        self.assertEqual("Pair..ctor(List<String>,String[])", compact["qualifiedName"])
        self.assertIsNone(compact["coverageSignature"])

    def test_method_references_preserve_helper_identity(self):
        facts = self.analyze({"Example.java": "class Example { void helper() {} void exercise() { Runnable action = this::helper; action.run(); } }"})
        methods = {method["qualifiedName"]: method for method in facts["methods"]}
        self.assertIn("pom.xml#main::Example.helper()", methods["Example.exercise()"]["calls"])

    def test_dependency_diagnostics_locate_references_without_treating_package_prefixes_as_types(self):
        files = {"src/main/java/Example.java": """class Example {
    java.util.List<String> values;
    void exercise() {
        java.util.function.Function<String, Integer> action = String::missing;
        values.missing(
            3);
    }
}
"""}
        facts = self.analyze(files)
        diagnostics = [diagnostic for diagnostic in facts["diagnostics"] if diagnostic["code"].startswith("java.unresolved_")]
        self.assertEqual([
            ("java.unresolved_call", "values.missing", 5, 6),
            ("java.unresolved_method_reference", "String::missing", 4, 4),
        ], [(diagnostic["code"], diagnostic["construct"], diagnostic["location"]["startLine"],
             diagnostic["location"]["endLine"]) for diagnostic in diagnostics])

    def test_maven_test_helpers_are_test_support_without_test_annotations(self):
        facts = self.analyze({"src/test/java/demo/Checks.java": "package demo; class Checks { void check(int value) { assert value > 0; } }"})
        self.assertTrue(facts["types"][0]["isTest"])
        self.assertFalse(facts["methods"][0]["isTest"])
        self.assertEqual(1, facts["methods"][0]["assertions"])

    def test_analyzer_dependencies_do_not_leak_into_target_symbol_resolution(self):
        facts = self.analyze({"Example.java": "import com.google.gson.Gson; class Example { Gson value; }"})
        self.assertFalse(facts["semanticComplete"])
        self.assertFalse(facts["types"][0]["complete"])

    def test_standard_xml_api_remains_resolvable(self):
        facts = self.analyze({"Example.java": "import org.w3c.dom.Document; class Example { Document value; }"})
        self.assertFalse(facts["semanticComplete"])

    def test_incompatible_wire_version_is_rejected_before_analysis(self):
        process = subprocess.run(["java", "-jar", str(JAR)],
                                 input=json.dumps({"root": ".", "files": [], "schemaVersion": "future"}),
                                 text=True, capture_output=True, timeout=30)
        self.assertEqual(1, process.returncode)
        self.assertEqual("", process.stdout)
        self.assertIn("Unsupported snapshot schemaVersion", process.stderr)


class CoverageWorkerTests(unittest.TestCase):
    def setUp(self):
        temporary = tempfile.TemporaryDirectory()
        self.addCleanup(temporary.cleanup)
        self.root = Path(temporary.name)
        self.classes = self.root / "classes"
        self.report = self.root / "merged.xml"
        source = self.root / "Example.java"
        source.write_text("class Example { int choose(boolean first) { return first ? 1 : 2; } }")
        writer = self.root / "WriteProbes.java"
        writer.write_text("""package cqc;
import java.nio.file.*;
import java.util.Arrays;
import org.jacoco.core.data.ExecutionData;
import org.jacoco.core.internal.data.CRC64;
import org.jacoco.core.tools.ExecFileLoader;
class WriteProbes {
    public static void main(String[] args) throws Exception {
        var bytes = Files.readAllBytes(Path.of(args[0]));
        var data = new ExecutionData(CRC64.classId(bytes), "Example", ProbeLayout.count(bytes) + Integer.parseInt(args[2]));
        Arrays.fill(data.getProbes(), true);
        var loader = new ExecFileLoader();
        loader.getExecutionDataStore().put(data);
        loader.save(Path.of(args[1]).toFile(), false);
    }
}
""")
        subprocess.run(["javac", "-cp", str(JAR), "-d", str(self.classes), str(source), str(writer)], check=True)

    def data(self, name, extra_probes=0):
        output = self.root / (name + ".exec")
        subprocess.run(["java", "-cp", f"{JAR}{os.pathsep}{self.classes}", "cqc.WriteProbes",
                        str(self.classes / "Example.class"), str(output), str(extra_probes)], check=True)
        return output

    def check(self, inputs, *, local_report=False):
        request = {
            "owners": [{"module": "api/pom.xml", "classes": str(self.classes), "localReport": local_report,
                        "report": str(self.report)}],
            "inputs": inputs,
        }
        process = subprocess.run(["java", "-cp", str(JAR), "cqc.CoverageMain"], input=json.dumps(request), text=True,
                                 capture_output=True, timeout=30, check=True)
        return json.loads(process.stdout)

    @staticmethod
    def input(module, data, **details):
        return {"module": module, "data": str(data), "agentVersion": "0.8.15",
                "visibleModules": ["api/pom.xml"], **details}

    def class_archive(self, name, entry="Example.class"):
        path = self.root / name
        with zipfile.ZipFile(path, "w") as archive:
            archive.write(self.classes / "Example.class", entry)
        return path

    def test_valid_downstream_probes_survive_corrupt_incompatible_and_unsupported_inputs(self):
        corrupt = self.root / "corrupt.exec"
        corrupt.write_bytes(b"not JaCoCo")
        valid = self.data("valid")
        result = self.check([
            self.input("good/pom.xml", valid, agentVersion="0.8.14"),
            self.input("wrong-probe-count/pom.xml", self.data("mismatch", extra_probes=1)),
            self.input("corrupt/pom.xml", corrupt),
            self.input("future-version/pom.xml", valid, agentVersion="0.8.16"),
        ])
        self.assertEqual(["api/pom.xml"], result["reports"])
        self.assertEqual({"java.coverage_exec_invalid", "java.coverage_probe_mismatch", "java.coverage_agent_unsupported"},
                         {diagnostic["code"] for diagnostic in result["diagnostics"]})
        branch = ET.parse(self.report).find(".//method[@name='choose']/counter[@type='BRANCH']")
        self.assertEqual({"type": "BRANCH", "missed": "0", "covered": "2"}, branch.attrib)

    def test_competing_versioned_class_is_ambiguous(self):
        jar_path = self.class_archive("competing.jar", "META-INF/versions/17/Example.class")
        result = self.check([self.input("client/pom.xml", self.data("same"), competingPaths=[str(jar_path)])])
        self.assertEqual([], result["reports"])
        self.assertFalse(self.report.exists())
        self.assertEqual(["java.coverage_owner_ambiguous"], [diagnostic["code"] for diagnostic in result["diagnostics"]])

    def test_jar_only_reactor_owner_is_attributable(self):
        jar_path = self.class_archive("api.jar")
        result = self.check([self.input("client/pom.xml", self.data("jar-only"), competingPaths=[str(jar_path)],
                                       productionPaths={"api/pom.xml": [str(jar_path)]})])
        self.assertEqual(["api/pom.xml"], result["reports"])
        self.assertEqual([], result["diagnostics"])

    def test_another_owners_shaded_archive_remains_a_competitor(self):
        jar_path = self.class_archive("api.jar")
        shaded = self.class_archive("other.jar")
        result = self.check([self.input("client/pom.xml", self.data("shaded"), competingPaths=[str(jar_path), str(shaded)],
                                       productionPaths={"api/pom.xml": [str(jar_path)], "other/pom.xml": [str(shaded)]})])
        self.assertEqual([], result["reports"])
        self.assertEqual(["java.coverage_owner_ambiguous"], [diagnostic["code"] for diagnostic in result["diagnostics"]])
        self.assertIn(str(shaded), result["diagnostics"][0]["message"])

    def test_versioned_entry_alone_does_not_prove_production_ownership(self):
        jar_path = self.class_archive("api.jar", "META-INF/versions/17/Example.class")
        result = self.check([self.input("client/pom.xml", self.data("nonmatching"), competingPaths=[str(jar_path)],
                                       productionPaths={"api/pom.xml": [str(jar_path)]})])
        self.assertEqual([], result["reports"])
        self.assertEqual(["java.coverage_class_mismatch"], [diagnostic["code"] for diagnostic in result["diagnostics"]])

    def test_overlay_leaves_local_report_to_the_caller_when_local_probes_are_missing(self):
        result = self.check([self.input("client/pom.xml", self.data("downstream"))], local_report=True)
        self.assertEqual([], result["reports"])
        self.assertFalse(self.report.exists())
        self.assertEqual(["java.coverage_local_unavailable"], [diagnostic["code"] for diagnostic in result["diagnostics"]])


if __name__ == "__main__":
    unittest.main()
