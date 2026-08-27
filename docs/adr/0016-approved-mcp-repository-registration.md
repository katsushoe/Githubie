# ADR 0016: Out-of-band approved MCP repository registration

- Status: Accepted

## Context

ADR 0012 intentionally deferred dynamic repository registration. Operators now need a reproducible way to add an existing local GitHub repository without trusting an MCP caller to supply the GitHub owner or repository name.

## Decision

Githubie exposes `github_repository_register`. The caller supplies an internal ID, local root, remote name, and optional branch names. Githubie validates the local Git repository, reads the configured remote with the fixed Git command gateway, derives the GitHub owner and repository from that URL, and requires approval in an active interactive desktop session. The service enumerates active Windows sessions instead of assuming that the active console session is the interactive user, so the prompt also works for an active Remote Desktop session. After approval it atomically persists the repository database and updates the running allowlist.

New entries use safe defaults: direct push to `develop`, pull from `develop` and `main`, protected `main`, tags at `main`, merge commits, and a clean-working-tree requirement.

## Alternatives

- Continue configuration-file-only registration: rejected because it does not provide a reproducible MCP workflow.
- Accept owner and repository from the MCP caller: rejected because it crosses the repository trust boundary.
- Use chat confirmation only: rejected because the requesting agent shares that trust boundary.

## Impact

Registration no longer requires a service restart. Existing repository settings remain unchanged. Token provisioning remains a separate operator action through `githubie auth set`.

## Security conditions

The local root must exist, contain Git metadata, and contain no reparse point. The remote must resolve to `github.com`; owner and repository are derived only from that URL. Repository IDs cannot be replaced. Registration requires out-of-band desktop approval, and secrets are never returned or logged. Only an explicit negative response is treated as denial; a missing or malformed prompt response is a protocol failure.

## Operational conditions

The service identity must be able to update `githubie.json`. Operators should verify `github_repository_status` after registration and provision a repository-scoped token before network operations.

## Implementation, tests, and documentation

The Application layer owns registration validation and orchestration, Infrastructure atomically persists SQLite rows as specified by ADR 0025, and the Server maps stable MCP results. Tests cover success, defaults, duplicates, invalid roots/remotes, non-GitHub remotes, and approval denial. README, COMMANDS, CONFIG, and OPERATIONS describe the workflow.
