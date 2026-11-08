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

## Tools

| Tool | Result |
| --- | --- |
| `inspect_project` | Languages, modules, integrations, and missing build prerequisites |
| `check_code_quality` | Complexity and branch-coverage CRAP |
| `check_test_quality` | Test assertions, branching, size, and helper/fixture contributions |
| `check_duplication` | Structural duplicate groups within each language |
| `check_dependencies` | Semantic type/component cycles, coupling, and optional boundaries |
| `check_all` | All four check families with shared discovery/parsing |

All tools take a nested `request` object:

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

Use `scope: "paths"` with `paths: ["src/Feature.cs", "module/src"]`, or `scope: "changed"` with optional `gitBase: "main"`. Default changed scope includes staged, unstaged, and untracked source files. Checks assess complete selected files against the whole repository, so findings can point to unselected counterpart files.

Source-only analysis never invokes project builds, tests, restores, or MSBuild evaluation. Set `execute: true` explicitly to allow project-controlled Maven/.NET restores, builds, and tests. Execution uses an isolated copy and writes reusable evidence under the original root's `.cqc/` directory. Maven execution uses a project wrapper when available, otherwise host Maven; the project's Java/.NET SDKs are also needed. Initial execution integrations are Maven/JUnit 5 and SDK-style .NET/xUnit with VSTest; Gradle and Microsoft.Testing.Platform execution are outside the MVP.

Optional inputs are `configPath`, `reportPath`, and `timeoutSeconds` (1–3600). `reportPath` reads existing CQC coverage evidence; generated evidence is written to `.cqc/coverage.json`. Raw JaCoCo/OpenCover XML alone cannot establish source-matched provenance.

Results identify schema/ruleset/configuration/source versions, analyzed files, completeness, raw metrics, findings, symbol identities, source ranges, and advice. Scores remain separate by family. An incomplete family does not prevent an independent family from scoring. Recommendations are advisory; a high score does not prove functional correctness or test effectiveness.

## Build and verify

```sh
mvn -B -f java-worker/pom.xml package
dotnet test CodeQualityChecker.sln
./scripts/package-macos.sh
```

The build requires .NET SDK 10.0.403, Maven, and the pinned JBR JDK described in [distribution](docs/distribution.md). Package generation runs a real MCP smoke test with host Java/.NET runtime discovery disabled. Package artifacts remain under ignored `dist/`.

See [ruleset and configuration](docs/ruleset.md), [architecture and extension contract](docs/architecture.md), [evidence and execution](docs/evidence.md), and [third-party attribution](THIRD_PARTY.md).
