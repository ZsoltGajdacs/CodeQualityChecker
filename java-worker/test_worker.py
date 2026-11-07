"""Behavioral checks against the actual JavaParser worker process (standard library only)."""
import json
import io
from pathlib import Path
import subprocess
import tempfile
import unittest
import zipfile


JAR = Path(__file__).parent.resolve() / "target/java-worker.jar"


class JavaWorkerTests(unittest.TestCase):
    def analyze(self, files):
        with tempfile.TemporaryDirectory() as directory:
            root = Path(directory)
            for name, source in files.items():
                path = root / name
                path.parent.mkdir(parents=True, exist_ok=True)
                if isinstance(source, bytes):
                    path.write_bytes(source)
                else:
                    path.write_text(source)
            snapshot = {"root": directory, "files": sorted(files), "sourceFingerprint": "fixture", "schemaVersion": "1"}
            process = subprocess.run(["java", "-jar", str(JAR)], input=json.dumps(snapshot),
                                     text=True, capture_output=True, timeout=30, check=True)
            self.assertEqual("", process.stderr)
            self.assertEqual("1", json.loads(process.stdout)["schemaVersion"])
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
        self.assertTrue(facts["semanticComplete"], facts["diagnostics"])
        methods = {method["id"]: method for method in facts["methods"]}
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
        methods = {method["id"]: method for method in facts["methods"]}
        self.assertEqual(1, methods["demo.Example.helper(int)"]["assertions"])
        self.assertEqual(["demo.Example.helper(int)"], methods["demo.Example.exercise()"]["calls"])
        self.assertTrue(methods["demo.Example.exercise()"]["complete"])

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

    def test_deterministic_facts(self):
        source = {"Example.java": "class Example { int value(int x) { return x + 1; } }"}
        self.assertEqual(self.analyze(source), self.analyze(source))

    def test_conflicting_maven_coordinates_never_receive_complete_scores(self):
        def jar(version):
            output = io.BytesIO()
            with zipfile.ZipFile(output, "w") as archive:
                archive.writestr("META-INF/maven/demo/library/pom.properties",
                                 f"groupId=demo\nartifactId=library\nversion={version}\n")
            return output.getvalue()
        facts = self.analyze({
            "Example.java": "class Example { int value() { return 1; } }",
            ".cqc/one.jar": jar("1"), ".cqc/two.jar": jar("2"),
            ".cqc/java-classpath.txt": ".cqc/one.jar\n.cqc/two.jar\n",
            ".cqc/java-classpath.txt.source": "fixture",
        })
        self.assertFalse(facts["semanticComplete"])
        self.assertFalse(facts["methods"][0]["complete"])
        self.assertTrue(any(d["code"] == "java.classpath_conflict" for d in facts["diagnostics"]))

    def test_generic_coverage_alias_uses_erased_parameters(self):
        facts = self.analyze({"Generic.java": """import java.util.List;
class Generic<T> {
    void accept(List<String> values, T item) { }
}
"""})
        method = facts["methods"][0]
        self.assertEqual("Generic.accept(java.util.List<java.lang.String>,T)", method["id"])
        self.assertEqual("Generic.accept(java.util.List,java.lang.Object)", method["coverageSignature"])

    def test_static_imported_fields_have_semantic_dependency_edges(self):
        facts = self.analyze({
            "demo/Values.java": "package demo; public class Values { public static int VALUE = 3; }",
            "demo/Client.java": "package demo; import static demo.Values.VALUE; class Client { int read() { return VALUE; } }",
        })
        self.assertTrue(facts["semanticComplete"], facts["diagnostics"])
        self.assertTrue(any(edge["from"] == "demo.Client" and edge["to"] == "demo.Values" for edge in facts["dependencies"]))

    def test_unmodeled_executable_bodies_make_the_owning_type_incomplete(self):
        facts = self.analyze({"Example.java": "class Example { static { System.out.println(1); } int value() { return 1; } }"})
        self.assertFalse(facts["types"][0]["complete"])
        self.assertTrue(any(d["code"] == "java.unmodeled_executable" for d in facts["diagnostics"]))

    def test_method_references_preserve_helper_identity(self):
        facts = self.analyze({"Example.java": "class Example { void helper() {} void exercise() { Runnable action = this::helper; action.run(); } }"})
        methods = {method["id"]: method for method in facts["methods"]}
        self.assertIn("Example.helper()", methods["Example.exercise()"]["calls"])

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
        self.assertTrue(facts["semanticComplete"], facts["diagnostics"])

    def test_incompatible_wire_version_is_rejected_before_analysis(self):
        process = subprocess.run(["java", "-jar", str(JAR)],
                                 input=json.dumps({"root": ".", "files": [], "schemaVersion": "future"}),
                                 text=True, capture_output=True, timeout=30)
        self.assertEqual(1, process.returncode)
        self.assertEqual("", process.stdout)
        self.assertIn("Unsupported snapshot schemaVersion", process.stderr)


if __name__ == "__main__":
    unittest.main()
