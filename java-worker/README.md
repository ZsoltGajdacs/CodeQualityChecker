# Java fact worker

Build the pinned shaded JavaParser worker independently of the analyzed project's Maven settings:

```sh
mvn -B -s java-worker/settings.xml -f java-worker/pom.xml package
python3 java-worker/test_worker.py
```

The Java 17-compatible executable JAR reads one JSON `ProjectSnapshot` from stdin and writes one `AnalysisFacts` JSON object to stdout. Errors go to stderr. It does not invoke Maven, Gradle, javac, or tests. Gson ignores snapshot fields it does not need; the .NET adapter validates the result against the shared contracts.

All source files in `files` supply context. Filtering reported results by `selectedFiles` is the host's responsibility. Symbol identities use `package.Type.method(qualified.ParameterType)` and `package.Type..ctor(...)`; nested type names use dots. Source ranges are one-based and inclusive.

Source-root inference follows package declarations. Resolution combines source, platform reflection, directly declared dependencies already present in the local Maven cache, and the explicit execution classpath at `.cqc/java-classpath.txt`. Generated classpath metadata is accepted only when `.cqc/java-classpath.txt.source` matches the snapshot source fingerprint. Missing symbols are diagnosed and never silently treated as resolved.

The host locates `java-worker.jar` beside its executable (or via `CQC_JAVA_WORKER`), and uses the bundled `runtime/bin/java` when available. Development builds can use the repository's `java-worker/target/java-worker.jar` and host Java. No target-project build runs in this adapter.

Structural fingerprints retain AST shape, operators, and method names while replacing local names and literal values with node categories. The core owns similarity, grouping, thresholds, and scoring; the worker emits measurements only.
