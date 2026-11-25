
<!-- BACKLOG.MD GUIDELINES START -->
<!-- backlog.md-instructions-version: 1.55.3 -->
<CRITICAL_INSTRUCTION>

## Backlog.md Workflow

This project uses Backlog.md for task and project management.

**At the beginning of each conversation in this project, run `backlog instructions overview` before answering or taking action. Re-read it only if you have not read it yet in the current conversation.**

Use the overview to decide whether to search, read, create, or update Backlog tasks.

Before task lifecycle actions, read the matching detailed guide:
- `backlog instructions task-creation` before creating or splitting tasks
- `backlog instructions task-execution` before planning, changing status or assignee, adding a plan or implementation notes, or implementing task work
- `backlog instructions task-finalization` before checking acceptance criteria, writing final summaries, or moving tasks to terminal statuses

Use `backlog <command> --help` before running unfamiliar commands. Help shows options, fields, and examples.

Do not edit Backlog task, draft, document, decision, or milestone markdown files directly. Use the `backlog` CLI so metadata, relationships, and history stay consistent.

</CRITICAL_INSTRUCTION>
<!-- BACKLOG.MD GUIDELINES END -->

## Agent skills

When changing tooling, review the relevant agent skills against the updated tool behavior, inputs, outputs, and workflows. Update any affected skill in the same change so its guidance and examples stay accurate. The Claude Code and Codex plugins share the canonical skills and reporting guidance in [`plugins/skills/`](plugins/skills/).

## Commit messages and releases

Use Conventional Commits for new commits. Write the subject as `type(scope): summary` with an optional scope and an imperative summary. Keep each commit focused. Add a body when the reason for the change or the verification needs explanation.

- Use `fix: correct review scoring` for a patch and `feat: add a language analyzer` for a minor release.
- Use `!` before the colon, as in `feat(mcp)!: change the report schema`, or add a `BREAKING CHANGE: <description>` footer for a major release. Describe the migration in the commit body when consumers need to take action.
- Use the type that describes maintenance work, such as `docs`, `ci`, `chore`, `refactor`, or `test`. A push with no minor or major signal gets a patch release.

For a squash merge, put the intended type and any breaking-change marker in the commit that lands on `master`. Use a Conventional Commit PR title when GitHub uses it as the squash subject. The release workflow starts at `v0.3.0` and then applies the highest bump among commits since the previous tag. It creates the tag and release automatically.

## CodeScene

After changing code, run `code_health_review` on the changed files. If Code Health drops, fix the issues, run the review again, and use `code_health_score` to confirm the result. Before finishing, run `pre_commit_code_health_safeguard` and only stop when it passes.
