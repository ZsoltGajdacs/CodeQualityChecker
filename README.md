# CodeQualityChecker MCP

A local MCP server for deterministic Java, C#, TypeScript, and React quality checks. Language adapters collect facts and shared engines calculate findings and scores. TypeScript and React support static complexity, structural duplication, and source-module dependencies for `.ts` and `.tsx`.

**10 is the desired score.** Each distinct scored finding deducts one point from its check-family score, clamped at zero. There is no combined score. Missing required evidence produces `score: null`, never an invented 10. These are CQC scores, not CodeScene Code Health scores.

## Install on macOS Apple Silicon

Extract `dist/cqc-osx-arm64.tar.gz` into a permanent directory. Keep the server, workers, and bundled Java and Node runtimes together. Source analysis runs with the bundled runtimes.

Example Codex MCP configuration:

```toml
[mcp_servers.cqc]
command = "/absolute/path/cqc-osx-arm64/cqc"
```

The executable speaks MCP over stdio; logs go to stderr. Do not launch it through a shell that writes greetings to stdout. No HTTP service or account is needed.

For a bundled review-and-improve skill and local plugin installation in Claude Code or Codex, see [local plugins](docs/plugins.md). The installer scripts build the MCP and use `dist/cqc-osx-arm64` as its directory by default; prebuilt installations can also be selected.

## Tools

| Tool | Result |
| --- | --- |
| `inspect_project` | Languages, modules, integrations, and missing build prerequisites |
| `check_code_quality` | Quality review of the whole repository |
| `check_uncommitted_changes_quality` | Quality review of staged, unstaged, and untracked source |
| `check_test_quality` | Test assertions, branching, size, and helper/fixture contributions |
| `check_duplication` | Structural duplicate groups within each language |
| `check_dependencies` | Semantic type/component cycles, coupling, and optional boundaries |
| `check_all` | All four check families with shared discovery/parsing |
| `get_report_page` | Bounded detail pages from an immutable completed report |

### Review the repository or uncommitted changes

Use `check_code_quality` for the repository or `check_uncommitted_changes_quality` for uncommitted files. Both tools take a nested request with `projectRoot` and optional `execute`, `configPath`, and `timeoutSeconds` (1–3600).

```json
{
  "request": {
    "projectRoot": "/absolute/path/to/repository",
    "execute": false
  }
}
```

Both tools default to `execute: false`. They report static cyclomatic complexity and structural duplication using source and static project configuration. These checks include Java field expressions, initializer blocks, compact record constructors, and enum initialization. They skip saved build and coverage evidence and omit metrics that require it. The code family summary includes `scoredFileCount` and `unscoredFileCount` for selected production files and explicit source failures. A structural gap in a language’s duplication pool can withhold its numeric scores while preserving verified matches.

The changes tool requires Git. It scores methods and other executable constructs touched in the working-tree diff against `HEAD`, including staged and unstaged edits. Dependency findings retain each applicable touched reference. A repeated dependency contributes one deduction per affected entity. New files and rename destinations are reviewed in full. Deleted files are excluded. The selected file list can include files with no applicable touched code. Duplicate findings can show unchanged counterparts as context, with deductions applied to touched members. On a clean checkout the tool returns zero files without building or testing.

### Explicit scope and execution

Use `check_all` or an individual family tool for explicit selection. These tools default to `scope: "repository"` and also accept `scope: "paths"` with repository-relative `paths`, or `scope: "changed"` with an optional `gitBase` commit or branch. Omit `gitBase` to select uncommitted changes only. These tools can use saved matching evidence in analysis-only mode, including evidence supplied through `reportPath`. Raw JaCoCo or OpenCover XML alone cannot establish source-matched provenance.

When explicitly requested, `execute: true` makes both review tools run all four check families with evidence collected through project-controlled restores, builds, and tests. Execution runs in the checkout, reuses normal build outputs, and writes the evidence bundle `.cqc/evidence-v1.zip`. Project build and test code can modify the checkout. Maven uses the project wrapper when available. The supported execution integrations are Maven/JUnit 5 and SDK-style .NET/xUnit with VSTest.

