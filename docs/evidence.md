# Evidence and controlled execution

Without execution, `check_code_quality` and `check_uncommitted_changes_quality` use source and static project configuration. They bypass cached reference and coverage evidence and publish supported static complexity and duplication assessments. Analysis-only calls to `check_all` and the individual family tools can read matching cached evidence. When evidence is missing, those reports retain available facts and identify the affected assessments. Analysis-only calls never invoke project builds, tests, restores, or MSBuild evaluation. Git handles ignore and changed-file discovery.

`execute: true` explicitly permits running project-controlled Maven/.NET commands and restores. The executor copies the project into a temporary workspace, performs builds/tests there, collects reports and resolved references, and deletes the temporary workspace afterward. Source/configuration in the original root remains unchanged; reusable CQC metadata is written under `.cqc/`.

The Java integration runs Maven tests and captures module-specific main/test resolution metadata. It reuses a JaCoCo agent that the project schedules before Surefire, or adds a fallback agent for modules without one. CQC generates a module-owned report with the selected plugin version and the evaluated execution-data path. Skipped instrumentation, ambiguous data ownership, and unavailable reports leave the affected coverage incomplete. For each build root it prefers the nearest Maven Wrapper within the project root, otherwise it uses Maven on `PATH`; a broken wrapper is reported rather than bypassed. Nested declared reactor modules run with their aggregator, while independent projects run separately. Wrapper configuration is part of the source fingerprint. Project-generated JAR dependencies needed after execution are copied into the CQC reference cache; temporary reactor class directories are not retained. The .NET integration runs VSTest with pinned Coverlet collector and obtains resolved references through explicit MSBuild evaluation. A Java SDK and either a working wrapper or host Maven are required for Maven execution; toolchain discovery does not install them. Projects with incompatible test platforms, missing dependencies, or unsupported integrations report failures and incomplete scores.

Project execution can perform any actions authorized by the project's build/test code. An isolated copy protects original tracked inputs from routine build edits; it is not an operating-system sandbox. Network access can be used by Maven/NuGet restores through their normal configured repositories.

## Coverage provenance

Generated evidence is `.cqc/coverage.json`, a JSON `CoverageEvidence` object with:

- Schema version `"2"`.
- Source fingerprint covering source files and recognized build inputs.
- Effective configuration fingerprint.
- Successful execution status and collected tool versions.
- Per-method coverage counters: covered/total branches and covered/total executable lines.

The counters must have valid nonnegative bounds and unique, exact method identities. The production scorer requires evidence for every analyzed production method and returns covered/total branch and line counters alongside the derived coverage fraction. Source/configuration mismatches, failed execution, missing methods, invalid counters, and unsupported schema versions prevent affected scores. Branchless methods require executable-line evidence.

Coverage mapping preserves overload identities. Java generic signatures use erased coverage aliases. C# matches OpenCover modules to production outputs, then matches methods within the owning project using their native signature or unambiguous source points. Complementary OpenCover branch and line points from multiple test-project reports are unioned before calculating counters. Ambiguous entries leave coverage unavailable.

C# evidence records the method identity version alongside tool versions. Older C# counters require explicit execution to refresh them. Current Java counters in the same evidence file can still be reused. During evidence-aware analysis, the C# adapter checks the source fingerprint of the per-project reference manifest at `.cqc/csharp-references.json` and can read local NuGet asset metadata. Legacy flat or stale manifests produce a refresh diagnostic. Projects targeting a different framework from the CQC host need captured references for verified semantic assessments.

`reportPath` lets `check_all` and the individual family tools read an existing CQC evidence file. Directly supplying a raw coverage XML file does not establish its source provenance. Keep CQC artifacts together with their original source and configuration. Changes require recollection.

The server rechecks fingerprints after execution and analysis. Concurrent input changes invalidate evidence. Calls for the same project root are serialized; request cancellation and the configured timeout propagate to language workers and project processes. Timeouts/cancellation terminate the process tree and cannot yield successful fresh evidence.

Do not treat an empty result as a verified clean project without checking statuses and diagnostics. `notApplicable` means the family has no recognized applicable entities; `incomplete` means evidence or analysis is insufficient. A 10 is emitted only for a complete applicable assessment with no scored findings.

## Module context provenance (schema 2)

The bundled Maven metadata collector observes evaluated projects during explicit isolated execution. Main/test contexts retain evaluated source roots, ordered classpaths and reactor ownership. Class-directory outputs are cached as JARs before deleting the isolated copy; generated outputs support resolution and are not authored scoring targets. The context manifest is published atomically only after successful collection. Reuse checks the source fingerprint and every referenced artifact hash.

`.cqc/java-contexts.json` replaces the flat `java-classpath.txt` cache. Different versions in separate contexts do not create repository-wide conflicts. For evidence-aware analysis without verified contexts, the worker supplies best-effort semantic findings and leaves the corresponding scores unavailable. Legacy or stale metadata is rejected with refresh diagnostics. Coverage schema 1 is rejected. The private Java worker uses schema 3 for the dedicated source-only review mode and schema 2 for evidence-aware analysis. Persisted coverage and module context evidence use schema 2.

Main contexts exclude Maven test-scope dependencies. An explicitly declared compile-scope dependency on another module's attached test JAR is visible according to Maven's evaluated classpath; it does not expose the caller's own test sources. Duplicate binary conflicts compare overlapping class definitions, so disjoint main/test artifacts with shared Maven coordinates are valid.
