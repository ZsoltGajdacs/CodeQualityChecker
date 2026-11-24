---
id: TASK-21
title: Fix TypeScript dependency analysis review findings
status: Done
assignee:
  - '@codex'
created_date: '2026-11-24T13:42:00Z'
updated_date: '2026-11-24T14:02:00Z'
labels: []
dependencies: []
type: bug
---

## Description

<!-- SECTION:DESCRIPTION:BEGIN -->
Follow-up to TASK-20 after review of the uncommitted TypeScript feature. Fix the three dependency analysis defects. Active TypeScript checks are planned separately.
<!-- SECTION:DESCRIPTION:END -->

## Acceptance Criteria
<!-- AC:BEGIN -->
- [x] #1 Entry-point configs report cycles through transitively imported authored files with existing project reference behavior preserved.
- [x] #2 Valid exact and wildcard ambient modules keep dependency evidence complete while truly missing modules yield scoped diagnostics.
- [x] #3 Literal CommonJS require calls resolve in require mode, shadowed calls are ignored, and computed targets mark affected evidence incomplete.
- [x] #4 Worker and MCP regressions pass along with the required CodeScene safeguards.
<!-- AC:END -->

## Implementation Plan

<!-- SECTION:PLAN:BEGIN -->
1. Reproduce transitive ownership, ambient imports, and CommonJS defects in worker tests.
2. Use read-only project compiler state for transitive membership and symbol provenance while preserving existing resolution and references.
3. Exercise report behavior through MCP, run worker and .NET suites, and pass CodeScene before committing.
<!-- SECTION:PLAN:END -->

## Implementation Notes

<!-- SECTION:NOTES:BEGIN -->
The original worker passes its 11 tests and fails all four new regression cases covering transitive ownership, ambient exact/wildcard resolution, require-mode package exports and shadowing, and computed requires. The user excluded the canonical skill wording from this fix because active TypeScript checks are planned.

The new MCP regression reproduced incomplete dependency evidence on the original worker. The updated worker reported complete evidence and a cycle finding for each of the two modules; the assertion now checks both file locations. The Java worker passed 31 tests, and plugin packaging and installer suites passed 20 tests.

Independent review caught invalid exact relative ambient declarations masking unresolved imports. Added a worker regression and rejected exact relative matches while retaining wildcard matches. Independent worker verification passed 15 tests and a temporary-checkout probe. The .NET suite passed 226 tests, the final TypeScript MCP rerun passed 3 tests, and CodeScene passed across 42 eligible staged files with no issues. This repository has no Sonar configuration.

Packaged MCP smoke passed with the final worker copied into a temporary copy of the macOS package. The first attempt timed out during initialization inside the sandbox; the retry with local runtime IPC enabled passed.
<!-- SECTION:NOTES:END -->

## Final Summary

<!-- SECTION:FINAL_SUMMARY:BEGIN -->
Use read-only compiler project membership for transitive authored ownership and ambient declaration recognition. Resolve unshadowed CommonJS require calls with require conditions and report unresolved computed targets as scoped incomplete evidence. Verified by worker and MCP regressions and the required CodeScene safeguard.
<!-- SECTION:FINAL_SUMMARY:END -->
