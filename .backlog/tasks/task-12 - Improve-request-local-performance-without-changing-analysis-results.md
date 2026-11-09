---
id: TASK-12
title: Improve request-local performance without changing analysis results
status: Done
assignee:
  - '@codex'
created_date: '2026-11-09T14:29:00Z'
updated_date: '2026-11-09T14:55:00Z'
labels: []
dependencies: []
documentation:
  - docs/verification/request-local-performance-2026-11-09.md
type: enhancement
ordinal: 12000
---

## Description

<!-- SECTION:DESCRIPTION:BEGIN -->
Implement the approved performance plan: exact duplicate candidates, request-local scoring indexes, discovery and adapter reductions, linear report paging and once-per-workspace execution setup. Preserve public interfaces, tooling, evidence checks and deterministic outputs.
<!-- SECTION:DESCRIPTION:END -->

## Acceptance Criteria
<!-- AC:BEGIN -->
- [x] #1 Baseline and optimized measurements cover discovery, adapters, all rule families and reporting with output parity.
- [x] #2 Exact duplicate matching and indexed scoring preserve findings, scores, blockers and ordering.
- [x] #3 Discovery, adapter, paging and execution optimizations retain scope, provenance and budgets.
- [x] #4 Full tests, Java worker, real execution, packaged smoke checks and CodeScene gates pass.
<!-- AC:END -->

## Implementation Plan

<!-- SECTION:PLAN:BEGIN -->
1. Capture baseline (warm-up plus five runs). 2. Index scoring and exact duplicate matching. 3. Optimize discovery, adapters, reports and execution setup. 4. Differentially verify results and performance. 5. Complete full tests and CodeScene gates, record evidence.
<!-- SECTION:PLAN:END -->

## Implementation Notes

<!-- SECTION:NOTES:BEGIN -->
Captured original revision 74d1d68 in an isolated temporary tree before implementation. Added benchmark harness; baseline raw C# adapter diagnostics proved nondeterministic in order only, so raw-adapter parity canonicalizes diagnostics by full content. Scoring/discovery slices passed focused tests and CodeScene 10.0 reviews. Exact duplicate candidates use an adaptive direct path for dense postings; sparse workloads use postings and exact size bounds. Full verification underway.

Completed exact candidates, lazy scoring/evidence indexes, asynchronous streamed discovery fingerprints, one Git ignore invocation, C#/Java ownership/reachability indexing, linear paging/indexing and once-per-workspace .NET setup. Initial measurement exposed eager empty-family overhead; fixed and repeated full validation. Baseline raw diagnostic-order nondeterminism was verified and canonicalized only in raw-adapter hashes. Discarded test run overlapped worker replacement; sequential reruns clean. Final 34 stage hashes match, repository check_all 25.3% faster and 17.1% less allocated, sparse duplicate checks 96.2% faster, byte-limited paging 85.7% faster. Full .NET 170, worker 27, script 19, fresh packaged seven-tool and two-client plugin smoke all pass. Independent review clean; final CodeScene safeguard passed 32 eligible files with no issues. Sonar inactive (no configuration). Detailed log, limitations, reproducibility and raw samples are in the linked verification report.

Implementation is complete and staged. Final pre-commit CodeScene safeguard passed all 32 eligible files with no issues. Automatic approval review rejected creating a commit on master because user authorization covered implementation but did not explicitly authorize changing default-branch history. No commit was created; staged changes remain ready for user review/approval. No workaround attempted.
<!-- SECTION:NOTES:END -->

## Final Summary

<!-- SECTION:FINAL_SUMMARY:BEGIN -->
Implemented the approved request-local performance plan with unchanged public interfaces and matched outputs across 34 measured stages. Added exact duplicate-oracle and regression coverage, reproducible benchmark harness and complete samples. Final tests/package checks and CodeScene safeguard passed. No cross-request caches or approximate matching introduced.
<!-- SECTION:FINAL_SUMMARY:END -->
