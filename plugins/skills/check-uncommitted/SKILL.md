---
name: check-uncommitted
description: Check code quality in staged, unstaged, and untracked source changes with CQC.
---

# Check uncommitted code quality

Resolve the current Git repository's absolute root from the active workspace or working directory. This skill needs no arguments and always selects uncommitted changes.

Call the plugin's CQC MCP tool `check_uncommitted_changes_quality` using this request, with the resolved root as `projectRoot`. Client prefixes on MCP tool names can vary.

```json
{
  "request": {
    "projectRoot": "/absolute/path/to/repository",
    "execute": false
  }
}
```

CQC selects staged, unstaged, and untracked source, applies normal exclusions, and scores constructs touched in the working-tree diff against `HEAD`. New files and rename destinations are reviewed in full. Report zero selected files as an empty uncommitted scope. If Git scope fails, report the failure. Keep the uncommitted scope in either case.

Use `execute: true` only when the user explicitly requests execution and authorizes project-controlled restores, builds, and tests. Honor any requested `configPath` and `timeoutSeconds` accepted by the tool. If the server is unavailable, report the connection failure. If the server is connected but the tool is missing, report that the configured server may be out of date.

Read [the shared review guidance](../review-report.md) relative to this skill's directory for report pagination and interpretation. Present the results without editing files unless the user requested improvements.
