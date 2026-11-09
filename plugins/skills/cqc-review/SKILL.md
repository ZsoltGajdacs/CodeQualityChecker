---
name: cqc-review
description: Review Java and C# code with the CQC MCP tools, explain quality findings, and apply and verify requested improvements. Use for CQC checks and Java/C# quality reviews; not for package vulnerability scans or unrelated languages.
---

# CQC review and improvement

Use the plugin's CQC MCP tools. Tool prefixes vary by client; identify the server by its inspection/check tools and `get_report_page` rather than assuming a particular prefix. If the server is unavailable, report the connection failure and the configured executable prerequisite. Do not invent results or silently install software or change client configuration.

## Choose the scope and checks

Establish the absolute repository root and the user's intended scope. Call `inspect_project` for supported languages, modules, integrations, and prerequisites. Report unsupported languages as unanalyzed. Distinguish source-only analysis support from unsupported build/test execution integrations; for example, Gradle Java source can be reviewed even though Gradle execution is outside the MVP.

Use `scope: "changed"` for a change review, `scope: "paths"` with repository-relative `paths` for a targeted request, and `scope: "repository"` for a repository review. Set `gitBase` only when a comparison base is requested or established. Changed scope without a base includes staged, unstaged, and untracked source files. If Git scope fails, explain it and use explicit paths only when they represent the user's intended selection.

Choose `check_code_quality` for production complexity and coverage-based CRAP, `check_test_quality` for test structure, `check_duplication` for structural clones, or `check_dependencies` for semantic cycles, coupling, and configured boundaries. Use `check_all` for a broad review. Checks assess whole selected files with repository context, so a finding may reference an unselected counterpart file.

Inspection/check tools require a nested `request`, for example:

```json
{
  "request": {
    "projectRoot": "/absolute/path/to/repository",
    "scope": "paths",
    "paths": ["src/Feature.cs"],
    "execute": false,
    "timeoutSeconds": 300
  }
}
```

Default to `execute: false`. It does not invoke project builds, tests, restores, or MSBuild evaluation. Use `execute: true` only when existing user authorization covers project-controlled restores, builds, and tests; otherwise request that authorization if execution is needed. Execution can collect evidence but does not guarantee that every semantic blocker resolves. Execution runs in an isolated copy, needs the project's toolchains, and writes reusable evidence to the original root's `.cqc/` directory. Initial execution support covers Maven/JUnit 5 and SDK-style .NET/xUnit with VSTest.

Use an existing CQC evidence file through `reportPath` when available and appropriate. Raw coverage XML alone does not establish source-matched evidence. Preserve evidence diagnostics instead of treating stale or absent evidence as success. Optional `configPath` selects CQC configuration; do not relax thresholds or exclusions merely to improve a score. `timeoutSeconds` accepts 1–3600.

## Retrieve the completed report

Inspection and check responses always contain schema-v3 summaries in `structuredContent`: report ID, expiry, fingerprints, counts, family statuses, score distributions, and diagnostic code counts. Text content is only a short explanation. A summary is not the full list of findings. Retrieve the relevant details before making location-specific claims or applying changes.

Call `get_report_page` with a nested request such as `{"reportId":"<id>","section":"findings","family":"code","limit":50}`. Available sections are `files`, `modules`, `entities`, `findings`, `diagnostics`, `metrics`, and inspection `prerequisites`. Use `family` and `entityId` only where applicable. Follow `nextCursor` until null for sections needed by the requested review, preserving the same filters. Pages default to 50 records and accept 1–200; the 512 KiB response budget can return fewer records. Entity lists show score/status and detail counts; fetch metrics separately when needed.

Pagination is separate from semantic completeness: a non-null cursor means more records exist, while an incomplete family means the analysis lacks required evidence or resolution. Keep null scores and diagnostics visible. Production entities expose a separate `staticComplexity` assessment: it evaluates syntax complexity only and is not the combined complexity/CRAP score. Summaries contain separate `scores` and `staticComplexityScores` distributions; entity pages contain blockers for each assessment. Read those blockers before recommending execution. Do not describe partial retrieval as a complete review.

For an oversized individual record or when the complete original report is needed, request `section: "raw"` without family/entity filters. Concatenate `rawChunk` strings in cursor order and parse the resulting JSON. No detail is silently discarded. Retrieval uses an immutable snapshot and does not rerun analysis or execute project code.

Reports are temporary: they expire after 60 minutes, can be evicted earlier when the 10-report/512 MiB limits are reached, and do not survive a server restart. An expired or evicted report requires a fresh check if its details are still needed; keep the user's intended scope and execution authorization. Report retrieval failures explicitly rather than inventing missing findings.

## Explain and improve

Report actionable findings with rule, file and line, consequence, and concrete improvement. Keep CQC scores separate by family and file/class: **10 is the desired score**, with no combined score. CQC scores are not CodeScene Code Health. Preserve `null` scores and incomplete statuses when evidence or semantic resolution is missing; missing coverage never establishes a perfect score. Dependency checks are not vulnerability scans. High scores do not prove functional correctness or test effectiveness.

A review-only request produces findings without edits. Apply improvements when requested, preserve intended behavior, and stay within the requested scope. Run appropriate existing tests under the user's execution authorization, then repeat CQC checks for the affected files and relevant families. Stop when requested improvements are verified or a concrete blocker remains; do not introduce an automatic commit gate or keep changing code solely to reach 10.

Summarize what changed, checks and tests run, remaining findings, and verification limitations. Distinguish observed results from advice and unverified assumptions.
