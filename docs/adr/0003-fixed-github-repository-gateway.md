# ADR 0003: Fixed GitHub repository gateway

- Status: Accepted

## Context

MCP clients must be able to read and change GitHub Pull Requests, Branches, and Tags without ever choosing which GitHub Owner/Repo they talk to, and without being able to override the Pull Request route or Tag target branch.

## Decision

`IGitHubRepositoryGateway` accepts only a Githubie-internal Repository ID. It resolves `github_owner` / `github_repo` from `RepositoryAllowlist`, and calls `IGitHubApiClient` with those resolved values plus the Repository ID (needed for Personal Access Token lookup). Pull Request creation always uses the configured `develop_branch` as source and `main_branch` as destination; the caller supplies only title/description/draft. Pull Request merge re-fetches the PR from GitHub and validates `state == open` and the source/destination route against `RepositoryPolicy` before calling the merge endpoint. Tag creation resolves the current HEAD of `tag_target_branch` via the API and validates the tag name against `tag_pattern` before calling the two-step Git Data API sequence (ADR 0010).

## Alternatives

- Accepting `owner`/`repo` directly from the MCP client: rejected because it allows access to any public or token-visible repository, defeating the Allowlist.
- Allowing arbitrary source/destination for Pull Request creation: rejected because it allows bypassing the `develop → main` release flow.

## Impact

Adding a new Repository requires only a `githubie.json` entry; no code change. Every Pull Request created by Githubie targets the same fixed route, matching the standard Release Flow.

## Security conditions

- `github_owner` / `github_repo` are never accepted as tool parameters.
- Merge validates the *actual* PR state fetched from GitHub, not a client-supplied claim.

## Operational conditions

None beyond standard Repository Allowlist configuration.

## Implementation, tests, and documentation

`Githubie.Application.GitHub.GitHubRepositoryGateway`. Verified against a live GitHub repository during Phase 1 real-machine verification for the read path (`repository_not_allowed` for unregistered IDs); write paths require a live Personal Access Token to fully exercise.
