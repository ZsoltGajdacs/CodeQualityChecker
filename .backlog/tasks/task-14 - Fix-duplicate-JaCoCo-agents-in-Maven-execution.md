---
id: TASK-14
title: Fix duplicate JaCoCo agents in Maven execution
status: Done
assignee:
  - '@codex'
created_date: '2026-11-23T19:50:00Z'
updated_date: '2026-11-23T20:14:00Z'
labels: []
dependencies: []
ordinal: 14000
---

## Description

<!-- SECTION:DESCRIPTION:BEGIN -->
Recurring Billing and CQC attach separate JaCoCo agents and the test JVM aborts before producing evidence.
<!-- SECTION:DESCRIPTION:END -->

## Acceptance Criteria
<!-- AC:BEGIN -->
- [x] #1 Mixed Maven reactors reuse project instrumentation and fall back per module
- [x] #2 Reports use effective execution data and are attributed to the owning module
- [x] #3 Recurring Billing reaches Surefire without the duplicate-agent crash
- [x] #4 Failed and cancelled builds withhold fresh evidence
<!-- AC:END -->

## Implementation Plan

<!-- SECTION:PLAN:BEGIN -->
Configure JaCoCo per module in the Maven extension, publish explicit report metadata, verify real mixed reactor and Recurring Billing.
<!-- SECTION:PLAN:END -->

## Implementation Notes

<!-- SECTION:NOTES:BEGIN -->
Reproduced the duplicate bootstrap class and exit 134 with the mixed-reactor regression before the fix. After implementation, 187 tests passed. Independently verified inherited profile coverage, custom data files, mixed versions, skipped coverage, shared-path diagnostics, and early prepare-agent-integration. Isolated Billing core passed all 18 tests and generated a JaCoCo report for 161 classes. Initial full Billing probes discovered an unrelated nested Claude worktree; repeated against the intended Billing tree only.

Final suite: 187 passed. CodeScene pre-commit safeguard passed across 13 eligible files after the Maven refactoring. The standard Billing attempt timed out during frontend work. Full execute=true retry completed both frontend builds and spec with a longer timeout, npm audit and fetch retries disabled for the run, and Maven offline using cached artifacts. It published 3313 method coverage records and 679 code entity scores. All family statuses remain incomplete due to remaining analysis blockers including unresolved Java symbols and missing coverage in annotations-processors and api.
<!-- SECTION:NOTES:END -->

## Final Summary

<!-- SECTION:FINAL_SUMMARY:BEGIN -->
CQC now reuses scheduled JaCoCo instrumentation per Maven module, adds fallback only where needed, and consumes only module-owned reports from the evaluated execution data. The duplicate agent crash is reproduced by the regression and absent from the successful full Billing retry.
<!-- SECTION:FINAL_SUMMARY:END -->
