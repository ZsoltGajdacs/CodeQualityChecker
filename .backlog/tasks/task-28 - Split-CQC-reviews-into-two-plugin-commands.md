---
id: TASK-28
title: Split CQC reviews into two plugin commands
status: Done
assignee:
  - '@codex'
created_date: '2026-11-25T08:51:00Z'
updated_date: '2026-11-25T09:18:00Z'
labels: []
dependencies:
  - TASK-15
type: feature
ordinal: 27000
---

## Description

<!-- SECTION:DESCRIPTION:BEGIN -->
Expose the existing fixed-scope CQC checks as two argument-free plugin skills in Claude Code and Codex.
<!-- SECTION:DESCRIPTION:END -->

## Acceptance Criteria
<!-- AC:BEGIN -->
- [x] #1 Both clients offer check-project and check-uncommitted under the shared CQC prefix and the generic skill is removed.
- [x] #2 Each bare skill invocation uses its fixed-scope tool with execute false, and an empty uncommitted scope stays empty.
- [x] #3 Installer validation, package checks, and usage documentation cover both skills.
<!-- AC:END -->

## Implementation Plan

<!-- SECTION:PLAN:BEGIN -->
1. Update package expectations to require both new skills.
2. Replace the generic skill with two fixed-scope entry points and shared reporting guidance.
3. Update installer, smoke checks, and docs.
4. Verify discovery, scope routing, and quality gates.
<!-- SECTION:PLAN:END -->

## Implementation Notes

<!-- SECTION:NOTES:BEGIN -->
The package regression failed against the old skill inventory and passed after the split. Both CLI plugins now share two fixed-scope instructions and a report guide. Updated standalone asset download and generated package validation.

Terminal autocomplete showed both scope choices and no generic choice in disposable Claude Code 2.1.306 and Codex CLI 0.172.0 profiles. Claude Code expanded both bare commands to their fixed-tool skill prompts. Authenticated client-to-tool invocation remains unverified. Replaying both installed skill requests through each packaged MCP manifest against real Git fixtures confirmed execute false, project vs staged/unstaged/untracked scope, and zero files on clean Git. Both clients passed plugin and MCP discovery and packaged smoke checks. Installer review caught release skew. Cold and cached legacy tag installs now succeed alongside the split layout. Passed 20 installer tests, 6 packaging tests, 7 focused MCP/Git integration tests, ShellCheck, diff checks, and CodeScene review and safeguard. SonarMCP is inactive because this checkout has no .sonar/config.json. Both client profiles were disposable.

The user confirmed that the updated plugin works in their Claude Code profile and asked to close the task. The installed plugin listed both skills and its MCP server connected. Earlier fixture replays verified the exact execute flag and clean-worktree behavior. Codex verification covered autocomplete, the installed skill inventory, and packaged MCP behavior.
<!-- SECTION:NOTES:END -->

## Final Summary

<!-- SECTION:FINAL_SUMMARY:BEGIN -->
Replaced the generic CQC skill with argument-free project and uncommitted checks in both plugins. Preserved standalone installation for older release tags. Verified both client command menus, packaged MCP scope behavior, 33 focused tests, and Code Health. The user verified the updated Claude Code installation and accepted the task.
<!-- SECTION:FINAL_SUMMARY:END -->
