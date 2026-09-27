# ADR 0030: Per-repository local commit author

- Status: Accepted

## Context

`github_repository_commit` runs in the Windows service account. A repository without local `user.name` and `user.email` can fail with `Author identity unknown` because LocalSystem has no suitable global Git configuration. The service account is not the person who registered the repository.

## Decision

Store optional `commit_author_name` and `commit_author_email` together in each repository registration. Registration accepts an explicit pair or copies a valid pair from the repository-local Git configuration. Approved repository updates can set the pair for an existing registration. A legacy registration without stored identity reads only the repository-local pair at commit time. Before staging files, reject a commit that has neither pair with `author_identity_missing` and a configuration recommendation. Pass the resolved identity to Git with per-invocation `-c user.name` and `-c user.email` arguments.

## Alternatives

- Use the service account's global Git configuration: rejected because it depends on LocalSystem setup and mixes identities across repositories.
- Write `user.name` and `user.email` into each repository's `.git/config`: rejected because Githubie's registration database is the source of truth for service behavior and repository registration should not mutate local Git configuration.

## Impact

Existing registrations with local Git identity continue to commit. Registrations with neither stored nor local identity receive a typed error and can set the pair through `github_repository_update`. The normal CLI `mcp call` path exposes the same tool parameters and result as MCP clients.

## Security conditions

Names and email addresses are validated as a pair, bounded in length, and reject control characters. Git receives each value as a separate argument rather than through shell command construction. Author identity is shown in the desktop approval for registration or update and is not written to audit records as a token or credential.

## Operational conditions

Repository registration and update persist the pair to the SQLite repository record and update the live allowlist without a service restart. The historical JSON import accepts the optional pair. Operators must set both values together for an existing registration.

## Implementation, tests, and documentation

Gateway tests cover stored identity and a missing identity before staging. Registration and update tests cover storage. Git command and integration tests verify explicit arguments and a successful commit in a repository without local identity. `CONFIG` and `COMMANDS` describe the inputs and the failure response.
