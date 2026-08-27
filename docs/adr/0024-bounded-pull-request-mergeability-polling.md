# ADR 0024: Bounded pull-request mergeability polling

## Status

Accepted

## Context

GitHub computes pull-request mergeability asynchronously. A recent `mergeable: true` response can be followed briefly by a merge API rejection, while `mergeable: null` and `mergeable_state: unknown` mean calculation is incomplete. Treating every rejection as a permanent conflict prevents safe client retries.

## Decision

`github_pr_get` returns the compatible `mergeable` field plus `mergeability_status` and `retry_after_seconds`. Status is one of `calculating_retryable`, `mergeable`, `conflicting`, `blocked`, or `unknown_retryable`. Calculating responses use error code `mergeability_calculating` when merge is requested, matching Buckettie's shared client contract.

Before merge, Githubie reads the pull request at most three times with two seconds between reads. It never retries the merge mutation itself. A temporary merge API rejection triggers one final read and classification. Only confirmed `conflicting` and `blocked` states are non-retryable. Raw GitHub response bodies remain internal.

## Alternatives

Returning every HTTP 405 as a permanent conflict was rejected because GitHub can return it during state propagation. Unbounded polling was rejected because it creates unpredictable latency. Adding a new tool was rejected because the existing get and merge operations can expose one consistent contract.

## Consequences

Existing clients retain all previous fields and receive additive status metadata. Retry-aware clients can wait for the advertised two seconds. The merge call adds at most four seconds of preflight latency and one read after a temporary mutation rejection. Application, Infrastructure, MCP mapping, CLI-through-MCP, and documentation tests cover the shared contract.

## Security and operations

Polling uses only the allowlisted repository and fixed pull-request endpoint. Cancellation propagates through every read and delay. Operators diagnose repeated retryable states using the existing audit log without exposing credentials or raw provider responses.
