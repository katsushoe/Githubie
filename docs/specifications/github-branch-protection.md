# GitHub branch protection — candidate contract

This document describes the development candidate; it does not assert that the installed service or a Moyai Consumer supports these operations. [日本語](github-branch-protection.ja.md) / [ADR0044](../adr/0044-github-branch-protection.md).

## Scope and authorization

These operations use GitHub's classic **exact branch protection rule** for a registered Githubie Repository. Githubie's `protected_branches` only controls operations routed through Githubie. GitHub rulesets are separate and may still protect a branch after a classic rule is removed. A classic-rule result does not fully describe ruleset protection.

`github_branch_protection_get` requires GitHub Administration read access. `github_branch_protection_update` and `github_branch_protection_delete` require GitHub Administration write access and interactive Desktop approval for every actual change. Owner-session authentication and, in legacy signed mode, the `repository.read` or dedicated `provider.githubie.branch_protection.write` scope do not replace GitHub permissions or Desktop approval. The feature reports `branch_protection_read` and `branch_protection_write` only when its service is available; the candidate adds three owner operations.

## Operations

- `github_branch_protection_get(repository, branch)` returns the classic rule's branch, whether it can be safely updated, and normalized policy when supported. Unsupported or unfamiliar security settings yield `can_update=false` and no policy body. A GitHub 404 is `branch_protection_not_found_or_inaccessible`, not proof that the branch is unprotected or that credentials are valid.
- `github_branch_protection_update(repository, branch, edit, create_if_missing)` changes explicitly requested fields. Nullable `edit` properties mean preserve: `require_pull_request`, `required_approving_reviews` (0–6), `allow_force_pushes`, `allow_deletions`, `enforce_admins`, `require_status_checks`, `status_check_contexts` and `strict_status_checks`. Reject empty or contradictory edits and exact-branch names containing wildcards or invalid ref characters. Enabling status checks requires an explicit contexts array. A context-list replacement is rejected when it would discard an existing GitHub App binding.
- For creation, `create_if_missing=true` is required, the branch must be observed as unprotected, and review, force-push, deletion, administrator and status-check decisions must be explicit. A 404 can still be caused by access restrictions; the operation fails closed if the branch or policy cannot be resolved safely. The caller must explicitly provide an approval count when requiring PR reviews. The final proposed policy is displayed in the approval prompt.
- `github_branch_protection_delete(repository, branch)` removes the classic rule only after displaying the existing rule and obtaining approval. Unrecognized or unrepresentable rule settings cause rejection before mutation.

Updates preserve supported unrelated review, status-check, principal and security settings. Unknown fields, enabled required-signature settings or unavailable evidence cause rejection rather than a partial PUT. This conservative boundary may reject rules that GitHub itself can edit. The approval dialog displays before and after; changes are rejected if the rule differs at the second authenticated read. GitHub's REST PUT has no documented atomic precondition, so a separate client can still change it between that read and the PUT.

## Failures and CLI

A 403 is reported as `branch_protection_access_denied`; verify both Administration permission and plan availability. A status alone is not enough to distinguish them. Missing approval, unsafe settings, input errors and detected concurrent changes are `not_executed`. A lost or inconsistent mutation result is `branch_protection_result_unknown`, `outcome=unknown`, `retryable=false`, `suggested_action=check_status`; read independently before deciding on any later action. Audit records the operation and result, plus SHA-256 fingerprints of the repository and branch, without request bodies or external error diagnostics.

CLI uses the existing generic `githubie mcp call` route with the same JSON arguments, authentication, approval and errors. For example, use `githubie mcp call github_branch_protection_get '{"repository":"example","branch":"main"}'`; the JSON is an example, not authorization to operate on any real repository. The update and delete operations are destructive and do not bypass Desktop approval.
