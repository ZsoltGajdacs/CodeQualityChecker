---
id: TASK-15
title: Separate project and uncommitted CQC reviews
status: Done
assignee:
  - '@codex'
created_date: '2026-11-23T21:31:00Z'
updated_date: '2026-11-23T21:40:00Z'
labels: []
dependencies: []
type: feature
ordinal: 15000
---

## Description

<!-- SECTION:DESCRIPTION:BEGIN -->
Implement fixed-scope CQC tools with source-only structural reports by default and full analysis with explicit execution.
<!-- SECTION:DESCRIPTION:END -->

## Acceptance Criteria
<!-- AC:BEGIN -->
- [x] #1 Fixed-scope tools use existing Git selection and share analysis.
- [x] #2 Source-only results are reliable and consistent across report delivery, independent of prior builds.
- [x] #3 Explicit execution runs all families and legacy tools preserve evidence behavior.
- [x] #4 Documentation, smoke tests, and CodeScene gates pass.
<!-- AC:END -->

## Implementation Plan

<!-- SECTION:PLAN:BEGIN -->
1. Route fixed scopes.
2. Separate structural and evidence-dependent analysis.
3. Publish one consistent result across report surfaces.
4. Verify and document both modes.
<!-- SECTION:PLAN:END -->

## Implementation Notes

<!-- SECTION:NOTES:BEGIN -->
Recorded the approved plan. Observed the missing-tool regression against the old server. Implemented shared fixed-scope routing, an explicit source-only policy, structural evaluation, and shared omission rules for report delivery. Core review caught duplication status and build-integration diagnostic leakage, both corrected. Code Health found complexity in the service and serialization, which was refactored and re-reviewed at 10.0.

Adapter review added explicit source-only Java worker negotiation and corrected case-insensitive Java extension handling. The worker suite passed 27 tests, and direct probes verified independence from invalid cached metadata and distinct overload IDs. First full .NET run passed 192 of 193, including explicit mixed-language execution and cache invariance. Restored the standard scoring sentence in both MCP tool descriptions to satisfy the remaining tool-inventory assertion.

Full .NET rerun passed 193 of 193. Real Git and MCP tests covered fixed schemas, working-tree selection, cross-scope duplicate locations, summary/detail/raw omission consistency, syntax failures, and explicit mixed execution with matching source-only reports before and after the run. Python package tests passed 6 of 6 and installer tests 13 of 13. Sonar is inactive because this project has no .sonar/config.json.

Fresh pinned macOS package passed all eight MCP tools with host Java and .NET absent. Codex and Claude generated plugin connections passed, and disposable CLI installs discovered the tools and review skill. Updated the discovery verifier to expect the generated manifest version. Final CodeScene safeguard passed all 34 eligible files with no issues. Working changes remain local.
<!-- SECTION:NOTES:END -->

## Final Summary

<!-- SECTION:FINAL_SUMMARY:BEGIN -->
Separated repository and uncommitted quality reviews, with shared source-only structural results and explicit all-family execution. Source-only assessments are independent of prior build evidence and omit unsupported metrics across all report surfaces. Verified 193 .NET tests, 27 Java worker tests, package and installer tests, fresh package and both plugin client smoke checks, and the CodeScene safeguard.
<!-- SECTION:FINAL_SUMMARY:END -->
