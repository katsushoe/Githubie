# Windows owner acceptance v1

Status (2026-10-26): Provider supplement to [authentication v1](windows-owner-authentication-v1.md), following Moyai's major agreement in message `873bbf9f941a45229ffedf266583b791`. Final detail agreement and real Moyai interoperability are pending. This document describes the existing candidate 2.1.0.0; it changes no runtime behavior or frozen artifact.

## Fixed candidate and evidence

The separately delivered candidate directory is `.local/owner-session-trust-20261025/package` in the Provider checkout. It contains the complete `owner-authentication-contract.json` (58 exact operation names), `manifest.json` (50 source hashes and one deletion, base `b8122669fb6c0edd16cb543bcfb3c1ef8c011b6e`, uncommitted), and `Githubie-2.1.0.0-win-x64.msi`.

| Artifact | SHA-256 |
| --- | --- |
| MSI 2.1.0.0 | `B14A833268AD812F8E867745E5870376A67BD691B60A200FE7F6DC231754B607` |
| Published/fixture Githubie.Server.dll | `B0BF755C389E03C01C5E2D8BCC0EBED5385D67872CF202991E93D988987A715F` |

The adjacent supplemental manifest, delivered separately from the frozen package, pins this document, the operation table, the old acceptance document, the original manifest and all original source hashes. It must not replace the original manifest. Provider evidence is 770 passing tests and 12 isolated fixture checks. These are not Moyai Consumer acceptance. MSI costing/Install/Upgrade/Uninstall and live migration remain unverified.

## Error transport and execution boundary

The source of the owner gate is `HostRuntime.cs`: the user-host middleware calls `UserHostRequestGate.IsAuthorizedAsync`, then checks Authorization, and returns before `next()` on either rejection. `MapMcp` and tool dispatch occur downstream. Gate rejection sets HTTP status and header only: the body is empty, not a JSON error and not a JSON-RPC response. No `Retry-After` is emitted by this gate. If the owner check fails and Authorization is also present, owner rejection takes precedence.

| Condition | Wire response | Execution evidence and Consumer handling |
| --- | --- | --- |
| Missing/wrong session or generation; Host/Origin rejected by owner gate; connection unavailable | HTTP 403, `X-Githubie-Error: user_host_owner_rejected`, empty body | This specific header/status pair from the pinned, validated local Provider means rejected before MCP handling. Stop the request. At most one fresh-discovery retry for bootstrap metadata under the existing schema1 retry rules; no automatic business mutation replay. |
| Valid owner/generation, any Authorization header (assertion, Bearer or empty value still present) | HTTP 403, `X-Githubie-Error: user_host_auth_mode_mismatch`, empty body | Before MCP handling. Remove the incompatible Consumer configuration; no assertion/token/static fallback or automatic mutation replay. |
| Bare 403, unknown/missing error header, redirect, malformed/absent RPC result, timeout/disconnect after send | Transport/RPC failure, not an owner-gate result | Does not prove no dispatch or no side effect. Disable redirects; unknown mutation outcome must be reconciled through fresh read/status/ref/audit evidence before any new operation. |
| Consumer discovery/contract validation fails before it sends | Local Consumer error; no Provider response | Consumer may record not dispatched only if it can prove no request was emitted. No fallback. Preserve stable discovery category separately from HTTP errors. |
| Unknown tool, invalid parameters, protocol failure | MCP JSON-RPC `error` envelope (SDK transport) | Not a `GithubieToolResult`. Do not assume an arbitrary JSON-RPC error proves no mutation. Retain error code and request ID; no automatic mutation replay. |
| Repository busy / commit in progress | Successful MCP transport carrying tool failure: `error.code=repository_busy` / `commit_in_progress`, `common_code=CONFLICT`, `outcome=not_executed`, `retryable=false`, `suggested_action=check_status` | Tool handler was entered; the Git precondition rejected execution. Check status/lock and obtain a new deliberate action; do not call this owner-auth rejection or rediscover for it. |
| Expected HEAD mismatch / branch changed / detached HEAD | Tool failure, `common_code=CONFLICT`, `outcome=not_executed`, `suggested_action=check_status` | Inspect branch/HEAD. No automatic resubmission of the original mutation. |
| Protected branch / branch protection denied | Tool failure, `common_code=POLICY_REJECTED`, `outcome=failed`, `retryable=false`, `suggested_action=inspect_policy` | Handler-level policy result, not HTTP owner rejection. For the isolated protected-push case separately verify local/remote refs unchanged and no publication. The generic `failed` field alone does not prove no partial effect for every operation. |
| Approval denied/timed out/unavailable; other repository policy or GitHub permission failure | Tool failure with the actual operation-specific code; unoverridden fields default to `common_code=PROVIDER_ERROR`, `outcome=failed`, `retryable=false`, `suggested_action=inspect_provider_error` | Do not invent a stronger common classification or not-executed guarantee. Keep Provider code, inspect actual result and reconcile when needed. Owner authentication does not grant GitHub token permissions or bypass approval. |
| Commit result uncertain | Tool failure `code=commit_result_unknown`, `common_code=RESULT_UNKNOWN`, `outcome=unknown`, `retryable=false`, `suggested_action=check_status` | Check branch ref/index/audit; never replay automatically. |
| Push network/remote unavailable/timeout/cancellation/Git failure | Tool failure with original Provider code, `common_code=RESULT_UNKNOWN`, `outcome=unknown`, `retryable=false`, `suggested_action=check_status` | Remote side effect is uncertain. Reconcile remote state; never replay automatically. |
| GitHub API network/timeout (including PR/release writes) | Existing tool codes `network_error` / `timeout`; current mapper defaults may still say `outcome=failed` / `PROVIDER_ERROR` | A dispatched network mutation may have happened despite that default. Consumer must conservatively mark its operation result unknown and reconcile. Do not equate `failed` with not executed or introduce a retry from prose. |

