# Architecture and language extension contract

The MCP host is .NET 10 with the official MCP C# SDK. `Cqc.Core` owns discovery, scope selection, normalized evidence, execution orchestration, shared rules, and scoring. `Cqc.Server` owns schema-v3 MCP summaries, report storage, pagination, and byte budgets. `Cqc.CSharp` uses Roslyn. `Cqc.Java` and `Cqc.TypeScript` invoke separate JavaParser and TypeScript compiler workers through private JSON protocols. Each adapter owns its language behavior and any supported build or coverage integration.

The processing pipeline is:

1. Validate the request/configuration and discover every supported source/module.
2. Capture source/build-input and configuration fingerprints.
3. Collect pinned local Git history when hotspots are requested. Optionally execute project tools in the checkout and capture reference/coverage evidence.
4. Parse the full repository through each applicable language adapter.
5. Qualify identities with a language prefix and merge immutable facts.
6. Evaluate shared rules with full repository context.
7. Filter reported entities to selected files and return deterministic ordered data.
8. Store the immutable report and its detail indexes outside the target repository, then return a bounded MCP summary.

Repository context remains available for cross-file dependencies, clone groups, helpers, and partial classes when only one file is selected. Method identities include the declaring type and parameter signature. C# identities also include generic method arity and the owning project path when a project owns the source. Partial declarations merge within their owning project and retain their separate source locations.

The C# adapter groups files by project and builds a Roslyn compilation for each group. Supported project references link the compilations in dependency order. The symbol identity component maps resolved symbols back to their owning projects so helpers and type edges can cross project boundaries. Loose files use their own context. External reference metadata and incomplete analysis diagnostics are scoped by project.

The TypeScript adapter emits one source-module entity per authored `.ts` or `.tsx` file for complexity, duplication, and dependencies. It also supplies authored class declarations for hotspot scoring. The worker supplies executable ranges and structural fingerprints for functions, components, hooks, class members, nested callbacks, and module-level code. Compiler resolution supplies module edges for imports and re-exports, including type-only imports and literal dynamic imports.

## MCP report delivery

Tool handlers delegate to focused components for typed report projections, storage/lifetime management, cursor pagination, and bounded serialization. These components remain in the server; analysis engines and language adapters do not depend on MCP contracts or pagination. Summary projection does not alter scores, findings, diagnostic messages, or original metric values.

Inspection and analysis always return schema-v3 summaries through `structuredContent`; text content is a short explanation. `get_report_page` reads indexed sections of the completed snapshot without parsing the whole report on each request or rerunning analysis. Entity lists omit full metrics, which are available separately. Raw pages reconstruct the complete canonical report JSON and provide a lossless escape hatch for oversized individual records.

The complete serialized tool result, including escaping and text, is limited to 512 KiB. Page boundaries follow both a record limit and the byte budget. IDs are random and cursors are opaque and bound to the report, section, and filters. Published reports have a 60-minute lifetime and limits of 10 reports and 512 MiB including indexes. The store can stage one additional report up to 512 MiB before replacing older reports. A failed or canceled staging operation preserves existing unexpired reports. IDs become invalid after expiry, eviction, or restart. Source-only operations do not create report files in the target repository.

## Add a language module

Implement `ILanguageAnalyzer` with `Language` and `AnalyzeAsync(ProjectSnapshot, CancellationToken)`, plus `ILanguageDiscovery.Definition`. The module's `LanguageDefinition` declares source extensions, manifest patterns, generated-file suffixes, manifest interpretation, optional build-input patterns, and `BuildTools` prerequisite names. Register the analyzer in the host; discovery and prerequisite inspection consume its definition without adding language-specific source/manifest/tool switches to Core. Language modules ship with server releases; external plugin discovery and dynamic downloads are not supported.

An adapter returns `AnalysisFacts`:

- `TypeFact`: qualified identity, language, declaration location, abstraction/test classification, and completeness (including declarations without methods).
- `ClassDeclarationFact`: production class identity, location, constructor arity, and completeness for hotspot scoring.
- `MethodFact`: overload-safe identity, declaring type, range, complexity, executable lines, test/fixture/parameterized flags, assertions, resolved helper call IDs, normalized fingerprints/node count, completeness, and optional language-specific coverage signature.
- `DependencyFact`: resolved source type relationship and evidence location.
- `Diagnostic`: stable code, actionable message, optional file and language.
- `SemanticComplete`: whether type dependency resolution is complete.

