# Commands

[English](COMMANDS.md) | [日本語](COMMANDS.ja.md)

## CLI

Use `githubie.exe --config <path>` to override the default configuration.

| Command | Result or state change |
| --- | --- |
| `githubie help` | Prints the command list |
| `githubie version` | Prints the CLI version |
| `githubie logs` | Prints the log directory |
| `githubie config check` | Validates JSON and legacy-import repository roots and `.git` directories |
| `githubie config show` | Prints the loaded port, path, and repository IDs |
| `githubie repo list` | Prints configured repository IDs |
| `githubie repo status <repository>` | Reads current branch, HEAD, ahead/behind, and working-tree state |
| `githubie repo description get <repository>` | Gets the repository description |
| `githubie repo description update <repository> <description>` | Updates the description; an empty string removes it |
| `githubie repo rename <old> <new>` | Atomically migrates repository configuration and its encrypted token to a new ID |
| `githubie auth set <repository>` | Replaces the DPAPI-encrypted token after masked input |
| `githubie auth test <repository>` | Calls GitHub with the stored token and reports authentication status |
| `githubie auth delete <repository>` | Deletes the stored token |
| `githubie mcp status` / `mcp test` | Sends MCP `initialize` and reports connectivity |
| `githubie mcp tools` | Sends `tools/list` and prints the exposed definitions |
| `githubie mcp call <tool> [<arguments-json>]` | Calls any exposed MCP tool through the running server; arguments default to `{}` |
| `githubie mcp call <tool> --file <path>` | Calls an MCP tool with a JSON object read from a file |
| `githubie doctor` | Reports configuration, Git, service composition, token, and repository checks |
| `githubie start` / `stop` / `restart` / `status` | Changes or reads the Windows Service state |
| `githubie service install` / `uninstall` / `status` | Registers, unregisters, or reads the Windows Service |

Successful diagnostic commands print `[OK]`; failures print `[NG]` and return a nonzero exit code. Commands that query repository state derive branch, HEAD, ahead/behind, and cleanliness values from the configured local repository at call time.

`mcp call` prints the JSON-RPC response as JSON and returns a nonzero exit code for transport, JSON-RPC, MCP, or structured tool failures. It deliberately delegates to the running MCP server so CLI calls use the same allowlist, approvals, audit log, and safety policy as other MCP clients. Do not place secrets in tool arguments.

## MCP Result Envelope

Every tool returns `{ ok, operation, repository, data, error }`. `ok` reflects the operation outcome. `data` is populated on success from local Git state, GitHub API state, configuration, or the server version as appropriate. `error` is populated on failure and includes a stable code documented in [Troubleshooting](TROUBLESHOOTING.md).

```json
{"ok":true,"operation":"github_branch_get","repository":"example","data":{"name":"develop","sha":"..."},"error":null}
```

```json
{"ok":false,"operation":"github_push","repository":"example","data":null,"error":{"code":"protected_branch"}}
```

## Read-only Tools

| Tool | Parameters | Data source and result |
| --- | --- | --- |
| `github_repository_status` | `repository` | Local/remote HEAD, ahead/behind, and working-tree state from Git |
| `github_repository_description_get` | `repository` | Repository description from GitHub |
| `github_workflow_run_get` | `repository`, `run_id` | Workflow run status and metadata without logs |
| `github_workflow_run_list` | `repository`, optional filters, `limit` | Up to 100 workflow runs without logs |
| `github_branch_list` | `repository` | Remote branches visible to the stored token |
| `github_branch_get` | `repository`, `branch` | The named remote branch and HEAD SHA; fails if absent |
| `github_pr_list` | `repository`, `state?`, `source?`, `destination?` | Pull requests matching the optional filters |
| `github_pr_get` | `repository`, `pull_request_number` | Pull-request metadata for an existing number |
| `github_pr_diff` | `repository`, `pull_request_number` | Diff and change statistics for an existing pull request |
| `github_pr_comment_list` | `repository`, `pull_request_number` | Conversation comments for an existing pull request |
| `github_tag_list` | `repository` | Repository tags visible through GitHub |
| `github_tag_get` | `repository`, `tag` | Tag and target details; fails if absent |
| `github_release_list` | `repository` | Releases and their assets |
| `github_release_get` | `repository`, `tag` | Release and asset details for a tag |
| `get_version` | None | Running Githubie Server version |

## State-changing Tools

| Tool | Parameters | State and constraints |
| --- | --- | --- |
| `github_repository_register` | `repository`, `local_root`, `remote?`, `develop_branch?`, `main_branch?` | Derives GitHub identity from the local remote and registers it only after desktop approval |
| `github_repository_update` | `repository`, branch policy fields | Updates branch policy only after desktop approval; identity and paths remain unchanged |
| `github_repository_unregister` | `repository` | Removes the entry from Githubie configuration and the live allowlist without deleting GitHub or local data |
| `github_repository_rename` | `old_repository`, `new_repository` | Migrates configuration and encrypted token together; keeps the old ID usable if migration fails |
| `github_repository_description_update` | `repository`, `description` | Patches only `description`; empty string removes it; maximum 350 characters |
| `github_workflow_dispatch` | `repository`, `workflow`, `ref`, `inputs` | Dispatches only configured workflows and correlates exactly one new run |
| `github_fetch` | `repository` | Updates remote-tracking refs |
| `github_pull` | `repository`, `branch` | Fast-forwards an allowed branch; rejects divergent history |
| `github_push` | `repository` | Pushes the current allowed branch, creating it on the remote when absent and requiring fast-forward updates when present; rejects protected branches, dirty trees when configured, and no-op pushes |
| `github_pr_create` | `repository`, `title`, `description?`, `draft` | Creates only the configured `develop` to `main` route |
| `github_pr_merge` | `repository`, `pull_request_number`, `merge_strategy?`, `message?` | Polls mergeability at most 3 times at 2-second intervals, then merges an open, mergeable pull request on the allowed route |
| `github_pr_close` | `repository`, `pull_request_number` | Closes an open, unmerged pull request; already-closed requests are unchanged |
| `github_pr_reopen` | `repository`, `pull_request_number` | Reopens a closed, unmerged pull request; already-open requests are unchanged |
| `github_pr_comment_create` | `repository`, `pull_request_number`, `body` | Adds a non-empty conversation comment to an existing pull request |
| `github_pr_review_approve` | `repository`, `pull_request_number`, `body?` | Approves an open pull request with an optional review body |
| `github_pr_review_request_changes` | `repository`, `pull_request_number`, `body` | Requests changes on an open pull request with a required review body |
| `github_tag_create` | `repository`, `tag`, `message?` | Creates an annotated tag matching `tag_pattern` at the configured target branch HEAD |
| `github_tag_delete` | `repository`, `tag` | Deletes a policy-compliant tag; returns `tag_not_found` when absent |
| `github_release_create` | `repository`, `tag`, `name`, `body?`, `draft`, `prerelease`, `assets` | Creates or resumes a matching draft, uploads all missing assets, then publishes only after success |
| `github_release_update` | `repository`, `release_id`, `name?`, `body?`, `draft?`, `prerelease?` | Updates explicitly supplied release fields |
| `github_release_asset_upload` | `repository`, `release_id`, `assets`, `replace_existing` | Adds up to ten approved assets; same-name replacement requires `replace_existing=true` |

## Audit Log

Each call records client, tool, repository, relevant branch/PR/tag fields, result, duration, and error code in `<install-root>\logs\githubie-yyyyMMdd.log`. Tokens, authorization headers, and raw error messages are excluded.
