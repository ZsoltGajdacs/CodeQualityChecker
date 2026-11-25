---
name: check-project
description: Check code quality across the entire Java, C#, TypeScript, or React project with CQC.
---

# Check project quality

Resolve the current project's absolute root from the active workspace or working directory. In a Git checkout, use its repository root. This skill needs no arguments and always selects the project scope.

Call the plugin's CQC MCP tool `check_code_quality` using this request, with the resolved root as `projectRoot`. Client prefixes on MCP tool names can vary.

```json
{
  "request": {
    "projectRoot": "/absolute/path/to/project",
    "execute": false
  }
}
```

Use `execute: true` only when the user explicitly requests execution and authorizes project-controlled restores, builds, and tests. Honor any requested `configPath` and `timeoutSeconds` accepted by the tool. If the server is unavailable, report the connection failure. If the server is connected but the tool is missing, report that the configured server may be out of date.

Read [the shared review guidance](../review-report.md) relative to this skill's directory for report pagination and interpretation. Present the results without editing files unless the user requested improvements.
