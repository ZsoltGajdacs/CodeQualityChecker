---
id: TASK-10
title: Make uncommitted-change checks easy to use
status: Done
assignee:
  - '@codex'
created_date: '2026-11-09T12:13:00Z'
updated_date: '2026-11-09T12:16:00Z'
labels: []
dependencies: []
ordinal: 10000
---

## Description

<!-- SECTION:DESCRIPTION:BEGIN -->
Users need a discoverable way to check staged, unstaged and new source files. Existing changed scope already provides whole-file selection; preserve its API and repository context.
<!-- SECTION:DESCRIPTION:END -->

## Acceptance Criteria
<!-- AC:BEGIN -->
- [x] #1 MCP scope and gitBase descriptions explain uncommitted selection and working-tree content
- [x] #2 README and canonical review skill show an uncommitted-change request
- [x] #3 Real-Git tests cover staged, unstaged, partially staged, untracked, clean, deletion, rename, exclusion, space-containing paths and nested project roots
- [x] #4 All inspection/check MCP tools verify changed-file reports, pagination, descriptions and non-Git errors
- [x] #5 Full .NET suite and required CodeScene gates pass
<!-- AC:END -->

## Implementation Plan

<!-- SECTION:PLAN:BEGIN -->
1. Review existing discovery, MCP schemas and fixtures. 2. Add real-Git and MCP regression coverage; reproduce any boundary failures. 3. Add parameter descriptions and uncommitted examples, fixing scoped discovery defects if exposed. 4. Review changes, run focused and full tests, CodeScene reviews and safeguard, and finalize task.
<!-- SECTION:PLAN:END -->

## Implementation Notes

<!-- SECTION:NOTES:BEGIN -->
Planner/explorer reviewed GitScope and verified nested-root Git output using a real temporary repository. Existing selection semantics are correct; keep GitScope unchanged unless regressions expose a defect. README and canonical skill now show changed scope with gitBase omitted. Sonar inactive: no .sonar/config.json in this project.

Implementer added 7 real-Git cases and 3 actual MCP tests. Red run: 9 passed, MCP schema description test failed as expected because descriptions were absent. Added Description attributes to Scope, Paths and GitBase. CodeScene review: Contracts and both new test files 10.0. Plugin packaging 6/6 and installer 13/13 passed. No GitScope production logic changes required.

Final verification: focused ChangedScope 10/10 green; dotnet test CodeQualityChecker.sln --no-restore 127 passed, 0 failed, 0 skipped. Independent reviewer found no actionable issues and tester assessment confirmed planned coverage. CodeScene pre_commit_code_health_safeguard passed: checked-file-count 3, eligible 3, no issues. git diff --check clean. Changes remain local and uncommitted; no client settings, installed binaries or plugin installations changed.
<!-- SECTION:NOTES:END -->

## Final Summary

<!-- SECTION:FINAL_SUMMARY:BEGIN -->
Made existing changed scope discoverable through MCP Scope/Paths/GitBase descriptions, README example and canonical review skill. Added 10 real-Git/MCP tests for uncommitted selection, working-tree content, edge cases, pagination and errors. Existing selection/API/defaults preserved. Validation: 127 .NET tests, 6 packaging and 13 installer tests passed; all 3 changed code files Code Health 10.0; safeguard passed; independent review clean. Sonar inactive because project is unconfigured.
<!-- SECTION:FINAL_SUMMARY:END -->
