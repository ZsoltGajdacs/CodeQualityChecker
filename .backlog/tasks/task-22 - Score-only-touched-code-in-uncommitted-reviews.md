---
id: TASK-22
title: Score only touched code in uncommitted reviews
status: Done
assignee:
  - '@codex'
created_date: '2026-11-24T15:03:00Z'
updated_date: '2026-11-24T15:15:00Z'
labels: []
dependencies: []
type: enhancement
ordinal: 21000
---

## Description

<!-- SECTION:DESCRIPTION:BEGIN -->
Uncommitted review currently reports findings from untouched methods when another method in the same file changes.
<!-- SECTION:DESCRIPTION:END -->

## Acceptance Criteria
<!-- AC:BEGIN -->
- [x] #1 Both uncommitted review modes count findings and deductions only for Git-touched code and review new files in full.
- [x] #2 Clone counterparts and diagnostics retain necessary repository context, and repository and legacy reviews preserve existing behavior.
- [x] #3 Real Git and MCP regressions, documentation, Code Health review, and safeguard pass.
<!-- AC:END -->

## Implementation Plan

<!-- SECTION:PLAN:BEGIN -->
1. Reproduce the issue at the MCP boundary.
2. Read working-tree hunks against HEAD; preserve full analysis and filter evidence before aggregation.
3. Cover staged, unstaged, deletion, new files, clones, and execution; update shared documentation and verify quality gates.
<!-- SECTION:PLAN:END -->

## Implementation Notes

<!-- SECTION:NOTES:BEGIN -->
The regression reproduced two complexity findings against the saved original server and reports one against the new server.
Added working-tree Git ranges and an internal evidence projection. Scoped clone ownership, partial-class file scores, and repeated dependency references were checked independently with temporary Git fixtures. The first full solution run passed 234 tests.
The safeguard flagged a public record change. Moved clone ownership into internal assessment evidence and started a fresh test and safeguard run. Sonar is inactive because this repository has no .sonar/config.json.

Final solution rerun passed all 234 tests. CodeScene reviewed the changed source files, and its pre-commit safeguard passed for all 20 eligible files. Independent stdio MCP probes verified partial-class duplicate attribution, TypeScript import scoping, and repeated C# dependency references.
<!-- SECTION:NOTES:END -->

## Final Summary

<!-- SECTION:FINAL_SUMMARY:BEGIN -->
Uncommitted reviews now project Git-touched evidence before scoring in both modes, while retaining repository context and clone counterpart locations. New paths are reviewed in full. Updated the MCP description and review guidance. All 234 solution tests and the CodeScene safeguard passed. Changes are in the working tree.
<!-- SECTION:FINAL_SUMMARY:END -->
