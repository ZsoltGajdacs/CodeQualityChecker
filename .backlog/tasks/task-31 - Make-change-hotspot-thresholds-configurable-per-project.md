---
id: TASK-31
title: Make change-hotspot thresholds configurable per project
status: Done
assignee:
  - '@codex'
created_date: '2026-11-26T09:12:00Z'
updated_date: '2026-11-26T09:16:00Z'
labels: []
dependencies:
  - TASK-30
type: enhancement
ordinal: 30000
---

## Description

<!-- SECTION:DESCRIPTION:BEGIN -->
Project owners need to see and override the change-hotspot scoring bounds.
<!-- SECTION:DESCRIPTION:END -->

## Acceptance Criteria
<!-- AC:BEGIN -->
- [x] #1 All hotspot checks use validated bounds defaulting to 10/30, 7/10 and 400/800.
- [x] #2 Evidence-publishing execution creates .cqc/config.json once and preserves existing config, read-only checks, tracked evidence protection and explicit overrides.
- [x] #3 Tests, docs, shared plugin guidance and CodeScene verify the behavior.
<!-- AC:END -->

## Implementation Plan

<!-- SECTION:PLAN:BEGIN -->
1. Centralize threshold defaults and scoring.
2. Share directory write checks and seed config alongside evidence publication.
3. Verify scoring, precedence and lifecycle.
<!-- SECTION:PLAN:END -->

## Implementation Notes

<!-- SECTION:NOTES:BEGIN -->
Confirmed that the new constructor boundaries failed against the original scoring. Added typed defaults and project config publication. Preserved BOM-aware config reads after review. The first full regression run exposed an old partial-declaration expectation, so the fixture now uses eight parameters to retain penalty coverage. CodeScene flagged three files during review, then passed all 13 eligible files after simplification. The final full .NET run passed all 280 tests.
<!-- SECTION:NOTES:END -->

## Final Summary

<!-- SECTION:FINAL_SUMMARY:BEGIN -->
Centralized hotspot bounds and raised the default constructor lower bound to seven. Evidence-publishing execution seeds editable project config and preserves existing files. All 280 .NET tests passed, including real Git and MCP, override precedence, config publication and concurrent creation. CodeScene passed all 13 eligible files and git diff --check passed.
<!-- SECTION:FINAL_SUMMARY:END -->
