# ADR 0008: Management CLI with doctor diagnostics

- Status: Accepted

## Context

Operators need a way to validate configuration, register/rotate Personal Access Tokens, check repository/service health, and control the Windows Service, without writing ad-hoc scripts or calling the MCP endpoint directly.

## Decision

`githubie.exe` (`Githubie.Cli`) provides `help`, `version`, `logs`, `config check|show`, `repo list|status`, `auth test|set|delete`, `mcp status|tools|test`, `doctor`, and service lifecycle commands (`start|stop|restart|status`, `service install|uninstall|status`). Command dispatch is a pure switch expression over `string[]` in `CliApplication.RunAsync(args, output, error, cancellationToken)`, taking `TextWriter` parameters rather than writing to `Console` directly, for testability. `doctor` builds the same `GithubieCompositionRoot` the Server uses and reports `[OK]`/`[NG]` per Repository (token presence, Git status) plus Configuration, Git availability, and Service Composition.

## Alternatives

- A CLI argument-parsing library (e.g. System.CommandLine): rejected for Phase 1; the command surface is small and stable enough that a switch expression is simpler to read and test than adding a dependency.
- Separate diagnostic tool from the management CLI: rejected to keep operational surface small; `doctor` reuses the same Composition Root the Server uses, so it exercises the real startup path.

## Impact

Adding a CLI command requires one pattern arm in `CliApplication.RunAsync` and, ideally, a corresponding `Githubie.Cli.Tests` case.

## Security conditions

- `auth set` reads the Personal Access Token via masked console input (`Console.ReadKey(intercept: true)`), never via a command-line argument.
- CLI commands that touch GitHub (`auth test`, `mcp *`) never print the token itself.

## Operational conditions

Console output encoding is forced to UTF-8 at CLI startup; omitting this caused mojibake in Japanese help text on real hardware during verification.

## Implementation, tests, and documentation

`Githubie.Cli.CliApplication`, `Program.cs`. Command reference in COMMANDS.md. Verified live: `help`, `version`, `config check|show`, `repo status`, `mcp status|tools` all executed against a real published binary and a real GitHub-hosted clone during Phase 1 real-machine verification; unit tests cover `help`, `version`, unknown command, and config check/show against temp files.
