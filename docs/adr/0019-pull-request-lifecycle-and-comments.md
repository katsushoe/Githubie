# ADR 0019: Pull-request lifecycle and conversation comments

## Status

Accepted

## Context

GitHub does not expose a REST operation that deletes a pull request. Its pull-request update endpoint changes the `state` between `open` and `closed`. Pull-request conversation comments are represented by the issue-comments API because every pull request is also an issue. Review submissions and line-specific review comments use separate APIs and permission contracts.

## Decision

Githubie exposes idempotent `github_pr_close` and `github_pr_reopen` tools and rejects state changes for merged pull requests. It also exposes `github_pr_comment_list` and `github_pr_comment_create` for pull-request conversation comments. Before using an issue-comments endpoint, Githubie resolves the pull-request number through the pulls endpoint so an ordinary issue number is not accepted accidentally.

Comment creation rejects empty bodies and bodies longer than 65,536 characters. State changes and comment creation are destructive MCP tools and remain subject to client approval; comment listing is read-only.

Review approval, change requests, and line-specific comments are intentionally outside this contract and may be added later with dedicated inputs and permissions.

## Consequences

Callers can manage the normal pull-request lifecycle without implying unsupported deletion semantics. Conversation comments use GitHub's canonical endpoint, while review workflows remain explicit rather than being conflated with general discussion.
