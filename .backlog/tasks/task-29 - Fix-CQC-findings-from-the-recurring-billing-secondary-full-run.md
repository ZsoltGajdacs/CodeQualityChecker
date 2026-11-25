---
id: TASK-29
title: Fix CQC findings from the recurring-billing-secondary full run
status: In Progress
assignee:
  - '@codex'
created_date: '2026-11-25T14:40:00Z'
labels: []
dependencies: []
type: bug
ordinal: 28000
---

## Description

<!-- SECTION:DESCRIPTION:BEGIN -->
CQC defects in report 7decfa72. Billing is the acceptance checkout. Work on master in focused local commits.
<!-- SECTION:DESCRIPTION:END -->

## Acceptance Criteria
<!-- AC:BEGIN -->
- [ ] #1 Verified Maven contexts resolve generated Java members and report unresolved references precisely.
- [ ] #2 JVM signatures and downstream JaCoCo probes produce correct module-owned coverage.
- [ ] #3 TypeScript resolves supported assets and runtime subpaths while preserving genuine blockers.
- [ ] #4 Report pagination filters diagnostics and separates scored, incomplete and not applicable entities.
- [ ] #5 Packaged Billing execution and cached evidence replay are verified against the baseline.
<!-- AC:END -->

## Implementation Plan

<!-- SECTION:PLAN:BEGIN -->
1. Correct Java resolver and dependency diagnostics with a Maven regression.
2. Index coverage signatures and handle erased types and enum constructors.
3. Merge validated downstream probes per production module.
4. Resolve TypeScript assets and runtime subpaths.
5. Improve report diagnostics and verify the package on Billing.
<!-- SECTION:PLAN:END -->
