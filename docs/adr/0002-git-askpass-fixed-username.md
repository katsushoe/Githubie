# ADR 0002: GIT_ASKPASS helper with a fixed username convention

- Status: Accepted

## Context

Git over HTTPS prompts for a username and a password when no credential helper answers. Githubie must answer these prompts at execution time only, without ever placing the Personal Access Token in a command line argument, a persisted environment variable, or the Remote URL. Unlike Bitbucket, GitHub's Personal Access Token authentication does not depend on a per-account username.

## Decision

A dedicated executable (`Githubie.AskPass.exe`) is set as `GIT_ASKPASS` (with `GIT_ASKPASS_REQUIRE=force`) only for the duration of network Git operations. The parent process passes only a non-secret Repository ID via `GITHUBIE_ASKPASS_REPOSITORY`. `GitAskPassResponder` answers "Username" prompts with a fixed literal (`x-access-token`) and "Password" prompts by reading the Personal Access Token from `IApiTokenStore` for the given Repository ID. No per-repository username configuration exists.

## Alternatives

- Per-repository configured username (Buckettie's Bitbucket approach): rejected because GitHub Personal Access Token authentication does not require a real username, and adding a configuration field would be unused complexity.
- Embedding the token in the Remote URL: rejected because it persists the secret in Git configuration and shell history.
- Windows Credential Manager generic credential prompt: rejected because it is interactive and unsuitable for a headless MCP Server / Windows Service.

## Impact

`githubie.json` does not need a `github_username` field, simplifying the configuration schema relative to Buckettie's `bitbucket_username`.

## Security conditions

- The AskPass process reads the token only when asked and only for the Repository ID it was given; it never receives the token via environment variable.
- The token is zeroed from memory immediately after being written to stdout for Git to consume.
- `GitEnvironmentSanitizer` strips any inherited `GIT_ASKPASS` / `SSH_ASKPASS` values before Githubie sets its own.

## Operational conditions

`Githubie.AskPass.exe` must be published to the same `bin` directory as `Githubie.Server.exe`; its path is resolved at Composition Root time and passed to `GitCommandClient`.

## Implementation, tests, and documentation

`Githubie.Application.Git.GitAskPassProtocol` / `GitAskPassResponder`; `Githubie.AskPass.Program`. Verified indirectly during real-machine verification (local-only Git operations); full HTTPS push/pull with a live Personal Access Token requires a follow-up manual test.
