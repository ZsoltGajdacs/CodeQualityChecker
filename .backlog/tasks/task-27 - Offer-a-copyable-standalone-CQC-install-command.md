---
id: TASK-27
title: Offer a copyable standalone CQC install command
status: In Progress
assignee:
  - '@codex'
created_date: '2026-11-25T07:53:00Z'
updated_date: '2026-11-25T07:57:00Z'
labels: []
dependencies:
  - TASK-26
type: enhancement
ordinal: 26000
---

## Description

<!-- SECTION:DESCRIPTION:BEGIN -->
Make the README a concise entry point and let a user install CQC from the public GitHub repository without a local checkout.
<!-- SECTION:DESCRIPTION:END -->

## Acceptance Criteria
<!-- AC:BEGIN -->
- [ ] #1 The README provides a copyable shell command that installs or updates CQC from a fresh terminal and keeps client selection interactive.
- [x] #2 The standalone installer downloads the verified release runtime and its corresponding plugin files into a persistent user directory.
<!-- AC:END -->

## Implementation Plan

<!-- SECTION:PLAN:BEGIN -->
1. Add standalone installation using files from the resolved GitHub release tag.
2. Shorten the README around the copyable installer, usage and documentation links.
3. Exercise fresh installation and verify the managed runtime and selected client registrations.
<!-- SECTION:PLAN:END -->

## Implementation Notes

<!-- SECTION:NOTES:BEGIN -->
Independent review, 19 installer tests, 6 packaging tests, ShellCheck, Bash syntax, documentation links, and the CodeScene safeguard passed. The Python tests scored 10.0. Bash is outside CodeScene language coverage.

The exact README command installed live v0.3.0 and both real plugins from an empty working directory using disposable client settings. The test supplied the local bootstrap, then fetched the runtime and all six tagged helpers from GitHub. The packaged smoke test passed. The public bootstrap URL returns 404 until install.sh reaches master. Keep AC 1 pending until the public command can be verified.
<!-- SECTION:NOTES:END -->
