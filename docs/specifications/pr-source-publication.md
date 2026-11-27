# PR source publication contract

[English](pr-source-publication.md) | [日本語](pr-source-publication.ja.md)

Status: current-source correction candidate 2.1.5.0, not installed or released. The prior Consumer operation mapping is agreed; review of the new source policy, Consumer implementation and live acceptance remain separate gates. Decision: [ADR 0047](../adr/0047-pr-publication-source-allowlist.md), superseding the route-only policy in ADR0046.

## Operations and inputs

`github_pr_publish_create` returns operation `pull_request_publish_create`. Capability `pull_request_publish_create=true` is advertised only when the publication service is configured. Consumer must explicitly opt into this operation; existing `github_pr_create` remains unchanged.

Required fields: registered `repository`; explicit `source_branch` and `destination_branch`; 40 hexadecimal `expected_source_sha`; nonempty `title` (at most 256 characters, no controls); UUID `operation_id` in hyphenated D format. Optional: `description` (at most 65536 characters), `draft` (false). UUID and SHA letter case are canonicalized; branch case is significant. Source/destination must differ. Only local branch names are accepted: no revision expressions, tags, full refs or arbitrary refspecs.

`github_pr_publish_status` returns operation `pull_request_publish_status` with `repository` and `operation_id`. Capability `pull_request_publish_status=true`. It never creates refs or PRs. Known terminal records are returned as historical results. Unknown effects are read-only reconciled; the observation timestamp is returned.

CLI: `githubie pr publish-create --file <arguments.json>` and `githubie pr publish-status <repository> <operation-id>`. Both connect to the existing current-user host, return the same JSON including failure data, and use a nonzero exit code on failure. The dedicated MCP client forwards these commands to the same host. Both use only `<actual install root>\config\githubie.json`; an alternate configuration is rejected.

## Settings and authorization

Source must be an exact member of `pr_publication_source_branches`, must not be protected, and the source/destination pair must exactly match `pull_request_routes`. Missing or empty source lists deny publication. The obsolete `local_commit_branches` setting is ignored; ordinary local commits retain their current-branch policy. `require_clean_working_tree` is enforced when true; dirty files are never part of the fixed commit. Ordinary direct-push permission is neither required nor widened. Remote fetch and the single effective push URL must identify the registered GitHub repository; ambiguous/multiple destinations are rejected.

In legacy assertion mode, creation requires both `repository.push` and `provider.githubie.pull_request.write`; status requires `repository.read`. In Windows owner mode, the authenticated owner SID replaces Project Assertion authorization. Records bind to the owner SID and repository identity/root, independently of obsolete Project mappings. Other owners cannot reuse them. Current authorization and execution revalidation remain active. Provider interactive approval displays repository, source, destination, expected SHA and operation ID. Denial/unavailability stops before external effects.

## Publication and PR states

- Source commit must match the expected SHA before and after approval and before PR creation.
- Missing remote source is created at that immutable SHA with a creation-only condition. Existing equal SHA needs no write; unequal SHA is rejected even if a fast-forward is possible. Tags, mirrors, other branches and submodules are excluded.
- PR creation verifies destination existence, current remote source and existing open PRs. An existing matching PR may be reused; mismatched SHA/repository is refused.
- Response fields include `operation_id`, source/destination/SHA, `publication_state`, `pull_request_state`, `pull_request_number`, URL/head SHA, observed remote SHA/time, `stage`, `requires_reconciliation`.
- Publication states: `not_attempted`, `created`, `already_present`, `failed`, `unknown`. PR states use the same values. `already_present` does not prove this request created the object.
- `ok=true` requires both a confirmed source and a matching open PR. Failure keeps `data` populated. `error.outcome` is `not_executed`, `failed` or `unknown`; unknown uses `retryable=false` and `check_status`.

## Errors and recovery

Typed codes include `publication_source_not_allowed`, `publication_invalid`, `expected_head_mismatch`, `branch_not_found`, `protected_branch`, `pull_request_route_not_allowed`, `working_tree_dirty`, `provider_remote_not_found/ambiguous/mismatch`, authentication/permission errors, `approval_denied/unavailable`, `operation_conflict/in_progress/not_found`, `source_operation_unresolved`, `remote_branch_conflict`, `publication_failed/result_unknown`, `pull_request_result_unknown/head_mismatch`, and journal/status/audit availability failures. Error diagnostics never include HTTP bodies or credentials.

Operation records live in `pr_publication_operations` in the current user-profile repository DB. In-flight states are written before each mutation. Same UUID and same normalized request only return/reconcile the recorded result; different input is an operation conflict. A previous unknown effect on the source blocks a new operation ID. After a timeout/cancel/crash, use status first. Absence from a single PR listing never authorizes a retry. Replaying the same original input performs only read reconciliation and can persist a known result; it never automatically resumes a mutation. Any later operation requires an explicit new request and all current permission/ref checks. Failed PR creation leaves publication visible and never deletes it.

The service cannot lock GitHub refs against other users indefinitely. Publication/PR are separate effects; clients must inspect both states. Source creation uses an explicit empty expected-value lease internally; existing refs cannot be changed, and arbitrary force options are not exposed. Audit includes operation ID, fixed SHA, source/destination, stages/effects and correlation ID, never description/token/signature.

## Upgrade and acceptance

The installer default is `C:\Githubie`; the selected installation directory is authoritative. Runtime settings use only `<actual install root>\config\githubie.json`. Operation records use the existing user-profile repository database; initialization adds the operation table without importing old service data. Local packaging does not change an installed host or live DB. Current integration regression results accompany the candidate manifest; historical isolated test results do not establish current acceptance. Consumer interoperability, native-client/live acceptance and approved deployment remain required.

## Independent source permission and migration

Candidate2.1.5.0 implements [ADR0047](../adr/0047-pr-publication-source-allowlist.md). Publication requires both `pr_publication_source_branches` and an exact PR route. Missing/empty lists deny publication; null, duplicates, patterns and full refs are invalid. Matching is ordinal and case sensitive. A route for `docs/other` does not authorize publication when that branch is absent from the independent list. Both-list-and-route permission still requires every other guard. Legacy local commit values are never migrated automatically; ordinary commit and direct-push policy remain unchanged. This differs from the earlier CR tying publication to ordinary local commit permission. The requester updated that CR after the user selected independent publication permission on2026-11-04; actual policy application and CR completion remain separate.

Use the approved registered settings operation, or `githubie repo settings update --file <arguments.json>` (`githubiemcp` forwards the same command). The file contains `repository` and `changes`; GET now includes ten settings. Explicit empty clears the publication list, omitted preserves it, null rejects it. For the user-selected `codex/docs-main-v1010`, explicitly include that source and the reviewed exact destination in the replacement arrays; preserve all other intended entries. The Japanese guide contains the requester-selected review-only codex/docs-main-v1010 -> main payload. Replacement arrays must preserve other intended entries. No production change is requested by this example. Editing a JSON seed does not update existing registered database rows. Old versions reject the new field, so preserve pre-change configuration/database backups and restore the baseline before downgrade.

`publication_source_not_allowed` returns `outcome=not_executed` before effects. Revocation after approval or at dispatch stops the next mutation and preserves a branch already published. Existing operation IDs/status calls never resume a mutation.