Every inspection and check returns a compact schema-v3 summary in MCP `structuredContent`, with a report ID, expiry, fingerprints, counts, per-family completeness and score distributions. Text content provides a short explanation. Diagnostic counts show at most 20 code groups and identify remaining groups. Scoped groups show `selectedCount`, `contextCount`, and `projectWideCount` when positive. `unsupported_language` lists the distinct extensions discovered across the repository. Independent families may still produce scores when another family is incomplete.

Retrieve details without rerunning analysis:

```json
{
  "request": {
    "reportId": "<reportId from summary>",
    "section": "findings",
    "family": "code",
    "limit": 50
  }
}
```

`get_report_page` supports selected `files`, `modules`, `entities`, `findings`, `diagnostics`, `metrics`, and inspection `prerequisites`. Diagnostic pages show selected-file entries first and include the triggering construct and source range when available. Use optional `family` and `entityId` filters where applicable and keep them fixed when passing a returned `nextCursor`. The default limit is 50 records, with 1–200 accepted. A null `nextCursor` means that section is exhausted. Detail retrieval does not execute project code or reread source files.

All tool results are bounded to 512 KiB, including JSON escaping and text content; a byte budget may reduce the record count. Exceptionally large records explicitly direct you to `section: "raw"`. Raw pages contain `rawChunk` strings: concatenate them in cursor order to reconstruct the complete original report JSON without loss. A partial page does not mean analysis is incomplete; use family statuses to assess analysis completeness.

Reports live in private server temporary storage for 60 minutes, with at most 10 published reports and 512 MiB of published data. A replacement can use up to another 512 MiB while it is staged. Old reports can be evicted after successful publication, and restarting the server removes access. Expired, evicted, or unknown IDs return explicit errors. Storage and page retrieval do not write into the analyzed repository.

## Build and verify

```sh
mvn -B -f java-worker/pom.xml package
npm ci --prefix typescript-worker --ignore-scripts
dotnet test CodeQualityChecker.sln
./scripts/package-macos.sh
```

The package build requires .NET SDK 11.0.100, Maven, Node 24 with npm, and the pinned JBR JDK. The build fetches or reads the pinned Node archive and verifies its checksum. Package generation runs a real MCP smoke test with only Git on `PATH`. See [distribution](docs/distribution.md).

See [ruleset and configuration](docs/ruleset.md), [architecture and extension contract](docs/architecture.md), [evidence and execution](docs/evidence.md), and [third-party attribution](THIRD_PARTY.md).

## Evidence-specific assessments (ruleset 3)

Evidence-aware production entities retain their combined complexity/CRAP score and expose a separate `staticComplexity` assessment. The static score deducts once per complexity finding and requires complete syntax, without requiring coverage or call resolution. A numeric static score never establishes a combined production score. Entity pages expose `blockers` for the primary score and status, score, finding IDs and blockers for `staticComplexity`; summaries include separate static score distributions.

Use `check_dependencies` or `check_all` with `execute: false` for TypeScript dependency checks. TypeScript follows static project configuration and uses dependencies already present on disk. Findings identify source modules and import locations. Test-quality scoring and coverage collection are unavailable for TypeScript, including during an explicit mixed-language run.

Java schema-2 facts identify symbols by Maven module and main/test source set. Explicit execution collects evaluated module contexts, preserves dependency order, and records paths and hashes for existing build outputs and dependencies in the evidence bundle’s `java-contexts.json`. Analysis-only calls to the scoped tools can reuse verified matching contexts; without them semantic-dependent scores remain incomplete while valid syntax assessments remain available. Different dependency versions in unrelated modules are permitted. Legacy reference manifests require a fresh execution. Upgrade the server and worker together. After refreshing evidence, you can manually remove any old `.cqc/references/` directory. Builds can still leave unsupported constructs or unresolved generated methods.
