---
id: TASK-8
title: Bound CQC output with summaries and paginated details
status: Done
assignee:
  - '@codex'
created_date: '2026-11-09T07:41:00Z'
updated_date: '2026-11-09T07:54:00Z'
labels: []
dependencies: []
ordinal: 8000
---

## Description

<!-- SECTION:DESCRIPTION:BEGIN -->
Claude closed the MCP transport on an impl check because a valid 75 MB JSON-RPC response exceeded its 16 MB framing limit. Replace report delivery without changing scoring.
<!-- SECTION:DESCRIPTION:END -->

## Acceptance Criteria
<!-- AC:BEGIN -->
- [x] #1 All inspection and analysis replies use bounded schema v2 summaries and details remain available through seven-tool MCP API.
- [x] #2 Immutable indexed temporary reports support filtering, lossless raw retrieval, expiration, eviction and bounded errors.
- [x] #3 Real stdio, lifecycle and regression tests pass; captured large report and original request are verified.
- [x] #4 Plugin guidance, docs, packaging and Code Health checks are updated and verified.
<!-- AC:END -->

## Implementation Plan

<!-- SECTION:PLAN:BEGIN -->
1. Separate summary contracts, projections, indexed report storage, cursors and bounded serialization. 2. Replace six responses and add get_report_page. 3. Verify pagination, lifecycle, size limits, raw reconstruction and unchanged scoring. 4. Update docs/plugin and rebuild package; replay captured report and original impl request; review and finalize.
<!-- SECTION:PLAN:END -->

## Implementation Notes

<!-- SECTION:NOTES:BEGIN -->
2026-11-09: Reproduced baseline transport failure: valid 75,030,652-byte JSON-RPC response exceeded Claude message limit. Planner/explorer established transport-only scope and schema consumers. Added failing seven-tool stdio regression, then implemented schema-v2 summaries, indexed temporary storage, authenticated cursors, typed/raw pagination and shared byte gate. Updated plugin 0.2.0, skill, docs and smoke clients. Reviewer identified and verified cleanup semaphore release, pending-byte accounting, file finding associations and retained failed cleanup; no unresolved findings. Independent tester added actual-wire and storage/disposal tests. Captured-report replay: summary1560bytes, largest tested page204785bytes,981rawpages reconstructed exactly. Final packaged original impl request:48.47seconds,summary1679bytes,continued requests and1109source hashes unchanged. Initial live/package overlap invalidated follow-up inspection; reran successfully against stable package. Full package and both plugin smokes passed,6packaging+13installer tests passed; Code Health safeguard passed19eligible files. No Sonar config. Validation evidence: docs/verification/output-pagination-2026-11-09.md. Changes remain uncommitted; no client settings changed.

User requested a local commit after delivery. Rechecked the final diff and Code Health safeguard: passed, 19 eligible files checked, no issues. Committing the completed task, implementation, tests, documentation and plugin updates together; no push requested.
<!-- SECTION:NOTES:END -->

## Final Summary

<!-- SECTION:FINAL_SUMMARY:BEGIN -->
Replaced full MCP replies with schema-v2 summaries and a seventh paginated retrieval tool, with lossless raw access and bounded output. Separated projections, cursor validation, indexed temporary storage and serialization. Added absolute expiry, physical byte/count eviction, cancellation and cleanup protections. Plugin0.2.0 and documentation updated; final macOS package rebuilt. Validation:101 .NET tests,6 packaging tests,13 installer tests, bundled-runtime and both plugin smokes passed; captured75 MB replay produced a1560-byte summary and exact raw reconstruction; original impl request succeeded in48.47 seconds with1679-byte summary and unchanged inputs. Code Health10.0 for changed scored code; safeguard passed; independent review resolved. Sonar inactive (no configuration). See docs/verification/output-pagination-2026-11-09.md. Changes uncommitted; client settings unchanged.
<!-- SECTION:FINAL_SUMMARY:END -->
