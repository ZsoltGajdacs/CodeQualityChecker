---
name: cqc-review
description: Review Java and C# code with the CQC MCP tools, explain quality findings, and apply and verify requested improvements. Use for CQC checks and Java/C# quality reviews; not for package vulnerability scans or unrelated languages.
---

# CQC review and improvement

Use the plugin's CQC MCP tools. Tool prefixes vary by client. Identify the server by `inspect_project` and `get_report_page`. If it connects but a review tool named below is missing, report that the configured server may be out of date. If the server is unavailable, report the connection failure and the configured executable prerequisite. Installation and client configuration changes require a user request.

## Choose the scope and checks

Establish the absolute repository root and the user's intended scope. Use `inspect_project` when module or language support is unclear. Report unsupported languages as unanalyzed.

- For a repository review, use `check_code_quality`.
- For “check my uncommitted changes”, use `check_uncommitted_changes_quality`. It selects staged, unstaged, and untracked source, applies normal exclusions, and reads the current working-tree contents. An empty selection means zero files. If Git scope fails, explain it. Use explicit paths only when they represent the requested selection.
- For explicit paths, a Git comparison base, or a review that needs saved evidence or a specific family, use `check_all` or an individual family tool. Pass `scope: "paths"` with repository-relative `paths`, or `scope: "changed"` with `gitBase`. Choose `check_test_quality` for test structure, `check_duplication` for structural clones, or `check_dependencies` for semantic cycles, coupling, and configured boundaries.

The two review tools take the same nested request with fixed scope.

```json
{
  "request": {
    "projectRoot": "/absolute/path/to/repository",
    "execute": false
  }
}
```

Default to `execute: false`. The fixed-scope tools use source and static project configuration for static complexity and duplication, skipping saved coverage and reference evidence. Present the results directly. The summary note about execution is informational. Findings can reference unchanged counterparts because the tools use repository context.

Use `execute: true` when the user requests execution and authorizes project-controlled restores, builds, and tests. Both review tools then run all four families. Execution runs in the checkout and can modify it through project build or test code. Normal build outputs remain available for reuse, and CQC writes evidence to `.cqc/`. The supported integrations are Maven/JUnit 5 and SDK-style .NET/xUnit with VSTest.

Optional `configPath` selects CQC configuration. Preserve the user's thresholds and exclusions. `timeoutSeconds` accepts 1–3600. With `execute: false`, `check_all` and the individual family tools can reuse matching local evidence and accept `reportPath` for a CQC coverage evidence file.

When diagnostics identify a legacy reference manifest or changed or missing build outputs, explain which assessments lack evidence. The current manifests verify Maven classpath entries and .NET assemblies in their existing locations. Refresh them through an authorized `execute: true` run when the requested assessment needs that evidence.

## Retrieve the completed report

Inspection and check responses always contain schema-v3 summaries in `structuredContent`: report ID, expiry, fingerprints, counts, family statuses, score distributions, and diagnostic code counts. Text content is only a short explanation. A summary is not the full list of findings. Retrieve the relevant details before making location-specific claims or applying changes.

Call `get_report_page` with a nested request such as `{"reportId":"<id>","section":"findings","family":"code","limit":50}`. Available sections are `files`, `modules`, `entities`, `findings`, `diagnostics`, `metrics`, and inspection `prerequisites`. Use `family` and `entityId` only where applicable. Follow `nextCursor` until null for sections needed by the requested review, preserving the same filters. Pages default to 50 records and accept 1–200; the 512 KiB response budget can return fewer records. Entity lists show score/status and detail counts; fetch metrics separately when needed.

A non-null cursor means more records exist. Source-only production entities expose `staticComplexity`. The code family exposes `staticComplexityScores`, `scoredFileCount`, and `unscoredFileCount` with `scores` omitted. Score distributions include class and file entities. Use the file counts when explaining how much selected production code was scored. Incomplete assessments expose blockers when available. Duplication can report verified matches while an incomplete language comparison pool prevents numeric scores.

Diagnostic groups include scoped counts when available. `selectedCount` counts diagnostic records in selected files, `contextCount` counts records in other repository files, and `projectWideCount` counts repository-wide records. Omitted scope counts represent zero for that scope in scoped reports. Diagnostic pages list selected-file entries first and include construct and line range when available. For `unsupported_language`, report the distinct repository-wide `extensions` in the summary. Use the `files` section to verify whether a particular path was selected.

For an oversized individual record or when the complete original report is needed, request `section: "raw"` without family/entity filters. Concatenate `rawChunk` strings in cursor order and parse the resulting JSON. No detail is silently discarded. Retrieval uses an immutable snapshot and does not rerun analysis or execute project code.

Reports are temporary: they expire after 60 minutes, can be evicted earlier when the 10-report/512 MiB limits are reached, and do not survive a server restart. An expired or evicted report requires a fresh check if its details are still needed; keep the user's intended scope and execution authorization. Report retrieval failures explicitly rather than inventing missing findings.

## Explain and improve

Report actionable findings with rule, file and line, consequence, and concrete improvement. Keep CQC scores separate by family and file/class: **10 is the desired score**, with no combined score. CQC scores are not CodeScene Code Health. For evidence-aware reports, keep incomplete assessments visible and explain any blocker that matters to the requested review. Dependency checks are not vulnerability scans. High scores do not prove functional correctness or test effectiveness.

A review-only request produces findings without edits. Apply improvements when requested, preserve intended behavior, and stay within the requested scope. Run appropriate existing tests under the user's execution authorization, then repeat CQC checks for the affected files and relevant families. Stop when requested improvements are verified or a concrete blocker remains; do not introduce an automatic commit gate or keep changing code solely to reach 10.

Summarize what changed, checks and tests run, remaining findings, and verification limitations. Distinguish observed results from advice and unverified assumptions.
