# ADR 0007: Structured audit log with framework noise filtering

- Status: Accepted

## Context

Operators need a durable, greppable record of every Tool invocation (who/what/when/result) without secrets, but the underlying ASP.NET Core host also emits verbose per-request diagnostic logging (routing, Kestrel, MCP protocol internals) at `Information` level, which drowns out the audit signal in the same log file.

## Decision

Wrap `IGitGateway` and `IGitHubRepositoryGateway` in audit decorators (`AuditedGitGateway`, `AuditedGitHubRepositoryGateway`) that measure duration and write one structured `GithubieAuditEvent` per call via `ILogger`, regardless of success or failure. All log output — audit events and everything else — goes to `DailyFileLoggerProvider`, writing `logs\githubie-yyyyMMdd.log`. To keep this file audit-focused, `Program.cs` filters `Microsoft`, `System`, and `ModelContextProtocol` logger categories to `Warning` and above, so only Githubie's own `Information`-level audit lines (plus any framework warnings/errors) reach the file.

## Alternatives

- Separate audit-only log file/sink: rejected for Phase 1 to keep operational surface small; category filtering achieves the same practical outcome with one file.
- No filtering (log everything): rejected after real-machine verification showed the unfiltered file interleaving audit lines with dozens of routing/hosting diagnostic lines per request, making the audit trail hard to read.

## Impact

Adding a new logger category that should appear in the audit file requires either using the `Githubie` namespace or adding an explicit filter override.

## Security conditions

- Recorded fields: `client`, `tool`, `repository`, `branch`, `pull_request_number`, `tag`, `result`, `duration_ms`, `error_code`.
- Never recorded: Personal Access Token, Authorization header, password, file contents, or any other secret.

## Operational conditions

Log files rotate daily by UTC date; no automatic retention/deletion policy exists yet (documented as an open item in OPERATIONS.md).

## Implementation, tests, and documentation

`Githubie.Server.GithubieAudit`, `DailyFileLoggerProvider`, filter configuration in `Program.cs`. Verified live: a real `tools/call` produced exactly one clean audit line in the log file with no framework noise, after the filtering fix found during real-machine verification.
