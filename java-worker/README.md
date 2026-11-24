# Java fact worker

Build the pinned shaded JavaParser worker independently of the analyzed project's Maven settings:

```sh
mvn -B -s java-worker/settings.xml -f java-worker/pom.xml package
python3 java-worker/test_worker.py
```

The Java 17-compatible executable JAR reads one JSON `ProjectSnapshot` from stdin and writes one `AnalysisFacts` JSON object to stdout. Errors go to stderr. It does not invoke Maven, Gradle, javac, or tests. Gson ignores snapshot fields it does not need; the .NET adapter validates the result against the shared contracts.

All source files in `files` supply context. Filtering reported results by `selectedFiles` is the host's responsibility. Schema 2 identities include the owning Maven context: `module/pom.xml#main::package.Type.method(qualified.ParameterType)` and `module/pom.xml#test::package.Type..ctor(...)`. The host adds the language prefix. `qualifiedName` and erased `coverageSignature` remain unqualified; coverage is matched inside its owning module. Source ranges are one-based and inclusive.

Verified resolution uses `.cqc/java-contexts.json` schema 2. It binds the snapshot fingerprint to ordered, SHA-256 checked artifacts, evaluated source roots, main/test source sets, and reachable reactor contexts. Every context has its own resolver. Main sources cannot see their own test sources, test-scoped dependencies, or unrelated modules. An explicitly declared compile-scoped dependency on another module’s attached test-JAR can expose that foreign test context; evaluated metadata records this exception. Different bytecode for the same class entry invalidates only the affected context. Disjoint classifier JARs and identical duplicate class definitions are valid; module descriptors are excluded and multi-release entries retain their distinct versioned paths. Generated binaries support resolution without becoming authored analysis targets. Missing or stale artifacts invalidate semantic evidence, while syntax measurements remain available.

Without verified metadata the worker infers roots from packages and inspects directly declared cached POM dependencies as a conservative fallback. This fallback never establishes complete semantic scores. Legacy flat classpath files are not reused. No fallback invokes Maven. Parse failures produce syntax gaps with source ranges when the parser supplies them. Evidence records syntax, identity, call and assertion, dependency, and context completeness independently.

The host locates `java-worker.jar` beside its executable (or via `CQC_JAVA_WORKER`), and uses the bundled `runtime/bin/java` when available. Development builds can use the repository's `java-worker/target/java-worker.jar` and host Java. No target-project build runs in this adapter.

The worker emits source executable facts for field initializers, static and instance blocks, enum constant arguments and bodies, and nested executable code inside initialization. Source positions disambiguate executable IDs. Compact record constructors use the normal method contract and derive their coverage signature from the record components. The other initializer facts carry structural measurements independently of coverage.

Structural fingerprints retain AST shape, operators, and method names while replacing local names and literal values with node categories. The core owns similarity, grouping, thresholds, and scoring; the worker emits measurements only.
