---
id: TASK-23
title: Fix CQC scoring, evidence, and test maintenance issues
status: Done
assignee:
  - '@codex'
created_date: '2026-11-24T15:54:00Z'
updated_date: '2026-11-24T16:05:00Z'
labels: []
dependencies: []
type: enhancement
ordinal: 22000
---

## Description

<!-- SECTION:DESCRIPTION:BEGIN -->
Implement the approved six-slice maintenance plan.
<!-- SECTION:DESCRIPTION:END -->

## Acceptance Criteria
<!-- AC:BEGIN -->
- [x] #1 Regression tests reject empty score rows and clone fingerprints.
- [x] #2 Touched dependency references retain complete source attribution.
- [x] #3 Family aggregation is shared across scoring scopes and assertions are credited from tests.
- [x] #4 Evidence publication is atomic and can read legacy flat caches.
- [x] #5 Analysis policy and adapter collection have explicit ownership.
- [x] #6 Required tests, documentation, and Code Health gates pass.
<!-- AC:END -->

## Implementation Plan

<!-- SECTION:PLAN:BEGIN -->
1. Strengthen regression tests.
2. Separate dependency graph edges and source references.
3. Unify family aggregation.
4. Separate test assertion credit and fixture cost.
5. Publish atomic evidence bundles.
6. Separate analysis policy and orchestration.
<!-- SECTION:PLAN:END -->

## Implementation Notes

<!-- SECTION:NOTES:BEGIN -->
Strengthened score and fingerprint checks. Isolated mutations returning empty rows or fingerprints failed the corresponding tests.
Retained dependency reference origins and consolidated whole and touched scoring. Reports from the old and rebuilt package matched on scoring, mixed, and TypeScript fixtures after excluding the intentional ruleset version change.
Separated fixture cost from assertion credit, published one evidence bundle per run, and separated analysis policy and fact collection from orchestration. Java worker tests, TypeScript worker tests, package smoke, and the CodeScene safeguard passed.
The first full verification attempt used a Maven repository ID incompatible with the local JaCoCo cache. Retried with the default offline Maven configuration and corrected two regression fixture expectations.

Final run passed all 240 .NET tests with SDK 10.0.403 and the default offline Maven cache. All 31 Java worker tests and 15 TypeScript worker tests passed. The isolated final macOS package passed the eight-tool stdio smoke test. CodeScene checked all 37 eligible changed files and passed its safeguard. Sonar was inactive because the repository is unconfigured.
<!-- SECTION:NOTES:END -->

## Final Summary

<!-- SECTION:FINAL_SUMMARY:BEGIN -->
Implemented all six slices. Dependency scoring retains every touched reference and deduplicates graph edges and deductions. Each rule family aggregates selected evidence through one path. Ruleset 3 credits assertions reached from the test body. CQC publishes evidence atomically and retains legacy cache and standalone reportPath reads. AnalysisService delegates policy and adapter work. Tests, fixture comparisons, package smoke, documentation, and Code Health verification passed. Changes remain in the working tree.
<!-- SECTION:FINAL_SUMMARY:END -->
