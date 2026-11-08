---
id: TASK-4
title: Strengthen Maven build integration
status: Done
assignee:
  - '@codex'
created_date: '2026-11-08T20:48:00Z'
updated_date: '2026-11-08T20:58:00Z'
labels: []
dependencies: []
ordinal: 4000
---

## Description

<!-- SECTION:DESCRIPTION:BEGIN -->
Improve isolated Maven wrapper and reactor execution and reusable evidence.
<!-- SECTION:DESCRIPTION:END -->

## Acceptance Criteria
<!-- AC:BEGIN -->
- [x] #1 Wrapper selection and prerequisite inspection work correctly.
- [x] #2 Common reactors and independent projects produce reusable classpath and coverage evidence.
- [x] #3 Behavior, safety and documentation are verified.
<!-- AC:END -->

## Implementation Plan

<!-- SECTION:PLAN:BEGIN -->
1. Add wrapper selection, mode preservation, and prerequisite inspection with behavioral tests.
2. Verify and strengthen reactor execution and durable evidence with real Maven fixtures.
3. Update documentation, review and run test and quality checks.
<!-- SECTION:PLAN:END -->

## Implementation Notes

<!-- SECTION:NOTES:BEGIN -->
Task created after sandboxed Backlog CLI could not create a lock under .git; authorized CLI execution used.

Real reactor probe confirmed sibling target/classes paths are temporary; retain source-based resolution and persist temporary JARs by content hash. Added regression test; Code Health reviews returned 10 for touched execution and metadata test files.

Nested reactor with dependent modules and independent Maven build passes real integration test, including source-only replay and unchanged originals. Wrapper implementation records versions per root and preserves executable modes.

Full suite passed 82 tests before final review changes; independent review identified missing-output diagnostics, malformed POM handling, and direct POM reactor declarations, now being corrected. Sandboxed MSBuild socket creation was denied; execution tests used authorized local execution.

Independent review prompted per-root missing-output diagnostics, preserving other-language coverage and invalidating partial classpath cache; direct POM declarations and malformed POM errors are covered. Full suite passed 85 tests before final diagnostic adjustment.

Final review confirmed first-run invalidation and command behavior. Full suite passed 89 tests; CodeScene safeguard passed 19 eligible files. SonarMCP inactive: no .sonar/config.json. Maven profile/custom-source and overlapping reactor report limitations remain outside the approved milestone.
<!-- SECTION:NOTES:END -->

## Final Summary

<!-- SECTION:FINAL_SUMMARY:BEGIN -->
Added wrapper-aware isolated execution, Maven root selection and per-root diagnostics with conservative reusable classpath and coverage evidence. Added real reactor, independent project, wrapper, failure, cancellation and replay tests; updated documentation. Verified full suite (89 passed) and CodeScene safeguard.
<!-- SECTION:FINAL_SUMMARY:END -->
