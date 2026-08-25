# Troubleshooting

[English](TROUBLESHOOTING.md) | [日本語](TROUBLESHOOTING.ja.md)

## Repository and Git Errors

| Error | Cause and recovery |
| --- | --- |
| `repository_not_found` / `repository_not_allowed` | Use an ID registered under `repositories` in [Configuration](CONFIG.md). |
| `local_root_not_found` / `git_metadata_not_found` | Correct `local_root` and ensure its `.git` directory exists. |
| `reparse_point_detected` | Configure the physical path without a symlink or junction. |
| `remote_mismatch` | Make the Git remote match the configured GitHub owner and repository. |
| `remote_https_required` | Change the Git remote to `https://github.com/OWNER/REPOSITORY.git`; SSH remotes are not accepted. |
| `git_not_found` / `git_failed` | Install Git for Windows, check `PATH`, inspect `repo status`, and use `correlationId` to locate the audit event. |
| `authentication_failed` / `permission_denied` | Refresh the configured credential or grant the required repository permission. |
| `network_error` | Check DNS, proxy, TLS, and network connectivity, then retry. |
| `remote_unavailable` | Verify the configured repository, remote, and ref. |
| `non_fast_forward` | Fetch and reconcile remote changes before retrying. |
| `working_tree_dirty` | Commit or stash changes when clean-tree enforcement is enabled. |
| `branch_protection_denied` | GitHub branch protection or a repository ruleset rejected the rewrite. Review the target ref's rules and force-push policy. |
| `token_permission_denied` | The configured token lacks repository write permission. Reissue or update the token, then run `auth test`. |
| `workflow_permission_denied` | The rewrite changes workflow files but the token lacks workflow permission. Grant the required workflow permission. |
| `lease_conflict` | The remote ref changed after planning. Fetch again and repeat dry-run with the current remote SHA. |
| `atomic_not_supported` | The remote cannot apply all ref updates atomically. Githubie does not fall back to partial updates. |

`repo status` and `github_repository_status` return up to 20 `working_tree_changes` entries containing only the two-character porcelain status and repository-relative path. No file content is returned. A file ignored only by an interactive user's global Git excludes may still appear when the LocalSystem service evaluates the repository; add operational exclusions to the repository `.gitignore` when all execution identities must share them.
| `branch_not_allowed` / `protected_branch` | Use an allowed branch and the configured pull-request route. |
| `nothing_to_push` | Create a local commit before pushing. |
| `non_fast_forward` | Resolve divergence manually; Githubie only performs fast-forward pulls. |

## GitHub API Errors

| Error | Cause and recovery |
| --- | --- |
| `workflow_not_allowed` / `workflow_ref_not_allowed` | Use a workflow and ref declared in the repository `workflows` policy. |
| `workflow_input_invalid` | Supply only configured keys with valid types, required values, and lengths. |
| `workflow_concurrency_limit` | Wait for the active dispatch to finish, then retry. |
| `workflow_run_not_found` | Verify the run ID and repository. |
| `workflow_run_correlation_failed` | List runs and identify the result manually; do not dispatch again until ambiguity is resolved. |
| `authentication_failed` | Replace an absent, revoked, or invalid token. |
| `permission_denied` / `token_scope_missing` | Grant the fine-grained PAT the required repository permissions. |
| `rate_limited` / `secondary_rate_limited` | Wait for reset or reduce request frequency. |
| `branch_not_found` / `pull_request_not_found` / `tag_not_found` | Verify the requested identifier with the corresponding list tool. |
| `pull_request_not_open` / `pull_request_not_mergeable` | Verify state and resolve conflicts before merging. `pull_request_not_mergeable` is returned only for confirmed conflicts. |
| `mergeability_calculating` | Status is `calculating_retryable`; wait `retry_after_seconds` and call `github_pr_get` or merge again. |
| `mergeability_unknown` | Status is `unknown_retryable`; wait `retry_after_seconds` and recheck because GitHub could not yet classify the result. |
| `pull_request_blocked` | Required reviews, checks, draft state, branch rules, or update requirements block merging; satisfy them before retrying. |
| `pull_request_route_not_allowed` | Use the configured `develop_branch` to `main_branch` route. |
| `pull_request_state_not_allowed` | A merged pull request cannot be closed or reopened. Verify its current state. |
| `pull_request_comment_invalid` | Supply a non-empty comment body of at most 65,536 characters. |
| `pull_request_review_invalid` | Supply a review body of at most 65,536 characters; change requests require a non-empty body. |
| `tag_invalid` / `tag_already_exists` / `tag_target_not_allowed` | Check `tag_pattern`, uniqueness, and the configured target branch. |
| `release_not_found` | Verify the tag or Release ID with `github_release_list`. |
| `release_already_exists` | Inspect the existing Release; only a matching draft can be resumed by `github_release_create`. |
| `release_asset_already_exists` | Retry without that file or explicitly set `replace_existing=true`. |
| `release_asset_invalid` / `release_asset_not_found` / `release_upload_failed` | Use one to ten repository-local MSI, ZIP, `.sha256`, `SHA256SUMS.txt`, or `.ps1` files and retry while the Release is a draft. |
| `network_error` / `timeout` / `github_api_error` | Check connectivity, GitHub status, and the audit error code, then retry. |

## Startup and Authentication

If the server exits immediately, run `githubie.exe config check`. If MCP is unreachable, verify service status and the configured port/path. For `auth set` ACL failures from pre-`1.0.0.0` installations, upgrade the MSI. If a pasted token is unexpectedly long or authentication fails immediately, delete it and enter it again.
# Approval prompt diagnostics

If repository registration or history rewrite returns `approval_unavailable`, inspect the daily Githubie log. Session lookup and process launch failures are logged separately. A genuine unanswered prompt returns `approval_timed_out`; launch failures do not wait for the approval timeout.
