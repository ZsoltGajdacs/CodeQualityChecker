"""Behavioral checks against the actual JavaParser worker process (standard library only)."""
import json
import io
import hashlib
from pathlib import Path
import subprocess
import tempfile
import unittest
import zipfile


JAR = Path(__file__).parent.resolve() / "target/java-worker.jar"


class JavaWorkerTests(unittest.TestCase):
    def analyze(self, files, evidence_directory=None):
        with tempfile.TemporaryDirectory() as directory:
            root = Path(directory)
            for name, source in files.items():
                path = root / name
                path.parent.mkdir(parents=True, exist_ok=True)
                if isinstance(source, bytes):
                    path.write_bytes(source)
                else:
                    path.write_text(source)
            snapshot = {"root": directory, "files": sorted(files), "sourceFingerprint": "fixture", "schemaVersion": "2"}
            if evidence_directory is not None:
                snapshot["referenceEvidenceDirectory"] = str(root / evidence_directory)
            process = subprocess.run(["java", "-jar", str(JAR)], input=json.dumps(snapshot),
                                     text=True, capture_output=True, timeout=30, check=True)
            self.assertEqual("", process.stderr)
            self.assertEqual("2", json.loads(process.stdout)["schemaVersion"])
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

    def test_static_imported_fields_have_semantic_dependency_edges(self):
        facts = self.analyze({
            "demo/Values.java": "package demo; public class Values { public static int VALUE = 3; }",
            "demo/Client.java": "package demo; import static demo.Values.VALUE; class Client { int read() { return VALUE; } }",
        })
        self.assertFalse(facts["semanticComplete"])
        self.assertTrue(any(edge["from"] == "pom.xml#main::demo.Client" and edge["to"] == "pom.xml#main::demo.Values" for edge in facts["dependencies"]))

    def test_unmodeled_executable_bodies_make_the_owning_type_incomplete(self):
        facts = self.analyze({"Example.java": "class Example { static { System.out.println(1); } int value() { return 1; } }"})
        self.assertFalse(facts["types"][0]["complete"])
        self.assertTrue(any(d["code"] == "java.unmodeled_executable" for d in facts["diagnostics"]))

    def test_unmodeled_field_initializer_and_enum_body_block_syntax(self):
        for source in ["class Example { int value = choose() ? 1 : 2; boolean choose() { return true; } }",
                       "enum Example { A { int value(int x) { if (x > 0) return x; return 0; } }; int value(int x) { return 0; } }"]:
            facts = self.analyze({"Example.java": source})
            self.assertFalse(facts["types"][0]["evidence"]["syntax"])
            self.assertTrue(any(gap["code"] == "syntax.unmodeledExecutable" for gap in facts["syntaxGaps"]))
            self.assertFalse(facts["syntaxGaps"][0]["unknownEntities"])
        facts = self.analyze({"Example.java": "class Example { static final String LABEL = \"label\"; int count = -2; int value() { return count; } }"})
        self.assertTrue(facts["types"][0]["evidence"]["syntax"])

    def test_method_references_preserve_helper_identity(self):
        facts = self.analyze({"Example.java": "class Example { void helper() {} void exercise() { Runnable action = this::helper; action.run(); } }"})
        methods = {method["qualifiedName"]: method for method in facts["methods"]}
        self.assertIn("pom.xml#main::Example.helper()", methods["Example.exercise()"]["calls"])

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


if __name__ == "__main__":
    unittest.main()
