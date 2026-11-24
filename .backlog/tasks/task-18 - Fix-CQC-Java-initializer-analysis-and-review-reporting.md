---
id: TASK-18
title: Fix CQC Java initializer analysis and review reporting
status: Done
assignee:
  - '@codex'
created_date: '2026-11-24T11:59:00Z'
updated_date: '2026-11-24T12:27:00Z'
labels: []
dependencies: []
type: bug
ordinal: 18000
---

## Description

<!-- SECTION:DESCRIPTION:BEGIN -->
Billing source-only review showed 391 structural diagnostics, omitted selected production files from scoring and suppressed Java duplication.
<!-- SECTION:DESCRIPTION:END -->

## Acceptance Criteria
<!-- AC:BEGIN -->
- [x] #1 All Java initializer forms participate in static complexity and duplication without double counting.
- [x] #2 Partial duplication keeps verified findings and withholds affected scores.
- [x] #3 MCP reports expose incomplete assessments and diagnostic scope.
- [x] #4 Worker, .NET, package and Billing checks verify behavior.
<!-- AC:END -->

## Implementation Plan

<!-- SECTION:PLAN:BEGIN -->
1. Separate Java structural extraction from callable analysis.
2. Share structural input and evidence across complexity and duplication.
3. Project scoped diagnostics and score counts and update shared plugin guidance.
4. Verify worker, MCP, packaged Billing and Code Health behavior.
<!-- SECTION:PLAN:END -->

## Implementation Notes

<!-- SECTION:NOTES:BEGIN -->
The final packaged Claude session used the same Billing source/configuration fingerprints and 21 selected Java files. Java structural diagnostics fell from 391 to 0. Static complexity scored 11 production files with 0 unscored and found two methods over threshold: RecurringStateEventService.handleRecurringUpdated at line 80 (21) and RecurringFlowFactory.getSteps at line 383 (14). Duplication completed with 17 groups. The structured report explicitly lists five repository-wide extensions. Claude still described that group as changed files in its prose. Billing was checked with execute=false.
<!-- SECTION:NOTES:END -->

## Final Summary

<!-- SECTION:FINAL_SUMMARY:BEGIN -->
Separated Java executable discovery and measurement from callable and JaCoCo evidence, added partial duplication and precise score/diagnostic scope reporting, and updated both plugin clients through the shared skill. Verified 31 Java worker tests, 200 .NET tests including compact constructor JaCoCo coverage, 20 packaging/installer tests, macOS package and both plugin smoke checks, a fresh Claude Billing rerun, and CodeScene across 44 eligible files. Delivered as uncommitted working-tree changes.
<!-- SECTION:FINAL_SUMMARY:END -->
