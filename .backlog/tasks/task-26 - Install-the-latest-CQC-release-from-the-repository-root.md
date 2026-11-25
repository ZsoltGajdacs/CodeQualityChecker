---
id: TASK-26
title: Install the latest CQC release from the repository root
status: Done
assignee:
  - '@codex'
created_date: '2026-11-25T07:38:00Z'
updated_date: '2026-11-25T07:41:00Z'
labels: []
dependencies: []
type: feature
ordinal: 25000
---

## Description

<!-- SECTION:DESCRIPTION:BEGIN -->
Users need a prebuilt installation path for Claude Code and Codex that can refresh an existing installation.
<!-- SECTION:DESCRIPTION:END -->

## Acceptance Criteria
<!-- AC:BEGIN -->
- [x] #1 The root installer downloads and verifies the latest macOS Apple Silicon release before replacing an earlier managed copy.
- [x] #2 One prompt lets users select Claude Code, Codex, or both, and the installer refreshes the chosen clients.
<!-- AC:END -->

## Implementation Plan

<!-- SECTION:PLAN:BEGIN -->
1. Add a root multi-select installer using the GitHub release archive and existing plugin installer.
2. Verify checksum and stage the runtime under dist before updating the managed copy.
3. Document and test client selection, replacement and failure behavior.
<!-- SECTION:PLAN:END -->

## Implementation Notes

<!-- SECTION:NOTES:BEGIN -->
Confirmed the public v0.3.0 release asset names and reproduced the missing root entry point with focused tests. Reused the existing client refresh path and kept the downloaded runtime separate from local builds. Independent review found a CDPATH issue, which was fixed and verified with a relative invocation in the live run.

Verified 17 installer tests, 6 package-generation tests, Bash syntax, ShellCheck and diff whitespace. Live v0.3.0 download replaced an older runtime and both real clients installed and refreshed their plugins in disposable settings. All eight packaged MCP tools passed the smoke test. CodeScene reported 10.0 for the Python test and its safeguard passed. Shell is outside CodeScene language coverage. SonarMCP is unconfigured.
<!-- SECTION:NOTES:END -->

## Final Summary

<!-- SECTION:FINAL_SUMMARY:BEGIN -->
Added an executable root installer that verifies the latest Apple Silicon release and offers Claude Code, Codex, or both. Updated installation documentation and the shared review skill. Verified both real client registrations and live release runtime behavior. Changes are local.
<!-- SECTION:FINAL_SUMMARY:END -->
