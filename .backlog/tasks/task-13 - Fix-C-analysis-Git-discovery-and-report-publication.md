---
id: TASK-13
title: Fix C# analysis, Git discovery, and report publication
status: Done
assignee:
  - '@codex'
created_date: '2026-11-23T11:28:00Z'
updated_date: '2026-11-23T11:45:00Z'
labels: []
dependencies: []
type: bug
ordinal: 13000
---

## Description

<!-- SECTION:DESCRIPTION:BEGIN -->
The general review reproduced colliding generic method IDs, a repository-wide Roslyn compilation merging independent projects, ignored tracked sources disappearing from discovery, and cancellation evicting existing reports.
<!-- SECTION:DESCRIPTION:END -->

## Acceptance Criteria
<!-- AC:BEGIN -->
- [x] #1 C# project-owned symbols, overloads, helper calls, dependencies, and coverage remain distinct with scoped evidence.
- [x] #2 Git discovery retains tracked files under ignore patterns across repository and changed scopes.
- [x] #3 Failed or canceled publication preserves existing unexpired reports with bounded staging.
- [x] #4 Regression and integration tests, documentation, and Code Health verify the work.
<!-- AC:END -->

## Implementation Plan

<!-- SECTION:PLAN:BEGIN -->
Add regressions first. Fix Git and reporting independently. Isolate C# projects, centralize identities, scope external references and coverage, and version C# evidence. Verify focused and full checks, package smoke, and CodeScene.
<!-- SECTION:PLAN:END -->

## Implementation Notes

<!-- SECTION:NOTES:BEGIN -->
Reproduced the original tracked-ignore, report cancellation, generic identity, and merged-project failures in focused fixtures.
Corrected Git discovery and split report staging from publication. The staged write is bounded to one additional report.
Isolated Roslyn compilations and reference manifests by project. Native Coverlet verification showed merged overload records and relative module paths. Added production output MVID matching and split counters only for uniquely owned source points. Older C# evidence requires refresh without discarding Java counters.
Ran 186 tests with SDK 10.0.403 including real .NET, Maven and MCP integration. Rebuilt the self-contained macOS package and passed the seven-tool smoke. Refactored CodeScene findings, confirmed scores of 10 on affected files, and passed the safeguard on 42 eligible files. git diff --check passed.
<!-- SECTION:NOTES:END -->

## Final Summary

<!-- SECTION:FINAL_SUMMARY:BEGIN -->
Fixed the four defects from the quality review. C# uses project-scoped identities, references and coverage. Git retains tracked sources that match ignore rules. Failed or canceled staging preserves existing reports. Verified with 186 tests, rebuilt package smoke and CodeScene.
<!-- SECTION:FINAL_SUMMARY:END -->
