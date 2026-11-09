---
id: TASK-6
title: Add local plugin installer scripts
status: Done
assignee:
  - '@codex'
created_date: '2026-11-09T05:45:00Z'
updated_date: '2026-11-09T05:47:00Z'
labels: []
dependencies: []
ordinal: 6000
---

## Description

<!-- SECTION:DESCRIPTION:BEGIN -->
Make the generated CQC plugins easy to install with one client-specific command instead of copying marketplace commands manually.
<!-- SECTION:DESCRIPTION:END -->

## Acceptance Criteria
<!-- AC:BEGIN -->
- [x] #1 Executable Claude and Codex installer scripts install the matching generated local plugin with default or explicit packages directory, including paths with spaces.
- [x] #2 Missing clients or packages fail clearly before changing settings; installation is verified in isolated client homes and documented.
<!-- AC:END -->

## Implementation Plan

<!-- SECTION:PLAN:BEGIN -->
1. Add two small shell entrypoints for generated local packages. 2. Document invocation and prerequisites. 3. Verify error handling and both real client installs in isolated settings, review Code Health, and finalize.
<!-- SECTION:PLAN:END -->

## Implementation Notes

<!-- SECTION:NOTES:BEGIN -->
Added executable client-specific shell installers and documented default/custom packages roots and user settings scope. Independent implementer reviewed package layout and CLI interfaces. bash -n and ShellCheck pass after expressing empty CDPATH explicitly (initial SC1007 fixed). CodeScene file review cannot analyze .sh, so no Code Health score is claimed; shell syntax/lint and real isolated client tests provide applicable validation.

Independent tester passed 18 behavioral cases across both scripts: real default installs from neutral cwd and explicit packages roots containing spaces; enabled cqc 0.1.0 verified through client lists; help, extra/unknown arguments, missing packages and missing CLI; registration failure exit17 prevents install, install failure exit23 propagates, neither prints false success. Repeated actual installs in same isolated settings also exit0 and retain exactly one enabled plugin per client. bash -n, full ShellCheck and git diff --check pass. Independent reviewer found no blockers; retry concern resolved by real duplicate-registration tests. Normal client settings unchanged. CodeScene cannot analyze shell, so applicable gates are syntax, ShellCheck and real behavior. No Sonar configuration. Changes left uncommitted.
<!-- SECTION:NOTES:END -->

## Final Summary

<!-- SECTION:FINAL_SUMMARY:BEGIN -->
Added executable install-claude-plugin.sh and install-codex-plugin.sh for generated local packages, with default/custom paths, preflight, user-scope installation and failure propagation. Documented invocation and isolated testing. All acceptance criteria verified with actual clients, error cases and repeated installation; shell syntax and lint pass.
<!-- SECTION:FINAL_SUMMARY:END -->
