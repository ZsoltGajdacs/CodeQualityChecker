---
id: TASK-19
title: Require four lines of body code for duplication checks
status: Done
assignee:
  - '@codex'
created_date: '2026-11-24T12:53:00Z'
updated_date: '2026-11-24T13:00:00Z'
labels: []
dependencies: []
ordinal: 19000
---

## Description

<!-- SECTION:DESCRIPTION:BEGIN -->
Declaration spans can currently make short Java and C# bodies eligible for clone detection.
<!-- SECTION:DESCRIPTION:END -->

## Acceptance Criteria
<!-- AC:BEGIN -->
- [x] #1 Duplication ignores bodies with fewer than four owned code lines in Java and C# and honors higher configured minimums.
- [x] #2 Dedicated and source-only reviews use the new eligibility rule and preserve existing similarity and measurement behavior.
- [x] #3 Regression tests, ruleset docs, and the shared CQC review skill cover the change.
<!-- AC:END -->

## Definition of Done
<!-- DOD:BEGIN -->
- [x] #1 Full solution tests, packaged MCP smoke, and CodeScene checks pass.
<!-- DOD:END -->

## Implementation Plan

<!-- SECTION:PLAN:BEGIN -->
1. Reproduce the three-line boundary through source analysis.
2. Centralize C# body selection and reuse Java executable discovery for token ownership. Carry counts through both fact producers and enforce the floor in the shared rule.
3. Cover edge cases, update ruleset and skill guidance, and verify the worker, solution, package, and CodeScene safeguards.
<!-- SECTION:PLAN:END -->

## Implementation Notes

<!-- SECTION:NOTES:BEGIN -->
Reproduced the three-line false positive in both Java and C# through dedicated and source-only analysis before implementation.
Separated duplication code-line counts from test-quality measurements. C# reuses one body selector and Java reuses executable discovery ownership. The four-line floor is applied before matching and higher configured minima are honored.
The Java worker passed 31 tests. An isolated packaged MCP probe exercised both review tools and confirmed grouping and score outcomes at configured minima 1, 4, and 5.
The first CodeScene pass flagged the adapter fact reader. Extracted Java worker protocol parsing and validation into JavaWorkerFacts and added direct tests for missing, negative, and zero counts. CodeScene review and safeguard passed across 20 eligible files.

Final verification passed: 219 .NET tests, 31 Java worker tests, and a fresh eight-tool packaged MCP smoke run in an isolated copy. Direct packaged tests confirmed both tool modes, configured limits, and the legacy Java worker diagnostic. CodeScene passed across all 20 eligible files. SonarMCP is unconfigured for this project.
<!-- SECTION:NOTES:END -->

## Final Summary

<!-- SECTION:FINAL_SUMMARY:BEGIN -->
Java and C# duplication candidates require four owned body-code lines before matching. Higher configured minima still apply. Updated the ruleset and shared CQC skill. Verified worker, solution, package, MCP behavior, and CodeScene safeguards.
<!-- SECTION:FINAL_SUMMARY:END -->
