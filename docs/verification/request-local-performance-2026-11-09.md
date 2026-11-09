# Request-local performance verification — 2026-11-09

Implemented TASK-12 against baseline `74d1d68e5cfb7a8d91b593da0b53897b2d232c90`.

## Results and correctness

All **34 measured stage hashes match** between baseline and optimized builds, and every measured iteration reproduced its stage hash. Repository `check_all` improved from **2,900 ms to 2,168 ms (25.3%)**, with **17.1% fewer .NET allocated bytes** and Git process count reduced from **12 to 4**. The small mixed fixture improved **27.8%** end to end. These are local measurements, not universal performance guarantees.

Sparse duplicate matching improved **96.2%** and performed **1,401 exact comparisons instead of 719,400**. Dense matching still performs all 79,800 exact comparisons and improved **52.3%**, retaining exact Jaccard thresholds and transitive groups. Byte-limited escaped paging improved **85.7%** with **84.9% fewer allocated bytes**.

The final samples contain no stage latency regression above 5%. Some adapters and publication allocate slightly more in exchange for lower latency; publication's finding index adds about 4.5–4.6% host allocations. Small-fixture discovery is effectively flat and allocates 7.7% more, while repository discovery is 58.1% faster with 25.1% fewer allocated bytes. Streaming hashing also bounds content-buffer memory for large files. Allocation snapshots do not establish retained/peak memory.

## Method and reproduction

- Same machine and stable input tree for both implementations, archived before edits; input tree initialized as Git, with baseline reference metadata built once before both runs.
- .NET SDK 10.0.403, .NET 10, JBR 25.0.2+10-b300.57; matching worker built from each revision. No dependency/tool versions changed.
- Release harness, `DOTNET_TieredCompilation=0`, one warm-up plus five measured samples per stage. Baseline and optimized runs executed sequentially without competing test/build processes.
- [Harness and commands](../../scripts/performance/README.md), [baseline samples](request-local-performance-baseline-2026-11-09.json), [optimized samples](request-local-performance-optimized-2026-11-09.json).
- Timings include Java worker startup. Allocations and working set cover the .NET host, excluding child JVM memory. Working set is a process snapshot rather than stage peak; inspect raw samples rather than interpreting it as retained heap.
- Raw adapter diagnostic order is canonicalized by code/file/message for hashing: the original Roslyn adapter produced different diagnostic order across repeated runs, and comparison proved all other contents identical. Duplicate diagnostics are retained. All other fact/result array ordering remains strict. Publication IDs/expiry and cursor token values are normalized; continuation presence is checked.
- Hashes cover complete stage results, including findings, metrics, scores, statuses, blockers, fingerprints and diagnostics. Normal tests separately verify complete raw report reconstruction, cursor filters and wire budgets.

## Measurements

Medians of five samples; negative changes mean less time/allocation. Git counts measure actual subprocess launches through a temporary forwarding wrapper. Comparison counts run outside timing and use the optimized exact-search implementation; baseline counts are exhaustive eligible same-language pair counts.

| Stage | Before ms | After ms | Time change | Allocation change | Git before/after |
| --- | ---: | ---: | ---: | ---: | ---: |
| fixture.discovery | 100.35 | 99.73 | -0.6% | +7.7% | 2/2 |
| fixture.csharp | 40.54 | 32.76 | -19.2% | +13.8% | 0/0 |
| fixture.java | 329.64 | 308.74 | -6.3% | -23.7% | 0/0 |
| fixture.check_all | 764.32 | 551.69 | -27.8% | -2.5% | 6/4 |
| repository.discovery | 278.50 | 116.76 | -58.1% | -25.1% | 4/2 |
| repository.csharp | 989.25 | 860.31 | -13.0% | +1.8% | 0/0 |
| repository.java | 1138.34 | 1010.80 | -11.2% | +0.1% | 0/0 |
| repository.check_all | 2900.43 | 2168.03 | -25.3% | -17.1% | 12/4 |
| java-modules.java | 746.00 | 678.18 | -9.1% | +0.1% | 0/0 |
| byte-budget.page | 8.32 | 1.19 | -85.7% | -84.9% | 0/0 |
| sparse.code | 136.98 | 17.02 | -87.6% | -70.6% | 0/0 |
| sparse.tests | 0.25 | 0.14 | -43.5% | -71.3% | 0/0 |
| sparse.duplication | 550.20 | 20.72 | -96.2% | -89.6% | 0/0 |
| sparse.dependencies | 95.36 | 8.66 | -90.9% | -88.5% | 0/0 |
| sparse.publish | 193.55 | 128.72 | -33.5% | +4.5% | 0/0 |
| sparse.page | 6.48 | 0.54 | -91.6% | -82.4% | 0/0 |
| dense.code | 17.67 | 5.47 | -69.1% | -46.0% | 0/0 |
| dense.tests | 0.07 | 0.04 | -37.0% | -70.0% | 0/0 |
| dense.duplication | 221.55 | 105.71 | -52.3% | -5.2% | 0/0 |
| dense.dependencies | 13.05 | 2.21 | -83.1% | -75.5% | 0/0 |
| dense.publish | 48.49 | 41.32 | -14.8% | +4.6% | 0/0 |
| dense.page | 6.18 | 0.42 | -93.2% | -89.1% | 0/0 |
| contexts.code | 131.98 | 18.01 | -86.3% | -70.6% | 0/0 |
| contexts.tests | 0.25 | 0.14 | -43.5% | -71.3% | 0/0 |
| contexts.duplication | 508.55 | 22.07 | -95.7% | -89.6% | 0/0 |
| contexts.dependencies | 418.04 | 33.85 | -91.9% | -91.2% | 0/0 |
| contexts.publish | 197.62 | 136.64 | -30.9% | +4.5% | 0/0 |
| contexts.page | 6.22 | 0.53 | -91.5% | -82.4% | 0/0 |
| helpers.code | 0.18 | 0.18 | -1.5% | -0.5% | 0/0 |
| helpers.tests | 190.71 | 19.34 | -89.9% | -87.9% | 0/0 |
| helpers.duplication | 504.96 | 22.35 | -95.6% | -89.6% | 0/0 |
| helpers.dependencies | 100.66 | 7.65 | -92.4% | -88.5% | 0/0 |
| helpers.publish | 270.71 | 136.30 | -49.7% | +4.6% | 0/0 |
| helpers.page | 6.55 | 0.55 | -91.6% | -85.4% | 0/0 |

