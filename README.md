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
| `check_code_quality` | Complexity and branch-coverage CRAP |
| `check_test_quality` | Test assertions, branching, size, and helper/fixture contributions |
| `check_duplication` | Structural duplicate groups within each language |
| `check_dependencies` | Semantic type/component cycles, coupling, and optional boundaries |
| `check_all` | All four check families with shared discovery/parsing |
| `get_report_page` | Bounded detail pages from an immutable completed report |

Inspection and check tools take a nested `request` object:

```json
{
  "request": {
    "projectRoot": "/absolute/path/to/repository",
    "scope": "repository",
    "execute": false,
    "timeoutSeconds": 300
  }
}
```

### Check uncommitted changes

To check only files with uncommitted changes, pass `scope: "changed"` and omit `gitBase` to any inspection/check tool, such as `check_all`:

```json
{
  "request": {
    "projectRoot": "/absolute/path/to/repository",
    "scope": "changed",
    "execute": false
  }
}
```

This selects staged, unstaged, and new untracked source files, respecting normal ignores and generated-file exclusions. Each file is checked once using its current working-tree content, including unstaged edits to a staged file. Deleted files are excluded; a clean repository selects no files. Git must be available and the project root must be inside a Git repository.

Checks assess complete selected files using the whole repository for context, so findings can point to unselected counterpart files. Changed scope limits the reported files; parsing and explicit builds/tests may still use the whole repository.

Use `scope: "paths"` with `paths: ["src/Feature.cs", "module/src"]` for explicit selection. To include changes relative to a commit or branch, use `scope: "changed"` with `gitBase`, such as `"main"`; untracked source files are also included. Omit `gitBase` for an uncommitted-only review. The default scope remains `"repository"`.

Source-only analysis never invokes project builds, tests, restores, or MSBuild evaluation. Set `execute: true` explicitly to allow project-controlled Maven/.NET restores, builds, and tests. Execution uses an isolated copy and writes reusable evidence under the original root's `.cqc/` directory. Maven execution uses a project wrapper when available, otherwise host Maven; the project's Java/.NET SDKs are also needed. Initial execution integrations are Maven/JUnit 5 and SDK-style .NET/xUnit with VSTest; Gradle and Microsoft.Testing.Platform execution are outside the MVP.

Optional inputs are `configPath`, `reportPath`, and `timeoutSeconds` (1–3600). `reportPath` reads existing CQC coverage evidence; generated evidence is written to `.cqc/coverage.json`. Raw JaCoCo/OpenCover XML alone cannot establish source-matched provenance.

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

The build requires .NET SDK 10.0.403, Maven, and the pinned JBR JDK described in [distribution](docs/distribution.md). Package generation runs a real MCP smoke test with host Java/.NET runtime discovery disabled. Package artifacts remain under ignored `dist/`.

See [ruleset and configuration](docs/ruleset.md), [architecture and extension contract](docs/architecture.md), [evidence and execution](docs/evidence.md), and [third-party attribution](THIRD_PARTY.md).

## Evidence-specific assessments (ruleset 2)

Production entities retain their combined complexity/CRAP score and expose a separate `staticComplexity` assessment. The static score deducts once per complexity finding and requires complete syntax, without requiring coverage or call resolution. A numeric static score never establishes a combined production score. Entity pages expose `blockers` for the primary score and status, score, finding IDs and blockers for `staticComplexity`; summaries include separate static score distributions.

Java schema-2 facts identify symbols by Maven module and main/test source set. Explicit execution collects evaluated module contexts, preserves dependency order, and caches hashed resolution artifacts under `.cqc/java-contexts.json`. Source-only analysis can reuse verified matching contexts; without them semantic-dependent scores remain incomplete while valid syntax assessments remain available. Different dependency versions in unrelated modules are permitted. Legacy classpath and coverage evidence require refresh; server and worker must be upgraded together. Builds can still leave unsupported constructs or unresolved generated methods.
