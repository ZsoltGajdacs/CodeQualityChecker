---
id: TASK-24
title: Fix CQC static review scoring and diagnostics
status: Done
assignee:
  - '@codex'
created_date: '2026-11-24T16:32:00Z'
updated_date: '2026-11-24T16:41:00Z'
labels: []
dependencies: []
type: bug
ordinal: 23000
---

## Description

<!-- SECTION:DESCRIPTION:BEGIN -->
Address the four static review findings from the repository review: misleading default components for nested types, partial-file scoring contamination, missing diagnostic detail, and duplicated family-selection policy.
<!-- SECTION:DESCRIPTION:END -->

## Acceptance Criteria
<!-- AC:BEGIN -->
- [x] #1 Default architecture components use declared C# namespaces and Java packages while preserving configured overrides.
- [x] #2 Partial-type file reports use file-local scoring evidence and class reports aggregate across parts.
- [x] #3 Source-only diagnostics keep actionable reasons and available source ranges.
- [x] #4 Analysis policy exclusively selects source-only families and MCP scopes keep their contracts.
<!-- AC:END -->

## Definition of Done
<!-- DOD:BEGIN -->
- [x] #1 Run focused regressions, repository checks, packaged source-only smoke, and CodeScene safeguards.
<!-- DOD:END -->

## Implementation Plan

<!-- SECTION:PLAN:BEGIN -->
1. Add regression coverage for component assignment, partial-file evidence, diagnostics and family dispatch.
2. Implement component resolver, evidence attribution, diagnostic projection and static dispatch refactors.
3. Update ruleset and shared review skill. Verify through tests, package smoke and CodeScene. Keep installed plugins outside scope.
<!-- SECTION:PLAN:END -->

## Implementation Notes

<!-- SECTION:NOTES:BEGIN -->
1. Reproduced C# component cycles, partial-file score suppression, generic diagnostics, and fixed-family dispatch with focused regressions. Used the pinned .NET 10.0.403 SDK and local IPC approval for the test runner.
2. Implemented four fixes and reviewed attribution and diagnostic metadata. Kept repository dependency deductions tied to published locations and retained distinct analyzer messages.
3. Verified 247 .NET tests on the final diff, 31 Java worker tests, 15 TypeScript worker tests, and 20 package and installer tests. Verified the fresh macOS package in a temporary copy and ran both fixed-scope tools on the checkout with execute=false. CodeScene passed for 26 eligible files. SonarMCP is unconfigured. Installed plugins were outside scope.
<!-- SECTION:NOTES:END -->

## Final Summary

<!-- SECTION:FINAL_SUMMARY:BEGIN -->
Default components use declared namespaces or packages. Partial-file assessments use file-local evidence, source diagnostics retain reasons and ranges, and the analysis policy owns family selection. Verified all regressions, repository checks, packaged MCP smoke, and CodeScene.
<!-- SECTION:FINAL_SUMMARY:END -->
