# Troubleshooting

[English](TROUBLESHOOTING.md) | [日本語](TROUBLESHOOTING.ja.md)

## Repository and Git Errors

| Error | Cause and recovery |
| --- | --- |
| `repository_not_found` / `repository_not_allowed` | Use an ID registered under `repositories` in [Configuration](CONFIG.md). |
| `local_root_not_found` / `git_metadata_not_found` | Correct `local_root` and ensure its `.git` directory exists. |
| `reparse_point_detected` | Configure the physical path without a symlink or junction. |
| `remote_mismatch` | Make the Git remote match the configured GitHub owner and repository. |
| `git_not_found` / `git_failed` | Install Git for Windows, check `PATH`, and inspect `repo status`. |
| `working_tree_dirty` | Commit or stash changes when clean-tree enforcement is enabled. |
| `branch_not_allowed` / `protected_branch` | Use an allowed branch and the configured pull-request route. |
| `nothing_to_push` | Create a local commit before pushing. |
| `non_fast_forward` | Resolve divergence manually; Githubie only performs fast-forward pulls. |

## GitHub API Errors

| Error | Cause and recovery |
| --- | --- |
| `authentication_failed` | Replace an absent, revoked, or invalid token. |
| `permission_denied` / `token_scope_missing` | Grant the fine-grained PAT the required repository permissions. |
| `rate_limited` / `secondary_rate_limited` | Wait for reset or reduce request frequency. |
| `branch_not_found` / `pull_request_not_found` / `tag_not_found` | Verify the requested identifier with the corresponding list tool. |
| `pull_request_not_open` / `pull_request_not_mergeable` | Verify state and resolve conflicts before merging. |
| `pull_request_route_not_allowed` | Use the configured `develop_branch` to `main_branch` route. |
| `tag_invalid` / `tag_already_exists` / `tag_target_not_allowed` | Check `tag_pattern`, uniqueness, and the configured target branch. |
| `network_error` / `timeout` / `github_api_error` | Check connectivity, GitHub status, and the audit error code, then retry. |

## Startup and Authentication

If the server exits immediately, run `githubie.exe config check`. If MCP is unreachable, verify service status and the configured port/path. For `auth set` ACL failures from pre-`1.0.0.0` installations, upgrade the MSI. If a pasted token is unexpectedly long or authentication fails immediately, delete it and enter it again.
# Approval prompt diagnostics

If repository registration or history rewrite returns `approval_unavailable`, inspect the daily Githubie log. Session lookup and process launch failures are logged separately. A genuine unanswered prompt returns `approval_timed_out`; launch failures do not wait for the approval timeout.
