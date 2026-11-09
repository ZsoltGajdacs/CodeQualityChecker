---
id: TASK-5
title: Implement local CQC plugins for Claude Code and Codex
status: Done
assignee:
  - '@codex'
created_date: '2026-11-09T05:37:00Z'
updated_date: '2026-11-09T05:43:00Z'
labels: []
dependencies: []
ordinal: 5000
---

## Description

<!-- SECTION:DESCRIPTION:BEGIN -->
CQC currently requires manual MCP registration. Deliver locally installable client-specific bundles with a shared review-and-improve skill pointing to an existing macOS arm64 CQC installation, without changing server APIs or user settings during packaging.
<!-- SECTION:DESCRIPTION:END -->

## Acceptance Criteria
<!-- AC:BEGIN -->
- [x] #1 Generate version 0.1.0 Claude and Codex plugin packages and local catalogs from one canonical skill, using a validated absolute executable path.
- [x] #2 Skill covers scoped source-only review, requested fixes and rechecks, authorization for execution, and accurate separate or missing scores.
- [x] #3 Packaging tests cover invalid paths and paths with spaces; real MCP smoke tests invoke all six tools through both generated configurations without changing fixtures.
- [x] #4 Validate manifests and skill and verify isolated client installation plus skill/tool discovery; exercise workflow scenarios and record evidence.
- [x] #5 Document generation, installation, reload, removal, duplicate registrations and relocation; deliver generated packages under ignored dist.
<!-- AC:END -->

## Implementation Plan

<!-- SECTION:PLAN:BEGIN -->
1. Inspect current manifests, MCP smoke harness and client CLI support. 2. Add shared skill, client templates and deterministic local package generator. 3. Add behavioral packaging tests and real MCP smoke validation. 4. Validate isolated client installs, skill scenarios and Code Health, then document evidence and finalize task.
<!-- SECTION:PLAN:END -->

## Implementation Notes

<!-- SECTION:NOTES:BEGIN -->
Implemented canonical skill, client manifests and atomic package generator. Independent planner/explorer verified approach. Six packaging behavior tests and skill validator pass; initial failing success-case test preceded generator implementation. Independent reviewer found no blocking issues; clarified Gradle source-only support and added empty-output/symlink tests. Python Code Health reviews reach 10.0. Real MCP launch stalls in sandbox; escalated smoke successfully invoked six tools for Claude before canonical-skill update invalidated the second-copy check, so regenerating both outputs before rerun.

Final verification: six behavioral packaging tests pass (space-containing executable, missing/directory/nonexecutable inputs, populated/empty output, unsafe output, symlink executable). Skill validator and Claude strict plugin/marketplace validators pass; public-directory license/icon diagnostics are informational for deferred public submission. Real smoke invokes all six tools through BOTH final generated configurations; verifies Java/C# entities, missing-coverage null scores, unsupported Python-only discovery and unchanged fixture digests. Isolated client verifier passed outside sandbox: Codex 0.166.0 installed enabled cqc 0.1.0, discovered cqc:cqc-review and all six live MCP tools; Claude 2.1.306 installed enabled plugin, inventoried skill and reported MCP Connected. No normal client config or credentials modified; no inference calls. Independent reviewer confirmed five requested skill scenarios plus Gradle source-only support and final script/docs review with no blockers. All five Python files Code Health 10.0; pre-commit safeguard passed with five eligible files. Sonar inactive because project has no .sonar/config.json. docs/plugins.md records reproducible validation and sandbox startup limitation. Final packages at dist/cqc-plugins/{claude,codex}, source changes left uncommitted and unpushed.
<!-- SECTION:NOTES:END -->

## Final Summary

<!-- SECTION:FINAL_SUMMARY:BEGIN -->
Delivered local Claude Code and Codex CQC 0.1.0 plugins from one canonical review-and-improve skill, safe atomic Python packaging, local catalogs, six packaging tests, real MCP smoke and isolated client verification scripts, and installation/update/removal documentation. All acceptance criteria verified. Native startup stalls in restricted sandbox; live checks passed outside it. Public distribution remains deferred. No global client installation performed.
<!-- SECTION:FINAL_SUMMARY:END -->
