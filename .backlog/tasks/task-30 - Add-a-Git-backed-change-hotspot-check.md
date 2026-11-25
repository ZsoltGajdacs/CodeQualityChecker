---
id: TASK-30
title: Add a Git-backed change hotspot check
status: Done
assignee:
  - '@codex'
created_date: '2026-11-25T16:04:00Z'
updated_date: '2026-11-25T16:32:00Z'
labels: []
dependencies: []
type: feature
ordinal: 29000
---

## Description

<!-- SECTION:DESCRIPTION:BEGIN -->
Surface production classes whose files change often and whose wide constructors and size point to concentrated responsibilities.
<!-- SECTION:DESCRIPTION:END -->

## Acceptance Criteria
<!-- AC:BEGIN -->
- [x] #1 Score production classes in C#, Java, and TypeScript on the approved weighted 0–10 scale and report class and file evidence.
- [x] #2 Collect local first-parent history across 12 months with rename tracking and partial-class deduplication.
- [x] #3 Expose hotspots through source-only, uncommitted, and all-check reviews while preserving existing family behavior.
- [x] #4 Document the check and verify it with real Git, MCP, regressions, and Code Health.
<!-- AC:END -->

## Implementation Plan

<!-- SECTION:PLAN:BEGIN -->
1. Collect branch history and language class facts.
2. Add family-owned scoring and policy routing.
3. Document and exercise production entry points and quality checks.
<!-- SECTION:PLAN:END -->

## Implementation Notes

<!-- SECTION:NOTES:BEGIN -->
- Added production class facts across the three analyzers and wired hotspots into the shared MCP reports.
- Review caught a backdated rename case and language-specific syntax/identity concerns. The Git reader now uses a metadata pass to bound one path-diff scan while preserving intervening topology. Reads use Git-reported project prefixes and local objects.
- The initial real-Git/MCP suite passed. The broad run exposed one fixture that expected no families for between-method edits. Its expectation now permits hotspot results while still requiring no static complexity assessment.
- Java worker suite passed 39 tests, TypeScript worker suite passed 20, and CodeScene passed all 37 eligible files. Final .NET regression is running.

Final verification passed: 271 .NET tests, 39 Java worker tests, 20 TypeScript worker tests, 26 packaging and installer tests, native MCP smoke, both generated plugin configurations, and the CodeScene safeguard on all 37 eligible files. The packaged smoke used current assemblies and workers with the existing bundled runtimes.
<!-- SECTION:NOTES:END -->

## Final Summary

<!-- SECTION:FINAL_SUMMARY:BEGIN -->
Implemented the Git-backed hotspot family with weighted class scores, lowest-class file scores, and pinned local Git history. Added a standalone source-only MCP tool and integrated the family into project, uncommitted, and all-check reports. Updated the ruleset, canonical skill guidance, and regression coverage. All acceptance criteria are verified.
<!-- SECTION:FINAL_SUMMARY:END -->
