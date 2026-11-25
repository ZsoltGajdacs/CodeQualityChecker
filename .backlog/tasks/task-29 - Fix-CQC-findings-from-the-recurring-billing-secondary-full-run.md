---
id: TASK-29
title: Fix CQC findings from the recurring-billing-secondary full run
status: Done
assignee:
  - '@codex'
created_date: '2026-11-25T14:40:00Z'
updated_date: '2026-11-25T15:31:00Z'
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
- [x] #1 Verified Maven contexts resolve generated Java members and report unresolved references precisely.
- [x] #2 JVM signatures and downstream JaCoCo probes produce correct module-owned coverage.
- [x] #3 TypeScript resolves supported assets and runtime subpaths while preserving genuine blockers.
- [x] #4 Report pagination filters diagnostics and separates scored, incomplete and not applicable entities.
- [x] #5 Packaged Billing execution and cached evidence replay are verified against the baseline.
<!-- AC:END -->

## Implementation Plan

<!-- SECTION:PLAN:BEGIN -->
1. Correct Java resolver and dependency diagnostics with a Maven regression.
2. Index coverage signatures and handle erased types and enum constructors.
3. Merge validated downstream probes per production module.
4. Resolve TypeScript assets and runtime subpaths.
5. Improve report diagnostics and verify the package on Billing.
<!-- SECTION:PLAN:END -->

## Implementation Notes

<!-- SECTION:NOTES:BEGIN -->
Slice 1 committed as cb58dfe. Old worker reproduced generated builder, getter-chain and assertion failures in the new reactor fixture. Updated worker passed 32 Python worker tests and 7 Maven integration tests. CodeScene gate passed. Verified precedence does not repair JavaParser lexical calls to generated members on the current authored type; measure that residual in Billing.

Slice 2 committed as ebe00b0. Worker regression reproduced incorrect erasure before the fix. 33 worker tests, Maven fixture tests and 27 focused .NET integration/evidence tests passed. CodeScene gate passed. Same-source Billing cached probe resolved all 95 ErrorLogFactory calls, reduced unresolved calls to 15,794 and raised scored test entities from 85 to 182, with duplication unchanged at 484.

Slice 3 committed as 98cbd40. Packaged-JAR and compiled-directory reactor scenarios passed. Fixtures verify complementary and downstream-only branch counts, skipped instrumentation, saved evidence, conflicting artifact bytes, unsupported agent versions, corrupt probes and local XML fallback. CodeScene gate passed.

Slice 4 committed as d2a93d8. Nineteen TypeScript worker tests passed. Direct Billing replay eliminated all 114 unresolved imports while retaining all 2,517 dependency edges, 35 genuine missing-configuration diagnostics and two baseUrl advisories. CodeScene gate passed.

Slice 5 report implementation passed 63 reporting and MCP tests and the CodeScene gate. The rare-code regression uses 39,100 preceding diagnostic rows and verifies cursor binding and score-count reconciliation.

Final package smoke passed with bundled runtimes. The manifest and archive checksums passed, including JaCoCo notices. Final suites passed 39 Java worker, 19 TypeScript worker, and 255 .NET tests. Billing packaged report 4d89e1df4d434899ae4e2ffe809e9852 matched the baseline source and configuration fingerprints. Full run took 546.9 seconds and saved-evidence replay took 85.1 seconds, with identical family summaries. Scores increased from 998 to 1046 Code, 96 to 1182 Dependencies, and 85 to 182 Tests. Duplication was complete with 484 findings. Exact diagnostic filtering returned the 16 spec conflicts and 35 configuration gaps, with zero unresolved TypeScript modules. ErrorLogFactory had no unresolved diagnostics. The enum and generic coverage samples now have counters. The report still identified unresolved Java calls, eight incomplete Code files, and genuine classpath or test-configuration blockers.
<!-- SECTION:NOTES:END -->

## Final Summary

<!-- SECTION:FINAL_SUMMARY:BEGIN -->
Delivered verified Java bytecode resolution, JVM coverage aliases, module-owned downstream JaCoCo aggregation, TypeScript bundler resolution, and report filtering and counts. Verified the packaged Billing run and cached replay against the original fingerprints. All five implementation slices are local on master.
<!-- SECTION:FINAL_SUMMARY:END -->
