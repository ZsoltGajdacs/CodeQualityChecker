# Resolution and scoring foundations — 2026-11-09

TASK-9 implements module-owned Maven resolution and coverage, explicit family evidence requirements, a separate static-complexity assessment, and supporting method/class refactorings. Internal/worker/coverage schema is 2, report envelopes are 3, ruleset is 2, and bundled review plugins are 0.3.0. Upgrade the server, worker, collector and plugins together; reconnect existing MCP clients. Legacy evidence must be refreshed.

## Billing source-only acceptance

The packaged server ran `check_all` against Recurring-billing with `scope: paths`, `paths: ["impl"]`, `execute: false`, and `timeoutSeconds: 600`. All 607 selected files remained inside impl. All 2,220 source/POM hashes matched the before-run snapshot. Billing builds/tests were not run. Follow-up entity, diagnostic, file and inspection requests succeeded over the same MCP connection; inspection agreed with the analysis source fingerprint.

| Measurement | Recorded baseline | Updated package |
| --- | ---: | ---: |
| Analysis seconds | 48.47 | 42.562 |
| Summary tool-result bytes | 1,679 | 1,913 |
| Production complexity findings | 24 | 24 |
| Duplication groups | 295 | 295 |
| Dependency findings | 542 | 287 |
| Test findings | 366 | 232 |
| Numeric primary production scores | 0 | 0 |
| Numeric static complexity class assessments | unavailable | 231 of 361 |
| Numeric static complexity file assessments | unavailable | 211 of 337 |

Static class scores: 218 at 10, 11 at 9, and 2 at 8; another 130 classes remain incomplete. File scores: 199 at 10, 10 at 9, and 2 at 8; another 126 files remain incomplete. These are complexity-only scores, not combined complexity/CRAP or general quality scores. Production entity counts include additional executable-only incomplete types that previously had no entity.

All primary families remain incomplete. Coverage is absent; verified Maven contexts are unavailable; 391 unmodeled-executable diagnostics expose structural gaps. Source-only resolution no longer borrows arbitrary neighboring module roots: unresolved calls increased from 53,190 to 75,475, fields from 1,677 to 5,646 and types from 958 to 6,292. These counts and lower dependency/test findings are not quality improvements or a controlled performance benchmark. They reflect conservative module visibility and missing semantic evidence. Only a verified build-backed context can support the semantic comparison. No execution was requested or performed on billing.

The five unsupported-language diagnostics correspond to extensions `.js`, `.kt`, `.py`, `.ts`, `.tsx`, not five files. Full aggregate measurements and fingerprints are in [the JSON evidence](resolution-scoring-billing-2026-11-09.json). Proprietary source and full findings were not copied into this repository.

## Validation

- Full .NET acceptance suite: 117 passed, zero failures/skips. Includes real isolated Maven/.NET builds, deterministic score goldens, module/classpath isolation, generated binaries, same-named classes, classifier artifact ownership, cache corruption/recovery, cancellation/failure handling, and bounded report/raw reconstruction.
- Final focused C# evidence run after top-level applicability fix: 5 passed. The new top-level regression supplements the full suite; the other four overlap it.
- Worker behavioral suite: 23 passed. Real class bytes verify differing duplicate classes conflict, identical classes and disjoint main/test classifier artifacts do not. Tests cover module ownership, parse/initializer gaps, metadata rejection, and semantic-versus-structural evidence.
- Plugin packaging tests: 6 passed. Installer tests: 13 passed.
- macOS Apple Silicon packaging completed with the pinned JBR SDK and self-contained .NET. Packaged smoke exercised all seven tools with host Java/.NET discovery disabled and verified unchanged inputs.
- Both generated Claude/Codex plugin smoke tests passed against the final package. The generated marketplaces are under `dist/cqc-foundations-plugins/`.
- Final Code Health safeguard: passed, 70 eligible files checked, no issues. Changed eligible sources received individual reviews. `git diff --check` passed.
- Sonar remains inactive because this project has no `.sonar/config.json`.

The initial sandbox test/build attempts could not create MSBuild/VSTest local sockets or Backlog Git locks; authorized escalated retries succeeded. Worker cached builds used offline Maven before final packaging. Initial regression failures were addressed rather than waived: schema/ID expectations, collector timing, invalid-context coverage reuse, helper-owner structural evidence, delegate calls, and classifier conflicts all received focused fixes/coverage.

## Method/class structure review

AnalysisService coordinates discovery, execution, extraction and evaluation; CoverageCollection owns provenance conversion/publication. AssessmentEvidence/DependencyEvidence own pure requirement policies. EntityReport projects and deduplicates family results without a blanket completeness gate. Adapters separate syntax measurement from symbol/call resolution; missing explicit evidence fails closed.

JavaBuildRunner owns commands, the Maven EventSpy captures evaluated model data, JavaContextCache projects/publishes contexts, JavaBinaryCache retains class directories, and ReferenceBinaryCache verifies/repairs immutable content. The worker separates manifest validation, artifact checks, fallback discovery, context/parser setup, canonical ownership, type extraction and method resolution. DTOs use named properties where long positional constructors obscured responsibilities.

Independent reviewers inspected responsibility, cohesion, coupling, method control flow and shared state in addition to Code Health. Findings fixed included omitted executable initializers, coverage reused after context rejection, helper-owner syntax omissions, indirect method-group calls and packaged/test artifact ownership. No unresolved review finding or speculative broad rewrite remains. Inherited fixture discovery, Kotlin/Lombok modeling and richer triage remain existing limits/deferred work, not completed capabilities.

## Delivery

The final package is `dist/cqc-osx-arm64.tar.gz` with its checksum sidecar and directory. It includes the metadata collector. Package/plugin smoke checks ran after package replacement completed. Client settings were not changed; reconnect the server and refresh the plugin to load schema-3 guidance. AGENTS.md now includes the CodeScene instructions explicitly requested during this task. Changes are local and uncommitted; no push or deployment was performed.