Tool results use snake_case and the envelope `{ "ok": false, "operation": "push", "repository": "fixture", "data": null, "error": { ... } }`. The operation field is the existing handler label, often without `github_`; dispatch and coverage matching use the exact MCP tool name instead. `error` includes string `code`, `message`, `summary`, `common_code`, `outcome`, `suggested_action`, `correlation_id`, boolean `retryable`, nested `provider` (`name`, `code`, `diagnostic`, `exit_code`, `recommendation`) and nullable detail fields. Preserve the structured fields; do not branch on natural language.

An ordinary tools/call result is JSON-RPC `result.structuredContent` containing this envelope, with content text compatibility. The HTTP transport can carry JSON or SSE; parse negotiated transport, not just the HTTP body as a single JSON object. HTTP success and MCP `isError` alone do not establish business success: inspect the Provider envelope's `ok`/`error`. There is no new HTTP busy/503 convention in this candidate.

## Exact operation and ordinary capability mapping

The companion `owner-operation-capabilities.json` and the table below enumerate all 58 `github_` tools in candidate 2.1.0.0. The machine-readable table is checked against both the frozen owner contract and source tool registrations. The ordinary capability keys are the 20 boolean fields of `GitHubProviderCapabilities`; all are true for a registered Repository in this candidate. They express implemented support, not policy approval. Remote resolution is separately `{ "version": 1, "mode": "repository_url" }`.

`none` means this candidate defines no separate ordinary boolean for that operation. It does not mean false, unsupported or authorization granted. Require the exact operation in `owner_authentication.operations`, a corresponding registered tools/list entry, the ordinary boolean where defined, and the operation's existing policy/approval. Never synthesize fields such as `pr_merge`, `push`, or `release_create`. The owner list covers authentication for management tools too; a Repository-bound capability handshake itself requires a registered Repository. A missing Repository produces `repository_not_found`; initial registration is a separate explicit management workflow, not a reason to substitute a different Repository.

<!-- operation-table:start -->
| Exact MCP tool | Ordinary boolean (under data) |
| --- | --- |
| `github_branch_create` | `branch_create` |
| `github_branch_delete` | `branch_delete` |
| `github_branch_get` | none |
| `github_branch_list` | `branch_list` |
| `github_branch_switch` | `branch_switch` |
| `github_fetch` | none |
| `github_history_rewrite` | none |
| `github_issue_get` | none |
| `github_issue_list` | none |
| `github_local_branch_create` | `local_branch_create` |
| `github_pr_close` | none |
| `github_pr_comment_create` | none |
| `github_pr_comment_list` | none |
| `github_pr_create` | `pr_create` |
| `github_pr_diff` | none |
| `github_pr_get` | none |
| `github_pr_list` | none |
| `github_pr_merge` | none |
| `github_pr_reopen` | none |
| `github_pr_review_approve` | none |
| `github_pr_review_request_changes` | none |
| `github_provider_capabilities` | `provider_capabilities` |
| `github_pull` | none |
| `github_push` | none |
| `github_release_asset_upload` | none |
| `github_release_create` | none |
| `github_release_draft_delete` | none |
| `github_release_get` | none |
| `github_release_list` | none |
| `github_release_publish` | none |
| `github_release_update` | none |
| `github_release_withdraw` | none |
| `github_repository_commit` | `repository_commit` |
| `github_repository_commit_selected` | `repository_commit_selected` |
| `github_repository_create` | `repository_create` |
| `github_repository_description_get` | none |
| `github_repository_description_update` | none |
| `github_repository_diff` | `repository_diff` |
| `github_repository_moyai_project_set` | none |
| `github_repository_register` | none |
| `github_repository_rename` | none |
| `github_repository_settings_get` | `repository_settings_read` |
| `github_repository_settings_update` | `repository_settings_update` |
| `github_repository_stash_apply` | `repository_stash_apply` |
| `github_repository_stash_drop` | `repository_stash_drop` |
| `github_repository_stash_list` | `repository_stash_list` |
| `github_repository_stash_push` | `repository_stash_push` |
| `github_repository_status` | none |
| `github_repository_unregister` | none |
| `github_repository_update` | none |
| `github_tag_create` | `tag_create` |
| `github_tag_delete` | `tag_delete` |
| `github_tag_get` | none |
| `github_tag_list` | none |
| `github_tag_push` | `tag_push` |
| `github_workflow_dispatch` | none |
| `github_workflow_run_get` | none |
| `github_workflow_run_list` | none |
<!-- operation-table:end -->

