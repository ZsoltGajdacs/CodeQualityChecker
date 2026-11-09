---
id: TASK-11
title: Exclude dependency-wiring constructors from duplication
status: Done
assignee:
  - '@codex'
created_date: '2026-11-09T12:38:00Z'
updated_date: '2026-11-09T12:43:00Z'
labels: []
dependencies: []
type: bug
ordinal: 11000
---

## Description

<!-- SECTION:DESCRIPTION:BEGIN -->
Unrelated eight-dependency constructors in Recurring-billing normalize to identical fingerprints and receive an actionable duplication deduction for dependency wiring.
<!-- SECTION:DESCRIPTION:END -->

## Acceptance Criteria
<!-- AC:BEGIN -->
- [x] #1 Java and C# direct parameter-to-instance-field wiring constructors are excluded, including partial C# fields and unresolved dependency types.
- [x] #2 Substantive constructors, ambiguous assignments and ordinary methods remain eligible; all non-duplication facts are preserved.
- [x] #3 Fingerprint generation is separated from general measurement; ruleset documents the policy.
- [x] #4 Real analyzer regression coverage, Java and .NET suites and CodeScene gates pass.
<!-- AC:END -->

## Implementation Plan

<!-- SECTION:PLAN:BEGIN -->
1. Capture CodeScene baselines and preserve extraction behavior while separating fingerprints from measurement. 2. Add focused ConstructorWiring classifiers and exclusion in fingerprint generation for Java and C#. 3. Verify negative cases, real Java pair, other facts and substantive duplication; document policy. 4. Review, run suites and CodeScene gates, finalize task.
<!-- SECTION:PLAN:END -->

## Implementation Notes

<!-- SECTION:NOTES:BEGIN -->
Captured CodeScene 10 baselines for five existing production files. C# extraction-only refactor passed 6 adapter/scoring cases; new tests reproduced 7 expected failures, then all 26 passed after classifier integration. Initial sandbox MSBuild IPC and Backlog Git lock failures resolved by authorized elevated commands. Planner/explorer roles completed; Java implementer is verifying extraction equality and real worker behavior. Sonar inactive: no .sonar/config.json.

Java extraction-only refactor preserved all JSON facts for the actual reported pair; the final policy changed only the two constructor fingerprint arrays from 6 entries to 0. Maven build and 27 worker tests passed. C# pre-policy regression run had 7 expected failures; final independent constructor suite passed 30 cases, including six nonvacuous Java/C# report cases. Full .NET suite passed 155 tests before final report-case expansion. Independent review found no production issues; its ordinary-method report coverage recommendation was implemented. All 10 changed source/test files reviewed and scored Code Health 10; safeguard passed with checked-file-count 10. Final C# classifier complex-condition finding was simplified and re-reviewed to 10. No remote configured.

Final full .NET suite passed 157/157. Final staged CodeScene safeguard passed, checking all 10 eligible files. Implementation and verification complete.
<!-- SECTION:NOTES:END -->

## Final Summary

<!-- SECTION:FINAL_SUMMARY:BEGIN -->
Excluded direct dependency-wiring constructors from duplication in Java and C#, including unqualified fields, partial C# fields, expression bodies and unresolved dependency types. Kept classification and fingerprint generation separate from syntax measurement. Verified original pair changes only its fingerprints, and substantive constructors/ordinary methods still report duplicates. Maven build, 27 Java tests, 157 .NET tests and CodeScene reviews/safeguard passed. All changed code scores 10. Sonar inactive because configuration is absent. No remote configured; local delivery only.
<!-- SECTION:FINAL_SUMMARY:END -->
