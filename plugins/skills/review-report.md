# CQC review reporting and follow-up

The fixed-scope tools use source and static project configuration for static complexity and duplication, skipping saved coverage and reference evidence. The summary note about execution is informational. Duplicate findings can reference unchanged counterparts. File scores use evidence attributed to that file, and partial-class scores aggregate evidence across declarations in scope. In an uncommitted review, deductions apply only to touched members and dependency findings retain each applicable touched reference. Repeated references count as one deduction per affected entity. Test helpers and fixtures contribute context for touched tests. Assertion credit comes from the test body and its reachable helpers.

When a fixed-scope review runs with `execute: true`, it runs all four families. Execution runs in the checkout and can modify it through project build or test code. Normal build outputs remain available for reuse, and CQC writes evidence to `.cqc/`. The supported integrations are Maven/JUnit 5 and SDK-style .NET/xUnit with VSTest.

## Additional checks on request

Use `inspect_project` when module or language support is unclear and more detail is needed. For a follow-up with explicit paths, a Git comparison base, saved evidence, or a specific family, use `check_all` or the individual family tool. Pass `scope: "paths"` with repository-relative `paths`, or `scope: "changed"` with `gitBase` for a Git comparison. Choose `check_test_quality` for test structure, `check_duplication` for structural clones, or `check_dependencies` for semantic cycles, coupling, and configured boundaries. With `execute: false`, these tools can reuse matching local evidence and accept `reportPath` for a CQC coverage evidence file.

For a requested `.ts` or `.tsx` dependency check, use `check_dependencies` with `execute: false` for resolved module cycles and boundaries. The report's `class` kind represents a TypeScript source module. Explain dependency blockers such as missing packages or ambiguous project configuration. TypeScript supports static complexity, duplication, and dependency analysis. Test quality and coverage are unavailable for TypeScript.

Duplication checks require at least four lines of body code. Signatures, comments, blank lines, brace-only lines, and separately analyzed nested bodies are excluded from the count. A project can raise the minimum with `duplicateMinLines`. Node-count and similarity thresholds also apply.

When diagnostics identify a legacy reference manifest or changed or missing build outputs, explain which assessments lack evidence. The current manifests verify Maven classpath entries and .NET assemblies in their existing locations. Refresh them through an authorized `execute: true` run when the requested assessment needs that evidence.

## Retrieve the completed report

Inspection and check responses contain schema-v3 summaries in `structuredContent`, including the report ID, expiry, fingerprints, counts, family statuses, score distributions, and diagnostic code counts. Use `get_report_page` to retrieve the relevant details before making location-specific claims or applying changes.

Call `get_report_page` with a nested request such as `{"reportId":"<id>","section":"findings","family":"code","limit":50}`. Available sections are `files`, `modules`, `entities`, `findings`, `diagnostics`, `metrics`, and inspection `prerequisites`. Use `family` and `entityId` only where applicable. Follow `nextCursor` until null for sections needed by the requested review, preserving the same filters. Pages default to 50 records and accept 1–200; the 512 KiB response budget can return fewer records. Entity lists show score/status and detail counts; fetch metrics separately when needed.

A non-null cursor means more records exist. Source-only production entities expose `staticComplexity`. The code family exposes `staticComplexityScores`, `scoredFileCount`, and `unscoredFileCount` with `scores` omitted. Score distributions include class and file entities. Use the file counts when explaining how much selected production code was scored. Incomplete assessments expose blockers when available. Duplication can report verified matches while an incomplete language comparison pool prevents numeric scores.

Diagnostic groups include scoped counts when available. `selectedCount` counts diagnostic records in selected files, `contextCount` counts records in other repository files, and `projectWideCount` counts repository-wide records. Omitted scope counts represent zero for that scope in scoped reports. Diagnostic pages list selected-file entries first. Use the detailed parser or configuration message, construct, and line range when available to explain incomplete static assessments. For `unsupported_language`, report the distinct repository-wide `extensions` in the summary. Use the `files` section to verify whether a particular path was selected.

For Java dependency analysis, use the location and construct on unresolved symbol diagnostics to identify the affected reference. Check classpath and context diagnostics before attributing an unresolved symbol to missing Maven evidence.

For `java.coverage_signature_ambiguous`, report the owning Maven module and the JVM signature listed in `construct`. CQC withholds the affected method's coverage when several authored methods match that entry.

For an oversized individual record or when the complete original report is needed, request `section: "raw"` without family or entity filters. Concatenate `rawChunk` strings in cursor order and parse the resulting JSON. Retrieval uses an immutable snapshot without rerunning analysis or executing project code.

Reports expire after 60 minutes, can be evicted earlier when the 10-report or 512 MiB limits are reached, and are cleared on server restart. If details are still needed, run a fresh check with the same scope and execution authorization. Report retrieval failures explicitly.

## Explain and improve

Report actionable findings with rule, file and line, consequence, and concrete improvement. Keep CQC scores separate by family and file or class. **10 is the desired score.** Keep incomplete assessments visible and explain any blocker that matters to the requested review. Report unsupported languages as unanalyzed.

A review-only request produces findings without edits. Apply improvements when requested, preserve intended behavior, and stay within the requested scope. Run appropriate existing tests under the user's execution authorization, then repeat CQC checks for the affected files and relevant families. Stop when requested improvements are verified or a concrete blocker remains.

Summarize what changed, checks and tests run, remaining findings, and verification limitations. Distinguish observed results from advice and unverified assumptions.