`branch_create` is the existing GitHub branch operation; `local_branch_create` is the local branch operation. Do not substitute one for the other. Metadata tools such as `get_version` are outside the 58 `github_` list but still pass through the owner HTTP gate.

## Old nine cases to owner acceptance

The original Provider agreement is `.local/user-host-20261022/CONSUMER_ACCEPTANCE_CASES.md`, dated 2026-10-24. Retain it as ES256 history. The following replaces its signing/Issuer/Project/scope preconditions for the new owner run; it does not claim that the cases have been executed by Moyai.

| Original case | Owner case and requests | Required evidence |
| --- | --- | --- |
| R1 | O-R1: owner+generation initialize/ping/tools/list/get_version | Version >=2.1.0.0, valid schema1 owner discovery, successful metadata and no business dispatch. Validate all owner object fields/types/constants and exact target registration. |
| R2 | O-R2: unsigned capabilities/status with owner+generation | Correct owner contract and ordinary capability fields; selected fixture Repository/develop, baseline SHA and clean state. No Authorization, Issuer, Project grant or signature scope. |
| W1 | O-W1: Provider prepares agreed change.txt; Consumer commits exactly once | New returned full SHA equals independent HEAD, exactly one commit, expected file/index state; all other and remote refs unchanged; no push. |
| E1 | O-E1: missing/wrong owner or generation; wrong SID/ACL/runtime/profile/PID or stale discovery | HTTP gate rejection where request is allowed to reach the pinned test host; invalid discovery must instead fail before transmission. Verify no business dispatch/refs change. Actual other-user testing and invalid discovery simulations must be identified separately. |
| E2 | O-E2 replaces signature rejection: valid owner without Authorization succeeds; Authorization mixed with valid owner is rejected; absent/malformed owner capability, wrong mode/version/operation support fails closed in Consumer | The exact auth_mode_mismatch pair proves pre-handler rejection; a generic403 does not. Do not manufacture invalid Issuer/scope/Project failures in owner mode. Isolate malformed capability responses as Consumer tests. |
| E3 | O-E3: unsigned protected develop push with valid owner | Structured policy refusal; independent local/remote refs unchanged and no publication. This does not permit an external successful push. |
| S1 | O-S1: restart same disposable profile; retain old owner/generation then fresh metadata | Profile/hash fixed; credential+generation rotate; old rejected; reread endpoint and reinitialize; bounded metadata rediscovery succeeds; old generation cannot revoke new discovery. |
| S2 | O-S2: exhausted metadata retry; mutation owner rejection / transport loss / ambiguous write result | Bounded stop and zero automatic mutation replays. Fresh read/HEAD/ref/audit reconciliation. Consumer failure injection must be distinguished from Provider end-to-end failure. |
| C1 | O-C1: stop/expiry | ready revoked, stopped record, discovery removed, fail closed without static/production fallback, no pending write replay; preserve redacted evidence. |

Additional target-operation coverage is required beyond the old nine cases: local branch create (independent refs), Settings get and isolated approved update with preservation/rollback, PR/release operations where the Consumer supports them, and ordinary capability refusal. The current local fixture has no GitHub token and intentionally denies push: it cannot prove successful external PR/release operations. Test these through dedicated Consumer transport/Provider contract tests first; a real external success needs a separately agreed disposable target, policy/approval and cleanup. Report unsupported/not-tested targets explicitly rather than counting all 58 as interoperable.

## Run coordination and ownership

Moyai owns its Githubie-specific discovery/owner Consumer, private DB/config, failure injection, request count and redacted results. Githubie owns the fixture profile/repository, lifecycle and independent Git evidence. Moyai reported its Githubie Consumer unimplemented and plans to proceed after its earlier isolated acceptance work. Githubie does not modify that project's code/DB/keys or report that work as completed.

Before a run, both owners agree these O-case IDs and additional supported targets, Consumer candidate/test pin, Provider original and supplemental manifests, repository/profile/runtime pins and start time. No test signing key/Issuer/trust exchange or signature-bound Project UUID is required for owner mode; any local Consumer project identifier is routing only. Normal Projects/profiles/Service/keys/settings remain untouched.

The fixture supports `hold` for 30..300 seconds using its own isolated run profile. Ready metadata contains expiry/generation/runtime/profile pins, never plaintext session credentials. Consumer starts only on fresh ready=true, no stopped record and at least60 seconds remaining. Each request timeout <=15 seconds, host readiness deadline25 seconds, stop verification deadline10 seconds. Coordinate a new bounded run if insufficient time remains. Githubie stops/revokes discovery in finally; stop the old run before restart, and do not reuse stale ready. The fixture is currently stopped; sending this supplement is not an instruction to silently start it.

Final acceptance requires both owners' actual results for the agreed supported operations, rejection/rotation/non-replay and cleanup. The original CR remains open until that evidence is reconciled. Normal MSI migration, source commit/push and release are separate work.
