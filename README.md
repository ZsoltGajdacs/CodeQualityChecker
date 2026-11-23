# CodeQualityChecker MCP

A local MCP server for deterministic Java and C# code/test quality checks. One generic tool interface dispatches to language modules, then shared engines calculate findings and separate file/class scores.

**10 is the desired score.** Each distinct scored finding deducts one point from its check-family score, clamped at zero. There is no combined score. Missing required evidence produces `score: null`, never an invented 10. These are CQC scores, not CodeScene Code Health scores.

## Install on macOS Apple Silicon

Extract `dist/cqc-osx-arm64.tar.gz` into a permanent directory. Keep the whole directory together: it contains the self-contained .NET server, Java worker, and Java runtime. No host Java or .NET runtime is required for source analysis.

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

Both tools default to `execute: false`. They report supported static cyclomatic complexity and structural duplication using source and static project configuration. They skip saved build and coverage evidence, and omit scores and metrics that need it. A source or analyzer failure may prevent an assessment and appears in diagnostics. The summary includes a brief note that execution may reveal additional findings.

The changes tool requires Git. It reads current working-tree content, including unstaged edits to a staged file, after applying ignore and generated-file exclusions. It excludes deleted files. On a clean checkout it returns zero files without building or testing. Findings can refer to unchanged counterpart files because checks use repository context.

### Explicit scope and execution

Use `check_all` or an individual family tool for explicit selection. These tools default to `scope: "repository"` and also accept `scope: "paths"` with repository-relative `paths`, or `scope: "changed"` with an optional `gitBase` commit or branch. Omit `gitBase` to select uncommitted changes only. These tools can use saved matching evidence in analysis-only mode, including evidence supplied through `reportPath`. Raw JaCoCo or OpenCover XML alone cannot establish source-matched provenance.

When explicitly requested, `execute: true` makes both review tools run all four check families with evidence collected through project-controlled restores, builds, and tests. Execution uses an isolated copy and writes reusable evidence, including `.cqc/coverage.json`, under the original root. Maven uses the project wrapper when available. The supported execution integrations are Maven/JUnit 5 and SDK-style .NET/xUnit with VSTest.

Every inspection and check returns a compact schema-v3 summary in MCP `structuredContent`, with a report ID, expiry, fingerprints, counts, per-family completeness and score distributions. Text content is a short explanation, not a second copy of the report. Diagnostic counts show at most 20 code groups and identify remaining groups. Scores remain separate by family. An incomplete family does not prevent an independent family from scoring. Recommendations are advisory; a high score does not prove functional correctness or test effectiveness.

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

`get_report_page` supports selected `files`, `modules`, `entities`, `findings`, `diagnostics`, `metrics`, and inspection `prerequisites`. Use optional `family` and `entityId` filters where applicable; keep them unchanged when passing a returned `nextCursor`. The default limit is 50 records, with 1–200 accepted. A null `nextCursor` means that section is exhausted. Detail retrieval does not execute project code or reread source files.

All tool results are bounded to 512 KiB, including JSON escaping and text content; a byte budget may reduce the record count. Exceptionally large records explicitly direct you to `section: "raw"`. Raw pages contain `rawChunk` strings: concatenate them in cursor order to reconstruct the complete original report JSON without loss. A partial page does not mean analysis is incomplete; use family statuses to assess analysis completeness.

Reports live in private server temporary storage for 60 minutes, with at most 10 published reports and 512 MiB of published data. A replacement can use up to another 512 MiB while it is staged. Old reports can be evicted after successful publication, and restarting the server removes access. Expired, evicted, or unknown IDs return explicit errors. Storage and page retrieval do not write into the analyzed repository.

## Build and verify

```sh
mvn -B -f java-worker/pom.xml package
dotnet test CodeQualityChecker.sln
./scripts/package-macos.sh
```

The package build requires .NET SDK 11.0.100, Maven, and the pinned JBR JDK described in [distribution](docs/distribution.md). Package generation runs a real MCP smoke test with host Java/.NET runtime discovery disabled. Package artifacts remain under ignored `dist/`.

See [ruleset and configuration](docs/ruleset.md), [architecture and extension contract](docs/architecture.md), [evidence and execution](docs/evidence.md), and [third-party attribution](THIRD_PARTY.md).

## Evidence-specific assessments (ruleset 2)

Evidence-aware production entities retain their combined complexity/CRAP score and expose a separate `staticComplexity` assessment. The static score deducts once per complexity finding and requires complete syntax, without requiring coverage or call resolution. A numeric static score never establishes a combined production score. Entity pages expose `blockers` for the primary score and status, score, finding IDs and blockers for `staticComplexity`; summaries include separate static score distributions.

Java schema-2 facts identify symbols by Maven module and main/test source set. Explicit execution collects evaluated module contexts, preserves dependency order, and caches hashed resolution artifacts under `.cqc/java-contexts.json`. Analysis-only calls to the scoped tools can reuse verified matching contexts; without them semantic-dependent scores remain incomplete while valid syntax assessments remain available. Different dependency versions in unrelated modules are permitted. Legacy classpath and coverage evidence require refresh; server and worker must be upgraded together. Builds can still leave unsupported constructs or unresolved generated methods.
