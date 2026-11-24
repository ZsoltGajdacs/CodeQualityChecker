---
id: TASK-20
title: Add TypeScript and React support to CodeQualityChecker
status: Done
assignee:
  - '@codex'
created_date: '2026-11-24T13:10:00Z'
updated_date: '2026-11-24T13:30:00Z'
labels: []
dependencies: []
type: feature
ordinal: 20000
---

## Description

<!-- SECTION:DESCRIPTION:BEGIN -->
Deliver structural complexity, duplication and resolved source dependencies for .ts and .tsx through an isolated language adapter. Score source modules. Defer JavaScript, test quality and coverage.
<!-- SECTION:DESCRIPTION:END -->

## Acceptance Criteria
<!-- AC:BEGIN -->
- [x] #1 Registered analyzers drive scoped diagnostics and analysis-only languages coexist with executable Java/C# modules.
- [x] #2 TS and TSX produce deterministic structural scores and compiler-resolved dependency findings with scoped incomplete evidence.
- [x] #3 The macOS package includes pinned Node and TypeScript and smoke tests exercise installed analysis without host runtimes.
- [x] #4 MCP fixtures, documentation, canonical plugin skill and repository quality checks cover the new language.
<!-- AC:END -->

## Implementation Plan

<!-- SECTION:PLAN:BEGIN -->
1. Refactor discovery, diagnostics, execution selection, and worker transport.
2. Add the isolated TypeScript analyzer and verify TS/TSX with real fixtures and MCP.
3. Bundle the pinned runtime and compiler, extend smoke tests, update plugin guidance, and complete quality checks.
<!-- SECTION:PLAN:END -->

## Implementation Notes

<!-- SECTION:NOTES:BEGIN -->
Shared registration, execution selection and worker transport refactors passed 29 focused regression tests. Added the TypeScript compiler worker, tests and host adapter, then staged the verified Node 24.22.3 archive for package validation. Full tests and packaging are in progress.

The first full run exposed inherited-only tsconfig ownership, empty-project evidence validation, and a test replay using a different analyzer registry. Fixed all three and reran the full .NET suite against the final worker. Verified React JSX runtime and conditional package exports through a workspace symlink. Final checks passed: 225 .NET tests, 31 Java worker tests, 11 TypeScript worker tests, 20 plugin packaging and installer tests, package and both plugin MCP smokes, missing bundled Node diagnostics, archive checksum and CodeScene safeguard across 41 eligible files. The project has no Sonar configuration.
<!-- SECTION:NOTES:END -->

## Final Summary

<!-- SECTION:FINAL_SUMMARY:BEGIN -->
Added the isolated Cqc.TypeScript adapter and compiler worker for TS and TSX complexity, duplication, and resolved dependencies. Shared discovery and diagnostic routing are registration-driven, and analysis-only modules coexist with Java and C# execution. The verified macOS package bundles Node 24.22.3 and TypeScript 6.0.3. TypeScript test-quality scoring and coverage collection remain deferred. Workspace changes are local and uncommitted.
<!-- SECTION:FINAL_SUMMARY:END -->
