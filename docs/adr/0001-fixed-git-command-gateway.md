# ADR 0001: Fixed Git command gateway

- Status: Accepted

## Context

Githubie must support repository status, fetch, fast-forward-only pull, and policy-controlled push without exposing arbitrary shell or Git argument execution to an MCP client.

## Decision

Expose one typed method per allowed Git operation (`IGitCommandClient`). Resolve repositories only by configured Repository ID, validate LocalRoot and Remote URL before every operation, and construct arguments internally with `ProcessStartInfo.ArgumentList`. Use `--` before configured remote and branch operands. Repository status reads configured remote-tracking refs with fixed `rev-parse` arguments and calculates local HEAD divergence from the configured develop branch with fixed `rev-list --left-right --count`; it performs no implicit network fetch. Environment variables are sanitized (`GitEnvironmentSanitizer`) before every invocation, disabling terminal prompts and forcing stable `LC_ALL=C` diagnostics.

## Alternatives

- Generic `run_git(args)`: rejected because it exposes unbounded Git behavior and option injection.
- Shell command strings: rejected because quoting, shell expansion, and command chaining expand the trust boundary.
- LibGit2Sharp: not selected because the specification requires system Git and `GIT_ASKPASS` integration for Personal Access Token authentication.

## Impact

Adding a Git operation requires a new typed interface method and explicit implementation. Commands cannot prompt interactively. Status remote HEAD and ahead/behind values reflect the most recent local remote-tracking refs and therefore become current after fetch, pull, or push.

## Security conditions

- Validate Allowlist, LocalRoot, `.git`, reparse points, and Remote URL before network operations.
- Never accept executable names, command strings, or arbitrary argument arrays from MCP input.
- Reject direct push to protected branches and dirty-tree push when configured.
- Do not place Personal Access Tokens in arguments, environment values that persist, output, or logs.
- Pass only the validated configured LocalRoot as process-local `safe.directory`.

## Operational conditions

The infrastructure host supplies a fixed timeout (30s local, 120s network). Timeout or cancellation terminates the entire process tree.

## Implementation, tests, and documentation

Application layer (`Githubie.Application.Git`) owns policy orchestration and structured results. Infrastructure layer (`Githubie.Infrastructure.Git`) owns process execution. Verified against a real GitHub-hosted clone during Phase 1 real-machine verification (repository_status, fetch/pull path).