sparse.operations: exact comparisons 719,400 → 1,401
dense.operations: exact comparisons 79,800 → 79,800
contexts.operations: exact comparisons 719,400 → 1,401
helpers.operations: exact comparisons 719,400 → 1,401

## Implementation and review

- Exact duplicate candidates use fingerprint postings, safe set-size bounds, and direct comparison for dense postings. Threshold zero preserves exhaustive comparisons; cancellation remains observable.
- Lazy request-local fact indexes and per-family assessment maps avoid repeated global scans. Dependency evidence is memoized per context; contextless evidence and partial declarations retain their existing policies.
- Source-only requests perform two complete discovery passes instead of three. Execution retains all three. Scans reuse regex matchers, stream SHA-256 over asynchronous sequential streams, and send ignore candidates through one NUL-delimited Git invocation.
- C# ownership lookup preserves nearest-project and tie behavior; invocation symbol resolution is reused for assertion recognition. Java reachability is immutable and ordered, with indexed source/root ownership and separate context solvers.
- Paging measures each item's compact bytes once. Finding-to-report-entity relationships are indexed once per family. Storage preflight, lifetime locks, raw fallback, and final wire-size gate remain.
- .NET SDK/collector setup occurs once per execution workspace; every project build/test/reference command remains.
- Independent static review found no outstanding correctness, ordering or maintainability issues. Reviewed source/tests/helpers score Code Health 10.0; the harness's top-level Program has no numeric CodeScene score. Required safeguard passed with applicable files checked. Sonar is inactive because this project has no `.sonar/config.json`.

## Validation and investigation log

- Full solution: `dotnet test CodeQualityChecker.sln --no-restore -m:1 /p:UseSharedCompilation=false --verbosity minimal` — **170 passed**, including actual .NET/Maven execution, stale evidence and provenance regressions.
- `python3 java-worker/test_worker.py` — **27 passed** against rebuilt worker.
- `python3 scripts/test-package-plugins.py` — **6 passed**; `python3 scripts/test-install-plugin.py` — **13 passed**.
- Fresh `./scripts/package-macos.sh` — built package and passed bundled smoke for all **seven MCP tools**, Java/C# with host runtimes disabled, summaries, bounded pages and raw reconstruction.
- `python3 scripts/smoke-plugins.py dist/cqc-performance-plugins` — both Claude and Codex clients passed real seven-tool flows, missing coverage and unchanged-input checks.
- Added exhaustive randomized duplicate-oracle/boundary/cancellation tests; context cycles, missing contexts, request isolation and shared recursive/partial helpers; streamed fingerprint format, unusual Git filenames, ownership ties; escaped Unicode byte-budget paging.
- Initial benchmark caught eager-index overhead for inapplicable families. Fixed with lazy lookup materialization, relevant type maps and empty-family completion. Initial streamed hashing used a synchronous stream; corrected to asynchronous sequential I/O. Controlled rerun removed the measured latency regressions.
- One test pass overlapped package cleanup/replacement of the worker JAR. It was discarded; clean sequential full-suite/package and worker reruns passed. Final .NET/package checks were repeated after the last production change. Sandboxed test IPC needed approved escalation.
- `git diff --check` passed. No public MCP/configuration/scoring/worker interfaces changed; no cross-request caches, persistent workers or approximate matching were added.
