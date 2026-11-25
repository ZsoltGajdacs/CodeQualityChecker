# CodeQualityChecker

A local MCP server for deterministic Java, C#, TypeScript, and React code reviews.

## Install on macOS Apple Silicon

Copy this command into your terminal:

```sh
cqc_installer=$(curl -fsSL https://raw.githubusercontent.com/ZsoltGajdacs/CodeQualityChecker/master/install.sh) &&
  /bin/bash -c "$cqc_installer" cqc-install --standalone
```

Choose `1` for Claude Code, `2` for Codex, or `1 2` for both. The installer verifies the latest GitHub release and installs CQC and its two check skills. You need Python 3.9+ and the selected client CLIs on `PATH`. Run the same command to update, then restart the selected clients. [View the installer](install.sh) · [Installation options](docs/plugins.md)

## Use CQC

In Claude Code, type `/cqc:check-` and choose `/cqc:check-uncommitted` for your changes or `/cqc:check-project` for the whole repository. In Codex, type `$cqc:check-` and select the matching skill. Both checks run with no arguments and default to source analysis. TypeScript and React support complexity, duplication, and dependency checks. Supported Java and C# projects can also run builds and tests when you explicitly enable execution. [Evidence and execution](docs/evidence.md)

| MCP tool | Purpose |
| --- | --- |
| `inspect_project` | Detect languages, modules, and prerequisites |
| `check_code_quality` | Review the repository |
| `check_uncommitted_changes_quality` | Review staged, unstaged, and untracked changes |
| `check_test_quality` | Assess test structure and assertions |
| `check_duplication` | Find structural duplication |
| `check_dependencies` | Find dependency cycles and coupling |
| `check_hotspots` | Score change hotspots using local Git history, constructor size, and class length |
| `check_all` | Run all five check families |
| `get_report_page` | Read details from a completed report and filter diagnostic pages by exact code |

Each check-family score starts at 10 and deducts one point per distinct scored finding. Family summaries count scored, incomplete, and not-applicable class and file entities. [Ruleset and configuration](docs/ruleset.md)

## Development

See the [architecture](docs/architecture.md), [build and packaging guide](docs/distribution.md), and [third-party notices](THIRD_PARTY.md).
