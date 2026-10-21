# ADR 0037: Explicit local branch creation provider capability

## Status

Accepted, 2026-10-18.

## Context

Moyai delegates local branch creation only when `github_provider_capabilities` explicitly returns `data.local_branch_create: true`. The existing `branch_create` capability means remote branch creation and must not be used as a substitute. Githubie already exposes `github_local_branch_create(repository, branch, source)` as a local-only operation.

## Decision

Add `local_branch_create` to `GitHubProviderCapabilities`. A Githubie instance that exposes `github_local_branch_create` returns `true`; an unavailable operation uses the model default `false`. This capability is independent of `branch_create`.

The tool continues to require `repository`, `branch`, and `source`, and is bound to exactly `repository.branch.write` by the Provider Assertion policy. It remains allowlist-bound and uses the existing assertion validation and audit path. It only creates a local ref and does not contact a remote or switch the checkout.

Moyai must refuse delegation when the capability is false or absent. It must not fall back to remote branch creation or raw Git.

## Verification

Server tests verify the advertised capability and its JSON field, and tie the registered local tool name, required input schema, and assertion scope together. Existing Application and Infrastructure tests cover local branch/tag/full-SHA sources, missing or invalid sources, dirty and conflicted worktrees, and duplicate destinations.

## Rollout

This source change does not itself identify a released build or an isolated Moyai acceptance endpoint. Those values must be reported only after a versioned build and a disposable, separately configured fixture are prepared. Do not use a regular service endpoint or a shared self-test repository for mutating acceptance.
