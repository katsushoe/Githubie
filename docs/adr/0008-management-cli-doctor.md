# ADR 0008: Management CLI with doctor diagnostics

- Status: Accepted

## Context

Operators need a way to validate configuration, register/rotate Personal Access Tokens, check repository/service health, and control the Windows Service, without writing ad-hoc scripts or calling the MCP endpoint directly.

## Decision

`githubie.exe` (`Githubie.Cli`) provides `help`, `version`, `logs`, `config check|show`, `repo list|status`, `auth test|set|delete`, `mcp status|tools|test`, `doctor`, and service lifecycle commands (`start|stop|restart|status`, `service install|uninstall|status`). Command dispatch is a pure switch expression over `string[]` in `CliApplication.RunAsync(args, output, error, cancellationToken)`, taking `TextWriter` parameters rather than writing to `Console` directly, for testability.

The Server atomically writes `<install-root>\data\service-state.json` with `initializing` before database initialization and `ready` only after the MCP listener starts. A startup failure writes `failed`. The state includes the process ID, process name, and recorded start time. `doctor` verifies the live process by ID and name without requiring privileged process-start-time access. It waits up to 30 seconds for `ready`, then builds a read-only diagnostic composition without schema initialization or migration. It reports `[OK]`/`[NG]` per Repository (token presence, Git status) plus Configuration, Git availability, Service Readiness, and Service Composition.

## Alternatives

- A CLI argument-parsing library (e.g. System.CommandLine): rejected for Phase 1; the command surface is small and stable enough that a switch expression is simpler to read and test than adding a dependency.
- Separate diagnostic tool from the management CLI: rejected to keep operational surface small; `doctor` reuses the same Composition Root the Server uses, so it exercises the real startup path.
- Read readiness from SQLite: rejected because database creation or locking can prevent the diagnostic process from reading the state it needs in order to decide whether to wait.

## Impact

Adding a CLI command requires one pattern arm in `CliApplication.RunAsync` and, ideally, a corresponding `Githubie.Cli.Tests` case. Operators can distinguish service initialization from database corruption or permission failure, and `doctor` no longer mutates the repository database.

## Security conditions

- `auth set` reads the Personal Access Token via masked console input (`Console.ReadKey(intercept: true)`), never via a command-line argument.
- CLI commands that touch GitHub (`auth test`, `mcp *`) never print the token itself.

## Operational conditions

Console output encoding is forced to UTF-8 at CLI startup; omitting this caused mojibake in Japanese help text on real hardware during verification.

The readiness wait is bounded at 30 seconds. Missing, stale, failed, malformed, or inaccessible state returns `[NG]` and a nonzero exit code. The state file contains no credentials or repository configuration.

## Implementation, tests, and documentation

`Githubie.Cli.CliApplication`, `Program.cs`, and `ServiceReadinessStore`. Command reference in COMMANDS.md. Verified live: `help`, `version`, `config check|show`, `repo status`, `mcp status|tools` all executed against a real published binary and a real GitHub-hosted clone during Phase 1 real-machine verification; unit tests cover CLI dispatch and readiness transitions, failure, and timeout behavior.
