# Local Claude Code and Codex plugins

The `cqc` plugins (version `0.2.0`) connect to a local macOS Apple Silicon CQC installation and provide the shared `cqc-review` skill. Inspection/check tools return schema-v2 summaries; `get_report_page` retrieves details without repeating analysis. This is a clean replacement of the previous response format. The installer scripts build that installation by default. Plugin packages contain configuration and instructions; the separate MCP directory contains the server and runtimes. Keep the complete MCP directory in place.

## Generate

From this repository, with Python 3.9 or later:

```sh
python3 scripts/package-plugins.py \
  --cqc-executable /absolute/path/to/cqc-osx-arm64/cqc
```

The default output is `dist/cqc-plugins/`, with independent `claude/` and `codex/` local marketplace directories. Use `--output dist/cqc-plugins-next` for another generation. Output must be a new or empty directory strictly inside this repository's `dist/`; existing nonempty directories are preserved. Symlinked input/output paths are rejected; pass the real installation path. Executable existence and permission are checked during generation; use the smoke check below to verify that its runtime files are usable.

Each generated plugin contains its client manifest, `.mcp.json`, and a copy of the canonical `plugins/skills/cqc-review` skill. The MCP command is the executable's absolute path, launched directly without a shell. Paths containing spaces work. Generation does not launch CQC, install software, download files, or edit client settings. Generated JSON contains a machine-specific path; regenerate it for another machine.

## Install locally

Run either installer from any working directory:

```sh
/absolute/path/to/CodeQualityChecker/scripts/install-claude-plugin.sh
/absolute/path/to/CodeQualityChecker/scripts/install-codex-plugin.sh
```

With no arguments, each script runs `scripts/package-macos.sh` to build and smoke-test the self-contained MCP. The default MCP directory is this checkout's `dist/cqc-osx-arm64`; generated `.mcp.json` points directly to its `cqc` executable. A fresh plugin marketplace is generated beneath `dist/` on every run, then the selected client's CQC plugin and local marketplace are refreshed and installed in user settings. Keep that generated directory: the client references it. Other plugins and the other client's settings are preserved.

Building requires macOS Apple Silicon, the pinned .NET SDK, Maven, Python 3.9+, and `JAVA_HOME` set to the pinned JBR SDK, as described in [distribution prerequisites](distribution.md). The target client CLI must be on `PATH`. No toolchains are automatically installed. Build and generation failures stop before changing client settings.

To use an already-built MCP directory and skip the build:

```sh
./scripts/install-codex-plugin.sh --mcp-dir "/absolute/path/to/cqc-osx-arm64"
./scripts/install-claude-plugin.sh --mcp-dir "/absolute/path/to/cqc-osx-arm64"
```

To install an already-generated plugin package without building or generating, pass its root as the sole positional argument, for example `./scripts/install-codex-plugin.sh "dist/my plugin packages"`. This cannot be combined with `--mcp-dir`. Use `--help` for usage. All modes validate the MCP executable and package files before refreshing client settings. If a client operation fails after refresh starts, fix the reported error and rerun the installer.

For isolated verification, set `CLAUDE_CONFIG_DIR` or `CODEX_HOME` to a temporary directory for the respective command. Use `--help` to see script usage.

Replace `/absolute/path/to/CodeQualityChecker` below with this checkout's path. Run the commands for the client you use. They explicitly register and install the local marketplace in your client settings; no public repository or publication is involved.

Claude Code:

```sh
claude plugin marketplace add /absolute/path/to/CodeQualityChecker/dist/cqc-plugins/claude
claude plugin install cqc@cqc-local-claude
claude plugin details cqc@cqc-local-claude
```

Restart Claude Code after installation or update. The skill is available as `/cqc:cqc-review`, and normal skill discovery can select it for relevant review requests. For a temporary session without persistent installation, use `claude --plugin-dir /absolute/path/to/CodeQualityChecker/dist/cqc-plugins/claude/plugins/cqc`.

Codex:

```sh
codex plugin marketplace add /absolute/path/to/CodeQualityChecker/dist/cqc-plugins/codex
codex plugin add cqc@cqc-local-codex
codex plugin list
```

