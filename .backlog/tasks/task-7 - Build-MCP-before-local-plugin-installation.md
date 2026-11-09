---
id: TASK-7
title: Build MCP before local plugin installation
status: Done
assignee:
  - '@codex'
created_date: '2026-11-09T05:50:00Z'
updated_date: '2026-11-09T05:59:00Z'
labels: []
dependencies: []
ordinal: 7000
---

## Description

<!-- SECTION:DESCRIPTION:BEGIN -->
Installer scripts currently require pregenerated packages. The default install should build the MCP and configure the plugin command to use the repository build output directory.
<!-- SECTION:DESCRIPTION:END -->

## Acceptance Criteria
<!-- AC:BEGIN -->
- [x] #1 Both installers build the MCP and generate plugin configuration pointing to dist/cqc-osx-arm64/cqc by default before installing.
- [x] #2 Repeated installation and explicit prebuilt MCP/package overrides work safely; build or generation failure prevents client settings changes.
- [x] #3 Document build prerequisites and defaults; verify installer behavior and a real packaged build plus isolated client installations.
<!-- AC:END -->

## Implementation Plan

<!-- SECTION:PLAN:BEGIN -->
1. Reuse the existing macOS self-contained packaging build. 2. Add default build/generation preparation and explicit prebuilt override behavior. 3. Verify failures, repeated install and isolated real clients against fresh build, document and review.
<!-- SECTION:PLAN:END -->

## Implementation Notes

<!-- SECTION:NOTES:BEGIN -->
Implemented shared Python install orchestrator with thin existing shell entrypoints. No arguments build via package-macos.sh, generate fresh dist/cqc-plugin-install.* catalogs and point to default dist/cqc-osx-arm64/cqc. --mcp-dir selects existing MCP; positional generated root remains install-only. Validation precedes inventory/settings changes; refresh removes only matching CQC identity. Real default runs for BOTH clients passed full pinned build, all-six packaged smoke, generation and isolated installation; cached .mcp.json command asserted exact build-output executable. Independent reviewer reproduced Claude project-only marketplace inventory scope issue; fixed by running inventory/refresh from a neutral temporary directory while preserving user-scoped commands. Build/generation remain in original context. Installer helper Code Health10; ShellCheck passes. Final tests and mixed-scope/changed-command refresh checks underway.

Final evidence: both no-argument installer workflows completed real pinned Maven/.NET/jlink build, packaged all-six MCP smoke, fresh plugin generation and isolated client installation. Cached MCP configs point exactly to repository dist/cqc-osx-arm64/cqc. Final public-CLI installer suite 13/13 and existing packaging suite 6/6 pass. Real prebuilt tests changed two MCP directories containing spaces sequentially in BOTH clients and confirmed exactly one enabled plugin and updated cached executable. Neutral cwd alone was insufficient because Claude caches project catalogs globally; final correction checks user settings.json extraKnownMarketplaces instead of merged inventory. Real project-only catalog regression now passes with project settings byte-identical. Malformed inventory/settings stop before mutations. Independent reviewer reran all 13 tests and found no blockers. Both new Python files Code Health10.0; safeguard passed with two eligible files; wrappers ShellCheck and diff whitespace checks pass. Sonar inactive (no project configuration). README and plugin guide document build prerequisites/defaults, --mcp-dir, install-only compatibility and refresh behavior. Changes remain uncommitted/unpushed.
<!-- SECTION:NOTES:END -->

## Final Summary

<!-- SECTION:FINAL_SUMMARY:BEGIN -->
Both client installers now build and verify the MCP by default and configure its build output dist/cqc-osx-arm64/cqc. Shared orchestration supports prebuilt MCP directories and existing plugin packages, validates before user settings changes, and refreshes matching registrations safely. Real default builds and isolated client installs, prebuilt path refresh, 13 installer tests, six packaging tests, independent review and applicable quality checks passed. No normal user settings were changed by validation.
<!-- SECTION:FINAL_SUMMARY:END -->