Keep language-specific parsing, symbol resolution, normalization, test-framework recognition, and coverage-name mapping in adapters. Keep score policy, graph traversal, clone grouping, helper accounting, and reporting in shared engines. Do not execute project tools from source-only adapter paths.

Implement the optional `ILanguageExecution` integration for explicitly authorized builds. `ExecuteAsync(ProjectSnapshot, runDirectory, CancellationToken)` builds in the snapshot root and returns `LanguageBuildResult` containing the language, staged manifest name, report paths, and tool versions. `ReadCoverage(LanguageBuildResult, IEnumerable<MethodFact>)` converts native reports into exact normalized `MethodCoverage` counters. `BuildExecutor` dispatches drivers and owns the run directory. Each module owns build commands, reference collection, report interpretation, and ambiguity handling. `AnalysisService` analyzes the staged manifests and publishes through `EvidenceStore` after verifying the source fingerprint. A new language does not add another language switch to the shared rule engines or MCP tools.

Set `LanguageDefinition.ParticipatesInExecution` to false for analysis-only languages. Those modules remain visible during inspection and mixed-language analysis. Discovery records source language ownership for scoped structural diagnostics. Adapters mark fatal worker diagnostics and supply any association between a syntax gap and a language-specific diagnostic.

The Java worker reads one `ProjectSnapshot` JSON object from stdin and emits one `AnalysisFacts` object on stdout. Diagnostics belong in the returned facts or stderr. Enum/property naming follows the shared web JSON contract. `CQC_JAVA_WORKER` can override the worker JAR during development; installed packages use `java-worker.jar` beside the host and `runtime/bin/java` before host Java discovery.

The TypeScript worker uses the same stdin/stdout protocol shape with facts schema 2. The package places it under `typescript-worker/` and launches it with `node-runtime/bin/node`. Development uses Node 24 from `PATH` and the worker's locked compiler dependency.

Every new module needs real fixture coverage for identities, language constructs, source ranges, unresolved semantics, test helpers, clone normalization, source-only operation, coverage mapping, cancellation, and repeatability. Unknown constructs and unresolved symbols must produce honest incompleteness rather than a verified score of 10.

## Initial analysis limits

Java compact record constructors and initializer blocks currently produce explicit diagnostics and make their owning type incomplete rather than silently disappearing from a healthy score. Unresolved generated APIs and ambiguous dependency versions also prevent affected complete assessments.

C# source options recognize static `LangVersion`/`DefineConstants` settings and ordinary DEBUG, TRACE, and target-framework symbols. Custom conditional configuration, imports/targets, custom compile-file selection, and multi-targeting that cannot be confidently evaluated produce explicit incomplete assessments. Source-only parsing does not execute MSBuild targets, source generators, or custom project tooling to infer configuration. Explicit execution can collect supported project reference evidence; it does not turn unsupported analysis constructs into a verified score.

## Determinism

For fixed source/build inputs, configuration, evidence, and tool versions, findings and scored results are reproducible. Paths are repository-relative; output sorting is ordinal; IDs are hashes of rule and entity/group identity. Results include schema/ruleset versions and fingerprints. Deterministic heuristics remain heuristics: recommendations require agent judgment.

## Evidence and responsibility boundaries (ruleset 3)

Adapters extract syntax measurements independently from semantic resolution and attach explicit evidence to facts. Core selects assessment evidence for the requested scope. Each family owns applicability, blockers, and metrics for that selection. Entity reporting renders and orders the resulting class and file scores. `GitHotspotHistory` collects local history before rule evaluation. The hotspot family owns its weighted score and uses the shared entity projection. `CoverageCollection` owns coverage provenance and report conversion. `EvidenceStore` publishes the staged coverage and reference manifests in a single atomic bundle. Static complexity shares complexity finding IDs with the combined assessment.

The Maven metadata extension observes evaluated projects in explicitly authorized Maven builds. `JavaBuildRunner` owns invocation. `JavaContextCache` stages ordered manifests that identify the existing binaries by path and hash. The worker separates manifest validation, artifact loading, fallback discovery and conflict detection from parser-context setup. Symbol ownership is canonicalized against reachable module/source-set contexts; explicit qualified names remain available for component patterns. Source-only fallback never establishes verified semantic evidence.
