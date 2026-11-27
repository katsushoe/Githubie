# 0036: Configurable Pull Request Routes

## Status

Accepted

## Context

Githubie previously created pull requests only from each repository's configured develop branch to its main branch. Repositories need additional controlled routes without bypassing GitHub permissions or branch protections.

## Decision

- Store an allowlist of `{ source_branch, destination_branch }` pairs in each repository's settings.
- Existing configuration without this field receives the legacy develop-to-main route.
- Read and update routes through Repository Settings. Updates use the existing interactive approval, audit, persistence, and live-allowlist path.
- `github_pr_create` accepts an optional source and destination together. Omitting both selects the legacy route; specifying only one or a route outside the allowlist is rejected before calling GitHub.
- Pull request merge validation uses the same route allowlist.
- An empty route list disables pull request creation and merge for all routes.

## Consequences

Repository settings remain backward compatible. Operators can scope routes per repository and disable all routes. The PR API still runs with the configured credential and remains subject to GitHub permissions and branch protection.
