---
id: TASK-25
title: Automate versioned macOS releases on master
status: In Progress
assignee:
  - '@codex'
created_date: '2026-11-25T07:15:00Z'
updated_date: '2026-11-25T07:22:00Z'
labels: []
dependencies: []
ordinal: 24000
---

## Description

<!-- SECTION:DESCRIPTION:BEGIN -->
Build and publish a downloadable CQC executable automatically when changes land on master.
<!-- SECTION:DESCRIPTION:END -->

## Acceptance Criteria
<!-- AC:BEGIN -->
- [ ] #1 The master push workflow creates a Semantic Version tag and drafts a release for the triggering commit.
- [ ] #2 The macOS arm64 package is built from the exact tag and published with its checksum after packaging and smoke checks pass.
- [x] #3 Agent guidance documents Conventional Commit messages, squash merges, and the release bump policy.
<!-- AC:END -->

## Implementation Plan

<!-- SECTION:PLAN:BEGIN -->
1. Define the version rule and test it against disposable Git histories.
2. Sequence the master workflow to tag, draft, build and smoke from the tag, and publish with its checksum.
3. Document the commit convention and verify the package path, workflow, and quality gates.
<!-- SECTION:PLAN:END -->

## Implementation Notes

<!-- SECTION:NOTES:BEGIN -->
Reviewed release tooling and the canonical cqc-review skill. The release workflow changes no MCP tool contract or review instructions.
Verified real Git histories for bootstrap, patch fallback, highest bump, breaking footers, stale pushes, and published release ordering. Exercised actual workflow shell steps with a disposable bare Git origin and a GitHub CLI stub for draft, retry, published skip, and upload failure behavior.
GitHub documents concurrency.queue. Local actionlint 1.7.12 passed with a targeted exception for that schema key. Python compilation and git diff --check passed.
Initial sandboxed packaging hit local cache and .NET restore restrictions. Native packaging in a temporary checkout passed the MCP smoke test and archive SHA-256 verification. CodeScene review scored the release resolver 10, and the safeguard passed with one eligible file. Sonar is unconfigured.
The first GitHub-hosted master push remains unverified. Leave acceptance criteria 1 and 2 open until actual GitHub tag creation, asset publication, and runner behavior are observed.
<!-- SECTION:NOTES:END -->
