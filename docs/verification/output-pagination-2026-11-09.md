# Bounded MCP report delivery verification — 2026-11-09

The original Recurring-billing `check_all` request selected `impl`, disabled execution, and allowed 600 seconds. Claude's MCP log closed the transport after about 51 seconds because the response exceeded its 16 MB message boundary limit. Direct capture confirmed a valid 75,030,652-byte JSON-RPC response.

## Captured-report replay

The original structured report was deserialized into the existing Core analysis contract and passed through the new summary, store, and pagination components.

| Measurement | Result |
| --- | ---: |
| Original JSON-RPC response | 75,030,652 bytes |
| New summary tool result, measured with MCP SDK serialization | 1,560 bytes |
| Largest tested raw/finding page tool result | 204,785 bytes |
| Canonical complete report JSON | 32,113,892 UTF-8 bytes |
| Raw pages reconstructed | 981 |
| Original top-level diagnostics retained | 56,052 |
| Selected files retained | 607 |

Concatenated raw chunks exactly matched the canonical complete report JSON. Its SHA-256 was `48cad771c5434918bcfd9addeb45f792e747a6b6cd91c630317edca47b4ce9f6`. All four finding sections were paged to completion and their counts matched the original report. The replay completed in about 2.5 seconds, including report publication and retrieval.

## Original request against the final package

The same source-only `impl` request completed in 48.47 seconds. Its summary tool result was 1,679 bytes when re-encoded by the Python verification client. Follow-up file retrieval and inspection succeeded on the same connection, source fingerprints agreed, and hashes of 1,109 Java/POM source inputs remained unchanged.

| Family | Entities | Findings | Status |
| --- | ---: | ---: | --- |
| code | 685 | 24 | incomplete |
| dependencies | 1,479 | 542 | incomplete |
| duplication | 1,479 | 295 | incomplete |
| tests | 562 | 366 | incomplete |

Null scores and incompleteness were preserved. This change fixes delivery; missing coverage and unresolved Java semantics remain analysis limitations.

One preliminary live verification overlapped package replacement and failed during follow-up inspection because runtime files were being replaced. It was discarded and rerun successfully against the stable final package. Package generation and verification must run sequentially.

## Automated and independent checks

- Real stdio tests measure complete JSON-RPC messages below 1 MiB and tool results at or below 512 KiB. Coverage includes Unicode/escaped filenames, raw reconstruction, cursor traversal, large input errors, and continued connection use.
- Reporting tests cover filters, empty sections, file-entity findings, changing page limits, oversized-record references, null scores, diagnostic summaries, expiry, count/physical-byte eviction, rejected publications, cancellation, concurrent reads, and disposal.
- The final full .NET suite passed: 101 tests, zero failures or skips, including existing scoring and isolated execution regressions.
- Full macOS packaging succeeded with pinned JBRSDK and self-contained .NET. The bundled-runtime smoke test passed with host runtime discovery disabled.
- Both generated Claude and Codex plugin configurations passed seven-tool smoke tests. Six plugin-packaging tests and thirteen installer tests passed.
- Changed eligible source files received Code Health 10.0. The safeguard passed with 19 eligible files checked and no issues. `Program.cs` has no numeric score because it contains top-level composition code.
- Independent review found and verified fixes for cleanup-lock release, pending-write storage accounting, file-entity finding associations, and retained cleanup failures. No unresolved review findings remain.
- Sonar is inactive because this repository has no `.sonar/config.json`.

## Operational behavior

Inspection and check tools now always return schema-v2 summaries. `get_report_page` retrieves details. Reports expire after 60 minutes and may be evicted at 10 reports or 512 MiB physical data; restart removes report access. Raw chunks preserve full report data, including exceptional records too large for typed pages. Source-only analysis and retrieval do not write into the analyzed repository.

Store I/O and eviction use one asynchronous gate to protect active reads. Publication measures data before reserving space and serializes it again into capped disk streams; this trades additional serialization work for simple, verifiable disk bounds. Pending cleanup failures block further publication until cleanup succeeds.

Artifacts were rebuilt under ignored `dist/`, including `cqc-osx-arm64.tar.gz` and generated plugin marketplaces in `cqc-pagination-plugins/`. Existing client settings were not changed; reconnect the CQC server to load the new API, and refresh the plugin to obtain the updated review skill.
