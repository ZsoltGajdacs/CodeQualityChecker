---
id: TASK-16
title: Build in the project directory and retire the reference cache
status: Done
assignee:
  - '@codex'
created_date: '2026-11-24T08:14:00Z'
updated_date: '2026-11-24T08:26:00Z'
labels: []
dependencies: []
type: enhancement
ordinal: 16000
---

## Description

<!-- SECTION:DESCRIPTION:BEGIN -->
Replace snapshot builds and the duplicated reference binaries with in-place builds and verified direct references.
<!-- SECTION:DESCRIPTION:END -->

## Acceptance Criteria
<!-- AC:BEGIN -->
- [x] #1 Explicit Maven and .NET builds use the original checkout and retain normal build outputs.
- [x] #2 Evidence-aware replay verifies direct binary references without creating or reading the legacy binary cache.
- [x] #3 Coverage and manifests are published from the verified current run only.
- [x] #4 Docs and MCP descriptions explain in-place execution and manual legacy-cache removal.
- [x] #5 Regression tests, packaged MCP smoke, and CodeScene gates pass.
<!-- AC:END -->

## Implementation Plan

<!-- SECTION:PLAN:BEGIN -->
1. Replace source copies with request-owned scratch and centralized staged evidence.
2. Build Maven and .NET in place, verify direct references and use Maven class directories directly.
3. Update documentation and run regression tests, package smoke, and CodeScene checks.
<!-- SECTION:PLAN:END -->

## Implementation Notes

<!-- SECTION:NOTES:BEGIN -->
Moved execution to the checkout; staged evidence and JaCoCo/OpenCover files under a per-run temp directory. References now point at verified build artifacts. Two real Maven runs reused compiled outputs. The worker suite passed 29 tests; the full .NET suite and packaged checks are in progress.

Full .NET suite passed 193 tests, including repeated real Maven and .NET builds, stale evidence, cancellation, publication preflight, and folder aliases. Java worker passed 29 tests. CodeScene safeguard passed across 50 eligible changed files. The package smoke test is still running.

The pinned macOS self-contained package passed its eight-tool MCP smoke test. A scratch build through the macOS /tmp alias omitted transitive dependencies, so packaging was rerun from the canonical path with the pinned SDK and JBR.
<!-- SECTION:NOTES:END -->

## Final Summary

<!-- SECTION:FINAL_SUMMARY:BEGIN -->
Maven and .NET now build in the checkout. CQC verifies existing JARs, class directories and DLLs in versioned manifests, stages per-run coverage and publishes after verification. Legacy binary caches are left for manual removal. All 193 .NET tests, 29 Java worker tests, eight packaged MCP tools and the CodeScene safeguard passed.
<!-- SECTION:FINAL_SUMMARY:END -->
