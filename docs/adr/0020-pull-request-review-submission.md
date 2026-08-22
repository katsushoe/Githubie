# ADR 0020: Pull-request review submission

## Status

Accepted

## Context

GitHub represents pull-request approval and change requests as submitted reviews. The create-review REST endpoint accepts the `APPROVE` and `REQUEST_CHANGES` events. A change request requires explanatory review text, while an approval may omit it.

## Decision

Githubie exposes `github_pr_review_approve` and `github_pr_review_request_changes` as destructive MCP tools. Both resolve the repository allowlist entry and verify that the pull request is open before submitting a review. Change requests require a non-empty body; both operations reject bodies longer than 65,536 characters.

The returned structured result includes the review ID, body, author, GitHub state, submission time, reviewed commit SHA, and URL. Review contents are not written to audit logs.

## Consequences

Callers can express formal review decisions separately from general pull-request conversation comments. GitHub still enforces repository permissions and rules such as prohibiting self-approval.
