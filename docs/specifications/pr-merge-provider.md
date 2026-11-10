# PR read and merge Provider contract

Candidate version2.1.1.0; implementation decision: [ADR0043](../adr/0043-pull-request-merge-provider.md). [日本語](pr-merge-provider.ja.md). This is a candidate contract, not a claim about the installed1.8 runtime or real Consumer acceptance.

## Negotiation and authentication

For the selected registered Repository, require `github_provider_capabilities` Provider result `ok=true`, with strict `data.pr_get=true` and `data.pr_merge=true`. No such capability can be inferred from PR creation, tool presence alone or a historical installed version. Require actual `source_commit_sha` from `github_pr_get` before dispatch.

Owner mode retains `authentication.integration_mode=windows_owner`, `direct_connection=unrestricted`, owner contract1, schema1 discovery and both `X-Githubie-Session`/`X-Githubie-Generation` on every HTTP request. Omit Authorization; exact `github_pr_get`/`github_pr_merge` must be in both owner operations and tools/list. No Issuer/ES256/Project security grant is required. Existing owner coverage has58 operations; candidate2.1.1.0 defines22 ordinary booleans, adding `pr_get`/`pr_merge` to the20 defined in2.1.0.0. The old frozen20-boolean table describes that older candidate only.

Legacy signed mode uses `github_pr_get:[repository.read]`, `github_pr_merge:[provider.githubie.pull_request.merge]`, Provider/audience `githubie`, and existing exact tool/Repository/Project binding. The old shared `provider.githubie.pull_request.write` no longer authorizes merge. Existing standalone/direct policy remains; these code changes do not edit the installed settings or enable a new grant.

## Inputs and output

`github_pr_get(repository, pull_request_number)` reads a positive Int32 PR number. Its existing fields remain; `source_commit_sha` is read from GitHub `head.sha`, `draft` is nullable actual API metadata. Never substitute the test merge SHA or a requested SHA for the actual source SHA. A missing/invalid source SHA cannot satisfy Consumer preflight.

`github_pr_merge` retains `repository`, `pull_request_number`, nullable `merge_strategy`, nullable `message`, adding schema-optional `expected_source_commit_sha`. Owner/Moyai calls require a full40-hex SHA. Native legacy standalone omission is retained for compatibility; callers wanting the conditional guarantee must provide the SHA. The Consumer always requires its approved expected SHA and compares a fresh authenticated PR read before merge dispatch. Int64 Consumers must reject values outside1..2147483647 before calling this Provider.

- `merge`/`squash`/`rebase` are supported; common `repository_default` maps to null. The Provider resolves null to the registered Repository's `merge_method`. Do not translate `fast_forward` to another method; it is unsupported.
- Omitted/null message means default, blank/whitespace-only is rejected, nonblank text is preserved exactly as upstream `commit_message` (extra commit message detail, not complete title replacement).
- A supplied SHA is validated, normalized to lowercase, compared with actual source SHA and sent as upstream PUT `sha`. GitHub rejects changed head at that gate. Destination head is not pinned.

Require a registered Repository and allowed source/destination route. Reject non-open and draft PRs before PUT; required-but-unavailable draft/source evidence fails closed. GitHub is authoritative for merge method permission, conflicts, required checks/reviews and branch rules. `mergeable=true` is not sufficient proof of all requirements.

For already-merged reconciliation, return the actual merged PR snapshot and complete source/merge SHA without another PUT, after Repository route and expected SHA checks. That observation does not prove the request's caller caused the merge. Normal success requires matching PUT confirmation and final GET `state=merged`/merge SHA. Existing fields include PR URL, source/destination, number and merge commit.

## Errors and recovery

Errors retain the existing Provider envelope `ok`, `operation=pr_merge`, `repository`, `data=null`, `error` and correlation/provider detail fields. HTTP/MCP transport success is not business success. JSON or SSE MCP results must be decoded to Provider `ok/error`.

| Code/class | Outcome | Required action |
| --- | --- | --- |
| `pull_request_number_invalid`, `merge_strategy_invalid`, `merge_message_invalid`, `expected_source_commit_required`, `expected_source_commit_invalid` | `not_executed` | Correct the inputs; do not switch strategy or authentication automatically. |
| `expected_source_commit_mismatch` | `not_executed`, common `CONFLICT` | Read current PR/source and obtain a new approval; no automatic resubmission. Covers local comparison and conditional upstream409. |
| `pull_request_draft`, `pull_request_not_open`, `pull_request_route_not_allowed`, `pull_request_blocked`, `pull_request_merge_rejected` | `not_executed`, common `POLICY_REJECTED` | Inspect state/policy/GitHub reason. Preserve protections. |
| authentication/permission failures | `not_executed`, common authentication/permission classification | Configure external credentials/permissions through the normal owner workflow. Owner authentication is not a GitHub token grant. |
| `pr_merge_result_unknown` | `unknown`, common `RESULT_UNKNOWN`, `retryable=false`, `suggested_action=check_status` | A dispatched mutation may have completed. Read the PR independently and reconcile source SHA/state/merge commit; never automatically replay. |

Missing/malformed PUT evidence, network failure/timeout/cancellation/server uncertainty, failed subsequent GET or inconsistent final state/SHA remain unknown. Known GitHub denials are not automatically changed to unknown. A bare403, arbitrary JSON-RPC error or transport disconnect never proves no mutation. Existing owner pre-handler rejection headers and one-time bootstrap metadata rediscovery remain as described in the owner contract; metadata retry is not permission to replay business mutations.

## CLI parity and examples

The CLI generic MCP call uses the same registered Tool, JSON arguments, owner connection, policy/audit and structured errors. Example JSON (synthetic SHA; no real PR request is authorized by this example):

```json
{"repository":"fixture","pull_request_number":1,"merge_strategy":null,"message":null,"expected_source_commit_sha":"1111111111111111111111111111111111111111"}
```

Save to a local file and use `githubie mcp call github_pr_merge --file <path>`, or pass the JSON as the final argument. Reconcile through `githubie mcp call github_pr_get` with the same registered Repository and number. CLI returns failure for Provider `ok=false`, including unknown; no write retry is introduced.

## Validation and rollout

Automated Provider/transport/CLI tests and local candidate artifacts do not establish actual Moyai Consumer interoperability or Windows migration. Pin candidate/config/test hashes and agree a disposable Repository/PR, operation/approval and cleanup before real acceptance. No production PR14, release, normal settings/keys/DB or installed Service change is part of this implementation. Keep previous2.1.0.0 artifacts and baseline source evidence intact.
