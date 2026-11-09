# Request-local performance harness

This macOS/Linux harness compares two builds on the **same stable input directory**. Keep the original source tree and Java worker before editing. Build the harness against each implementation, outside timed measurements:

```sh
dotnet build scripts/performance/Cqc.Performance.csproj -c Release \
  -p:CqcSourceRoot=/absolute/path/to/original-source -o /tmp/cqc-perf-before -m:1
# The default CqcSourceRoot is the current repository.
dotnet build scripts/performance/Cqc.Performance.csproj -c Release \
  -o /tmp/cqc-perf-after -m:1
DOTNET_TieredCompilation=0 CQC_JAVA_WORKER=/absolute/path/to/original-worker.jar \
  dotnet /tmp/cqc-perf-before/Cqc.Performance.dll /absolute/path/to/stable-input before.json
DOTNET_TieredCompilation=0 CQC_JAVA_WORKER=/absolute/path/to/updated-worker.jar \
  dotnet /tmp/cqc-perf-after/Cqc.Performance.dll /absolute/path/to/stable-input after.json
python3 scripts/performance/compare.py before.json after.json
```

The stable input must contain this repository's fixtures and source. Initialize it as a Git repository to measure ignore-check process counts. Do not build or edit that input between runs: cached reference metadata also affects analysis. Run without other builds/tests competing for CPU or replacing worker artifacts. Use the same SDK, JDK, runtime settings, and worker dependencies for both implementations. The commands disable .NET tiered compilation so background tier promotion does not distort early stage comparisons.

Each stage gets one warm-up and five measured iterations. Results contain elapsed milliseconds, .NET allocated bytes, process working set, and Git process counts recorded by a temporary forwarding wrapper. Java child allocations are not included in .NET allocation totals, and working set is a process snapshot, not per-stage peak or retained heap. Java timings include worker startup. The wrapper requires `/usr/bin/git`; it restores the environment and removes its temporary workspace on exit.

Stages cover mixed fixtures and the frozen repository (discovery, both adapters, check_all), 40 Java modules with transitive verified source contexts, escaped pages limited by bytes, and generated sparse/dense duplicates, reachable contexts, and shared helpers (every rule family, publication, and paging). Publication intentionally retains existing capacity preflight and storage locking. No project-controlled builds execute during measurement.

Every iteration verifies output hashes, and the comparison fails on changed output. Raw adapter diagnostics are sorted by code/file/message for hashing because the baseline Roslyn compilation returns diagnostics in nondeterministic order; all diagnostic contents and every other array order remain checked. Publication IDs/expiry and page cursor tokens are replaced by stable values/continuation presence. Analysis results, fingerprints, findings, scores, blockers, and metrics are not removed. Page correctness and full raw reconstruction are covered by the normal test suite.

Duplicate operation counts reflect actual exact Jaccard comparisons through an internal diagnostic entrypoint when available; the original exhaustive algorithm's count is calculated from eligible same-language pairs. Operation counting runs outside timed samples. Dense candidate selection can use direct comparisons when merging ubiquitous postings would be more expensive. There are no cross-request caches.

Treat repeatable timing regressions above 5% as investigation triggers, not flaky unit-test assertions. Inspect allocation/process reductions as well as latency, and repeat noisy stages before drawing conclusions.