Restart Codex, including the desktop app if used, and open a new chat so plugin skills and MCP tools load. Select the CodeQualityChecker plugin or ask to use its `cqc-review` skill. Local plugin support depends on client version; verification used Codex CLI 0.166.0 and Claude Code 2.1.306. Client formats follow the [Codex packaging guide](https://developers.openai.com/plugins/build/plugins) and [Claude plugin reference](https://code.claude.com/docs/en/plugins-reference).

The skill supports review-only requests and requested fixes with rechecks. Source-only analysis is the default. Project-controlled restores, builds, and tests require user authorization; source-only support remains useful when execution prerequisites are unavailable. Scores are separate CQC scores, not CodeScene Code Health, and missing coverage remains incomplete rather than receiving a perfect score.

## Update, relocate, or remove

Keep the generated marketplace directories available at their registered locations. Rerun an installer to rebuild and refresh its plugin, or use `--mcp-dir` after moving a prebuilt MCP installation. The installer removes only that client's matching CQC plugin/catalog before adding the prepared package, ensuring cached copies pick up the new command and skill. Restart the client afterward. Moving only the plugin directory does not update the absolute CQC command. For manual installation, uninstall/remove the old local marketplace before adding the newly generated output.

Remove Claude's local installation:

```sh
claude plugin uninstall cqc@cqc-local-claude
claude plugin marketplace remove cqc-local-claude
```

Remove Codex's local installation:

```sh
codex plugin remove cqc@cqc-local-codex
codex plugin marketplace remove cqc-local-codex
```

Restart the client after removal. These commands do not remove the separately extracted server. Disable or remove any previous manual `cqc` MCP registration through the client's own MCP settings before enabling the plugin, to avoid duplicate servers/tools. Packaging never changes those registrations for you.

## Verify

```sh
python3 scripts/test-package-plugins.py
python3 scripts/test-install-plugin.py
python3 scripts/smoke-plugins.py dist/cqc-plugins
claude plugin validate --strict dist/cqc-plugins/claude/plugins/cqc
claude plugin validate --strict dist/cqc-plugins/claude
python3 scripts/verify-plugin-clients.py dist/cqc-plugins
```

The packaging tests use a small local executable to verify path handling and output protection. The MCP smoke test starts the real server using both generated configurations, discovers seven tools, and calls all inspection/check tools against copies of existing Java/C# scoring fixtures. It retrieves complete reports through raw pages, checks message sizes, missing-coverage scores and unsupported source handling, and verifies unchanged fixture bytes. It also compares generated skill bytes to their canonical source.

The installer tests exercise the public entrypoints in a disposable checkout with build/client command stand-ins and the real package generator. They verify default build order, the configured MCP path, prebuilt overrides, refresh, and failures before settings changes. Client refresh commands run from a neutral temporary directory. Claude user marketplace declarations are checked in user settings, since its cached marketplace inventory can also contain project registrations.

The client verifier uses temporary `CODEX_HOME` and `CLAUDE_CONFIG_DIR` directories, without copying credentials or calling a model. It checks persistent local installation and component discovery, plus all seven live MCP tools in Codex through its app-server protocol. Claude's inventory check is supplemented by the independent real-MCP smoke check. Run it from a context that permits local server processes: this machine's restricted execution sandbox stalled the bundled .NET server before MCP initialization; the same checks passed outside that sandbox.

For a missing server, restore the original complete extracted installation or regenerate with a valid path. A successful manifest check alone does not prove the server can start. Public-directory warnings about listing assets are separate from strict local plugin validation; public submission is deferred.

## Historical validation evidence for 0.1.0

Verified on 2026-11-09: six packaging tests passed; canonical skill and strict Claude plugin/marketplace validation passed; both generated MCP configurations invoked all six tools successfully. Codex 0.166.0 discovered the enabled skill and six live tools; Claude Code 2.1.306 discovered the skill and reported its MCP server connected. All five touched Python files received Code Health 10.0, and the change safeguard passed. Normal client settings were left unchanged.

The build-enabled installers were additionally run with no arguments against the real pinned toolchain and disposable client settings. Both completed the full packaged MCP build and six-tool smoke test, generated their plugin packages, and installed a cached MCP command pointing to this checkout's `dist/cqc-osx-arm64/cqc`.

All 13 installer tests passed. Real prebuilt installs into both clients also verified that changing MCP directories with spaces updates the cached command. Claude user installation succeeded with an existing project-only CQC catalog while preserving project settings byte-for-byte. The installer helper and its tests received Code Health 10.0; shell wrappers passed ShellCheck.

The independent skill review walked through these scenarios; it did not run model-driven evaluations:

| Request or condition | Expected skill behavior |
| --- | --- |
| Review a diff only | Inspect project, check changed files, report findings without edits |
| Fix a complexity issue | Check selected paths, preserve behavior, apply requested fixes, run authorized tests, recheck |
| Coverage evidence missing | Preserve null scores and incomplete statuses; explain missing evidence |
| MCP unavailable | Report the connection blocker; do not invent results or install/configure software |
| Python-only repository | Report unsupported source; do not claim Java/C# analysis |
| Gradle Java source | Allow source-only analysis while identifying unsupported Gradle execution |

The plugin manifests and skill introduce no server API changes, hooks, or automatic commit gates. Publication, downloads, other operating systems, and server/runtime bundling are outside this version.
