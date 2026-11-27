# ADR 0043: Conditional PR merge Provider contract

## Status

Accepted for candidate 2.1.1.0 (2026-10-27). The earlier read/merge contract was accepted by Moyai in message `197ea941df324e41adce1b4d8b49f7e1`. Local implementation verification passed 826 tests. Candidate packaging, deployment and isolated real PR/Consumer acceptance are separate milestones whose outcomes must be recorded individually. Implements CR-2026-10-21-githubie-pr-merge-provider. No production PR14 operation is authorized.

## Context

The existing merge tool has no ordinary `pr_get`/`pr_merge` capability, expected source SHA, draft/source SHA output or complete unknown-result semantics. A successful PUT followed by a failed GET cannot prove that a merge failed. The original CR predates the Windows owner policy accepted in ADR0042.

## Decision

Add `pr_get` and `pr_merge` boolean capabilities, actual `head.sha` as `source_commit_sha`, nullable `draft`, and the optional schema argument `expected_source_commit_sha`. Owner and legacy Moyai connections require that argument at application dispatch; legacy standalone callers retain omission compatibility. A supplied value must be a full 40-hex SHA and match the authenticated PR read. Forward its normalized value as GitHub PUT `sha`, which checks the head at the merge gate. This pins source only, not destination, and does not bypass GitHub branch rules, checks, review or method restrictions.

Validate positive Int32 PR number, supported merge enum, null/nonblank message, registered Repository route and PR state before PUT. A null strategy uses the registered Repository's `merge_method`; an explicit merge, squash or rebase overrides that default. Draft and unproven owner-mode draft status fail closed. An already merged PR with actual source/merge SHA returns its observed snapshot with no PUT; this does not attribute the merge to a previous lost request.

Normal success requires a valid PUT result and matching subsequent merged PR state/merge SHA. Lost/malformed PUT response, network failure, cancellation, server uncertainty or failed/unproven follow-up read returns `pr_merge_result_unknown`, `outcome=unknown`, `retryable=false`, `suggested_action=check_status`. Perform a separate authenticated GET to reconcile. Never automatically repeat the merge. Preserve known GitHub refusals as refusals rather than treating every error as unknown.

Under `windows_owner`, use the existing schema1/DPAPI/owner+generation contract and Windows SID audit; no signature, Issuer or Project grant is required. The 58 operation names are unchanged and the ordinary capability object grows from20 to22 booleans. In legacy signed mode, separate `github_pr_merge` into `provider.githubie.pull_request.merge`, retaining `github_pr_get:[repository.read]` and the existing direct-connection policy. This does not enable or edit any installed trust/scope setting.

## Alternatives

- Consumer GET-only SHA checks have a GET-to-PUT race; use GitHub's conditional SHA too.
- Repeating an ambiguous merge could act on changed state; reconcile through GET instead.
- Requiring a new argument for every historical standalone call breaks existing native use; keep its schema optional while requiring it for the owner/Moyai contract.
- Replacing the synchronous endpoint with a new asynchronous/merge-queue API changes the accepted API and state model; defer that separate feature. This contract supports the existing synchronous merge operation.

## Consequences and operation

MCP and `githubie mcp call github_pr_merge <JSON>` use the same dispatch and error contract. No CLI-side strategy substitution or retry is added. Existing positional C# callers and PR fields retain compatibility through optional trailing fields. GitHub authentication/Repository policy, audit correlation and redaction remain in force. The prior2.1.0.0 package and manifests remain frozen, with their source pins backed up before these edits.

Meaningful tests cover preflight/no-PUT failures, already-merged/no-PUT, exact payload/message, one-PUT dispatch, upstream409, malformed/failed follow-up reads, transport loss, dedicated legacy scope, owner capability and CLI forwarding/failure. Public English/Japanese command references point to the [merge contract](../specifications/pr-merge-provider.md). Build, tests, local publish/package, Consumer interoperability, Windows deployment and release must be reported separately.

## Upstream source

[GitHub REST merge documentation](https://docs.github.com/en/rest/pulls/pulls#merge-a-pull-request) specifies `sha` and409 for a mismatching head. `mergeable=true` is not proof that every required check/review/branch rule is satisfied.
