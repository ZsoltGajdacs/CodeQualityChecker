---
id: TASK-17
title: Fix CQC source-only review output schema
status: Done
assignee:
  - '@codex'
created_date: '2026-11-24T09:56:00Z'
updated_date: '2026-11-24T09:59:00Z'
labels: []
dependencies: []
type: bug
ordinal: 17000
---

## Description

<!-- SECTION:DESCRIPTION:BEGIN -->
Claude rejects source-only reviews because the MCP output schema requires scores for every family, even though the production family omits unavailable combined scores.
<!-- SECTION:DESCRIPTION:END -->

## Acceptance Criteria
<!-- AC:BEGIN -->
- [x] #1 Both source-only review responses match the advertised family schema and retain static complexity and duplication distributions.
- [x] #2 Packaged regression coverage detects the old schema mismatch and verifies report retrieval and evidence-aware score buckets.
- [x] #3 Claude accepts the source-only summary and a report page from the rebuilt plugin.
<!-- AC:END -->

## Implementation Plan

<!-- SECTION:PLAN:BEGIN -->
1. Add a shared smoke assertion and reproduce the mismatch against the existing package.
2. Make scores an optional init property and share score distribution logic.
3. Run the .NET suite, package and plugin smoke checks, Claude acceptance, and CodeScene checks.
<!-- SECTION:PLAN:END -->

## Final Summary

<!-- SECTION:FINAL_SUMMARY:BEGIN -->
Made the production-family scores distribution optional in the schema and shared the projection's score grouping. The new smoke check reproduced the old mismatch in both review tools. The rebuilt package and both generated plugin smoke runs passed, including page retrieval and null score buckets for missing coverage. All 193 .NET tests passed with SDK 10.0.403. Claude Code accepted the review and the files page in a fresh session using a synthetic Git fixture. Independent review and the CodeScene safeguard passed. Updated the shared CQC skill to document the optional field.
<!-- SECTION:FINAL_SUMMARY:END -->
