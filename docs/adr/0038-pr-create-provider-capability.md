# ADR 0038: Explicit Pull Request Creation Provider Capability

## Status

Accepted

## Context

Moyai delegates pull request creation only when a successful `github_provider_capabilities` response includes boolean `data.pr_create: true`. Githubie already registers `github_pr_create`, validates the repository-specific pull request route, and requires the Project-bound scope `provider.githubie.pull_request.write`, but the capability response does not explicitly state that this operation is supported.

## Decision

- Add `pr_create` to `GitHubProviderCapabilities`. Githubie returns `true` in a successful capability response because `github_pr_create` is implemented. The model default is `false` so callers that omit the field do not imply support.
- Capability means the provider implements the operation; it does not promise that a particular source/destination pair is currently allowed or that GitHub will accept it.
- Keep the existing `github_pr_create` input and route compatibility. It accepts `repository`, `title`, nullable `description`, and `draft`; `source_branch` and `destination_branch` remain optional together for the legacy configured route. If supplied, both must be present and the pair must be in the repository's allowlist before any GitHub request is sent.
- Keep the exact Project-bound assertion scope `provider.githubie.pull_request.write`, the audit path, normal GitHub authorization and branch protections, and existing typed result/error mapping.
- Do not retry pull-request creation automatically. A timeout or network error can leave the remote outcome uncertain; the caller must stop and reconcile with a read operation before any explicit retry. An unrecognized success response is returned as a typed `github_api_error`.

## Alternatives Considered

- Reuse `repository_create` or `branch_create`: rejected because those capabilities describe different operations.
- Add a new PR creation tool or change the existing argument schema to require explicit routes: rejected because the existing tool already performs the operation and optional route inputs preserve the documented legacy behavior.
- Treat `pr_create: true` as proof that any route can be created: rejected because route allowlists, GitHub permissions, and branch protections remain enforced independently.

## Impact

Moyai can distinguish a provider that supports PR creation from one whose tool list happens to contain the tool without an explicit capability declaration. Existing consumers and repositories keep their current route behavior. The capability is not an authorization grant and does not bypass repository settings or GitHub policy.

## Security and Operations

Every call remains repository-allowlisted, audited, and bound to `provider.githubie.pull_request.write` in Moyai integration mode. Disallowed routes fail before the GitHub API call. Githubie does not automatically replay a create request after timeout or network failure because the remote side effect may already have occurred. Operators should inspect the pull request list before deciding whether to retry.

## Implementation, Tests, and User Documentation

The capability response and its serialized `pr_create` field are covered by Server contract tests. The registered tool schema and exact assertion scope are tied together by a Server test. Application tests verify that disallowed routes do not reach the API and allowed routes forward all explicit create fields. Infrastructure tests verify request JSON, mapped success fields, typed timeout failure, and one-attempt behavior. This ADR is the canonical design record; operational API behavior remains described in the existing pull-request route ADR and tool documentation.
