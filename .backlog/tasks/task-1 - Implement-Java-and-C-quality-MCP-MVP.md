---
id: TASK-1
title: Implement Java and C# quality MCP MVP
status: Done
assignee:
  - '@codex'
created_date: '2026-11-07T17:16:00Z'
updated_date: '2026-11-07T18:26:00Z'
labels: []
dependencies: []
type: feature
ordinal: 1000
---

## Description

<!-- SECTION:DESCRIPTION:BEGIN -->
Build the approved modular local MCP: Java/C# analyzers, four shared quality families, evidence-aware 0–10 file/class scores, controlled execution and macOS arm64 distribution.
<!-- SECTION:DESCRIPTION:END -->

## Acceptance Criteria
<!-- AC:BEGIN -->
- [x] #1 Six MCP tools support mixed-language discovery and repository, paths and Git-change scopes.
- [x] #2 Four check families provide stable findings, locations, metrics and explainable deductions; 10 is the desired score.
- [x] #3 Analysis-only never executes projects; missing/stale evidence blocks affected scores.
- [x] #4 Explicit execution collects provenance-matched coverage and semantics without editing tracked source/config.
- [x] #5 Behavioral fixtures and MCP tests cover correctness, incompleteness, failures, cancellation and determinism.
- [x] #6 macOS arm64 self-contained package bundles JVM and passes runtime-independent smoke test.
- [x] #7 Pinned dependencies, attribution, ruleset and extension docs are quality-reviewed.
<!-- AC:END -->

## Implementation Plan

<!-- SECTION:PLAN:BEGIN -->
1. Define contracts and fixtures. 2. Build discovery, config, language modules and shared engines. 3. Add scoped orchestration, coverage, execution and MCP tools. 4. Test and review. 5. Package, document, quality-check and finalize.
<!-- SECTION:PLAN:END -->

## Implementation Notes

<!-- SECTION:NOTES:BEGIN -->
Implemented contracts, discovery, normalized language facts, Roslyn/JavaParser adapters and four shared engines. 15 initial engine tests passed. Both adapters build; Java worker has five subprocess behavioral tests. Review found stale reference accumulation, ancestor-POM execution omissions and unsafe report output; fixes underway. Explicit builds now use isolated project copies. Sonar inactive: no .sonar/config.json. CodeScene file reviews underway.

End-to-end validation now passes 47 .NET tests plus 10 Java worker tests. Verified real mixed Maven/JUnit5 and .NET/xunit execution, all four complete families, exact branch coverage, byte-stable replay, source preservation, all six MCP tools, timeout/cancellation, Git scopes and regression cases. Review fixes include declaration completeness, static-import dependencies, generic coverage aliases, OpenCover point union across reports, tracked/symlink artifact guards and source-change rejection. Packaging smoke has passed without host Java/.NET; final package awaiting source freeze.

Architecture review moved manifest discovery, build execution and coverage mapping completely into language modules via registration metadata and execution contracts. 58 .NET tests and 11 Java tests passed; two additional registry/fingerprint tests passed. Bundled runtime expanded to full Java SE after package smoke exposed missing java.sql classes. Package smoke passed with no host runtimes. Pre-commit Code Health gate reported newly introduced maintainability findings (parameter counts, discovery primitive arguments and complexity); refactoring these before committing, then final package will be rebuilt.

Resolved every introduced Code Health finding; pre-commit gate PASSED with no issues across 85 eligible files. Internal worker schemaVersion 1 now enforced in both directions, with incompatible-version rejection test. Standard-library-only reflection avoids leaking bundled analyzer dependencies into target resolution. Java worker suite now14/14; full .NET suite60/60 passed before final decoder-only extraction, with final rerun underway. Final package rebuild will use this gate-clean source.

Final verification: dotnet test CodeQualityChecker.sln --no-restore --nologo passed62/62; python3 java-worker/test_worker.py passed14/14. Real MCP client covers all6tools; real mixed Maven/JUnit5 and .NET/xunit builds cover fresh coverage, failure, replay and source preservation. Fixed and reproduced host-dependency leakage in both analyzers; C# platform metadata is now restricted to actual framework runtime assets. Final CodeScene pre-commit gate passed86 eligible files with no findings. Sonar remains inactive because the project has no .sonar/config.json. Final package clean-build/publish and all-six-tool smoke passed with PATH containing only Git, including Java SQL/HTTP and .NET data APIs. Verified575 package hashes and exact current DLL/JAR/docs matches; archive checksum0843f9be87729e1a154e25fe42957576aca0472ef9c70be355ad431424f99fd5. Implementation committed locally as da4511a on codex/mcp-java-csharp-mvp; no Git remote configured. Action log and acceptance evidence retained in this task; final build log is /private/tmp/cqc-package-final.log.
<!-- SECTION:NOTES:END -->

## Final Summary

<!-- SECTION:FINAL_SUMMARY:BEGIN -->
Delivered the Java/C# MCP MVP: six language-neutral tools, automatic discovery and scoped dispatch, four shared quality families, explainable per-file/class 0–10 scores, strict evidence provenance, isolated explicit execution, and a self-contained macOS arm64 package. All62 .NET acceptance tests and14 Java worker tests pass; Code Health gate is clean across86 files; packaged MCP passes without host Java/.NET and all575 file hashes match. Setup, rules, language extension, execution/provenance, attribution and distribution documentation are included. Unsupported constructs/configurations are explicitly incomplete rather than assigned verified scores. Package: dist/cqc-osx-arm64.tar.gz. Implementation commit: da4511a; local branch codex/mcp-java-csharp-mvp, no remote configured.
<!-- SECTION:FINAL_SUMMARY:END -->
