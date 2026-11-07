---
id: TASK-2
title: Verify deterministic scores with paired Java and C# source fixtures
status: Done
assignee:
  - '@codex'
created_date: '2026-11-07T20:16:00Z'
updated_date: '2026-11-07T21:13:00Z'
labels: []
dependencies: []
type: task
ordinal: 2000
---

## Description

<!-- SECTION:DESCRIPTION:BEGIN -->
Existing MVP tests verify deterministic output and pure-engine deductions, but most exact scoring assertions bypass real language parsing. Add an independently specified source fixture corpus to lock scoring behavior across Java and C# through actual build/coverage integrations on the existing branch.
<!-- SECTION:DESCRIPTION:END -->

## Acceptance Criteria
<!-- AC:BEGIN -->
- [x] #1 Paired real Java and C# fixtures specify exact healthy and unhealthy scores, method metrics, rule deductions and source ranges for all four check families.
- [x] #2 Threshold boundaries, every-finding deductions and zero-score clamping are pinned by fixed expectations without deriving expected values from checker output.
- [x] #3 Two independently copied and executed fixture repositories produce byte-identical analysis responses; read-only evidence replay preserves scoring.
- [x] #4 Focused fixture tests, the full suite, independent review and applicable Code Health checks pass; fixture intent and expected scores are documented.
<!-- AC:END -->

## Implementation Plan

<!-- SECTION:PLAN:BEGIN -->
1. Review existing coverage and independently specify score witnesses. 2. Add paired Maven/JUnit5 and SDK .NET/xunit fixture projects and fixed expected results. 3. Test actual analysis/coverage execution in independent roots and validate exact scores, rules, ranges, ordering and replay. 4. Review, run focused/full tests and Code Health, then commit on the existing branch.
<!-- SECTION:PLAN:END -->

## Implementation Notes

<!-- SECTION:NOTES:BEGIN -->
Added literal paired Java/C# projects and32 independent score witnesses spanning all4 families. The focused real-build test passed: exact scores/rules/locations/metrics, class-vs-file rollup, threshold boundaries,12-finding zero clamp, isolated duplicate groups, two independent root builds, full response byte equality and read-only evidence replay. Source/config byte hashes remain unchanged. Initial red tests observed missing expectation data and a fixture-folder name collision on case-insensitive filesystems; expected data now lives under tests/Cqc.Tests/Expectations. Maintained harness Code Health10. Intentionally poor fixture inputs are preserved as measurement specimens, not refactored as product code. Full suite and independent review pending.

Final verification: focused real-build golden test passed; full dotnet test CodeQualityChecker.sln --no-restore --nologo passed63/63. Independent reviewer manually recomputed all32 witnesses and source ranges with no discrepancies or actionable findings. Both frameworks execute20 fixture tests per language per build. New maintained harness Code Health10/no findings. Broad pre-commit scan reports only6 expected Complex Method findings in AtComplexity10/OverComplexity11/HighRisk156 Java/C# fixture specimens; those exact CC10/11/12 shapes are the user-requested negative scoring inputs, not maintained/deployed application code. Preserving them is essential to the accepted fixture intent; no quality rules or checker implementation were weakened. Existing .ua untracked content was not authored by this task and is excluded from commit. TRX evidence: /private/tmp/cqc-scoring-verification and /private/tmp/cqc-scoring-full-verification.
<!-- SECTION:NOTES:END -->

## Final Summary

<!-- SECTION:FINAL_SUMMARY:BEGIN -->
Added paired literal Java/C# scoring projects and32 independent golden witnesses for all4 check families. Verifies exact scores/deductions/metrics/ranges, threshold boundaries,0-score clamping, duplicate membership, class/file rollup, raw-byte preservation, cross-root deterministic responses and evidence replay through actual Maven/xunit coverage execution. All63 tests pass; independent review is clear; maintained harness Code Health10. Intentional complexity findings are confined to negative fixture inputs and preserved by design. No production code changed.
<!-- SECTION:FINAL_SUMMARY:END -->
