---
id: TASK-3
title: Align default complexity cutoff with Code Health
status: Done
assignee:
  - '@codex'
created_date: '2026-11-07T21:17:00Z'
updated_date: '2026-11-07T21:29:00Z'
labels: []
dependencies: []
type: enhancement
ordinal: 3000
---

## Description

<!-- SECTION:DESCRIPTION:BEGIN -->
The scoring fixture comparison found CQC only flags complexity above10 while current Code Health flags9 and above for Java and C#. Align the default and pin the exact boundary without changing explicit override semantics.
<!-- SECTION:DESCRIPTION:END -->

## Acceptance Criteria
<!-- AC:BEGIN -->
- [x] #1 Default complexity9 and above produces code.complexity for both languages while complexity8 remains clean; explicit configured maximum overrides retain their existing meaning.
- [x] #2 Paired source fixtures and fixed expectations cover complexity8/9 boundary and update the existing complexity10 deductions and scores.
- [x] #3 Rule documentation, focused/full tests and independent review agree with the verified Code Health boundary; changes are committed on the existing branch.
<!-- AC:END -->

## Implementation Plan

<!-- SECTION:PLAN:BEGIN -->
1. Verify native Code Health cutoff with Java/C# CC8/9 specimens. 2. Add failing unit boundary/override tests and real source fixtures. 3. Change default maximum to8, revise fixed expectations and docs. 4. Run deterministic source goldens/full suite and maintained-code quality review; commit on current branch.
<!-- SECTION:PLAN:END -->

## Implementation Notes

<!-- SECTION:NOTES:BEGIN -->
Verified current Code Health using isolated Git specimens: CC8 has no findings and CC9 reports Complex Method for both Java/C#. Added regression tests first: olddefault caused3 expected failures (CC9/10 scores and missing-coverage CC9 finding); override cases already passed. Changed default maximum to8 with comparison unchanged. Focused CoreEngine+sourcegolden test run passed24 tests before boundary tests were separated into a dedicated ComplexityThresholdTests class (Code Health10). Paired source goldens now36 witnesses: four new CC8/9 entries and two revised CC10 entries. Independent review found no discrepancies. Kept ruleset1 because algorithm and explicit config semantics are unchanged; configuration fingerprint captures the new default policy. Full suite and fresh package verification underway.

Final full suite passed71/71, including36 literal score witnesses and8 new policy unit cases. New policy tests are in dedicated ComplexityThresholdTests.cs; prior CoreEngineTests remains unchanged. Maintained Contracts.cs and policy tests each Code Health10. Broad pre-commit scan flags only the two deliberately CC9 negative fixture specimens plus unrelated pre-existing untracked .ua scripts; none are maintained product-code findings. .ua remains untouched and excluded from commit. Code Health native findings on new CC8/9 files independently match CQC cutoff. Refreshing the local distribution to ship current default8 and updated rule documentation.

Final verification complete: full71/71 tests passed;36 real-source score witnesses and8 policy cases agree with Code Health boundary. Independent review found no issues. Fresh self-contained package passed all6tools without host runtimes; an extra packaged-server boundary check flagged exactly java:Boundary9 and csharp:Boundary9 and did not flag eitherCC8 class. Verified575 package hashes and current binaries/rules documentation. Archive SHA256 fa917acfd3c0023764cccdfb9c31889e9c11aa0ef1a200e8696e778ffe575872. Maintained code/tests Code Health10. Broad scan has only intentional CC9 fixture specimens and pre-existing unrelated .ua scripts; no product-code findings. .ua untouched. TRX evidence /private/tmp/cqc-complexity-full-verification; package log /private/tmp/cqc-complexity-package.log.
<!-- SECTION:NOTES:END -->

## Final Summary

<!-- SECTION:FINAL_SUMMARY:BEGIN -->
Aligned default complexity finding onset to9 for Java/C#, matching measured Code Health behavior. Default maximum is8; strict greater-than comparison and explicit override semantics are unchanged. Added pairedCC8/9 sources, updatedCC10 scores/deductions, expanded goldens to36 witnesses, and added8 unit policy cases including missing-evidence behavior. All71 tests pass; independent review is clear and maintained code/tests Code Health10. Updated docs explain configuration provenance refresh. Rebuilt macOS package and verified actual packaged cutoff without host runtimes. Changes committed on the existing branch; unrelated .ua artifacts excluded.
<!-- SECTION:FINAL_SUMMARY:END -->
