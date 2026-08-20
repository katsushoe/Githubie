# Commands

[English](COMMANDS.md) | [日本語](COMMANDS.ja.md)

## CLI

Use `githubie.exe --config <path>` to override the default configuration.

| Command | Result or state change |
| --- | --- |
| `githubie help` | Prints the command list |
| `githubie version` | Prints the CLI version |
| `githubie logs` | Prints the log directory |
| `githubie config check` | Validates JSON, values, local roots, and `.git` directories |
| `githubie config show` | Prints the loaded port, path, and repository IDs |
| `githubie repo list` | Prints configured repository IDs |
| `githubie repo status <repository>` | Reads current branch, HEAD, ahead/behind, and working-tree state |
| `githubie auth set <repository>` | Replaces the DPAPI-encrypted token after masked input |
| `githubie auth test <repository>` | Calls GitHub with the stored token and reports authentication status |
| `githubie auth delete <repository>` | Deletes the stored token |
| `githubie mcp status` / `mcp test` | Sends MCP `initialize` and reports connectivity |
| `githubie mcp tools` | Sends `tools/list` and prints the exposed definitions |
| `githubie doctor` | Reports configuration, Git, service composition, token, and repository checks |
| `githubie start` / `stop` / `restart` / `status` | Changes or reads the Windows Service state |
| `githubie service install` / `uninstall` / `status` | Registers, unregisters, or reads the Windows Service |

Successful diagnostic commands print `[OK]`; failures print `[NG]` and return a nonzero exit code. Commands that query repository state derive branch, HEAD, ahead/behind, and cleanliness values from the configured local repository at call time.

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
| `github_branch_list` | `repository` | Remote branches visible to the stored token |
| `github_branch_get` | `repository`, `branch` | The named remote branch and HEAD SHA; fails if absent |
| `github_pr_list` | `repository`, `state?`, `source?`, `destination?` | Pull requests matching the optional filters |
| `github_pr_get` | `repository`, `pull_request_number` | Pull-request metadata for an existing number |
| `github_pr_diff` | `repository`, `pull_request_number` | Diff and change statistics for an existing pull request |
| `github_tag_list` | `repository` | Repository tags visible through GitHub |
| `github_tag_get` | `repository`, `tag` | Tag and target details; fails if absent |
| `get_version` | None | Running Githubie Server version |

## State-changing Tools

| Tool | Parameters | State and constraints |
| --- | --- | --- |
| `github_repository_register` | `repository`, `local_root`, `remote?`, `develop_branch?`, `main_branch?` | Derives GitHub identity from the local remote and registers it only after desktop approval |
| `github_fetch` | `repository` | Updates remote-tracking refs |
| `github_pull` | `repository`, `branch` | Fast-forwards an allowed branch; rejects divergent history |
| `github_push` | `repository` | Pushes the current allowed branch; rejects protected branches, dirty trees when configured, and no-op pushes |
| `github_pr_create` | `repository`, `title`, `description?`, `draft` | Creates only the configured `develop` to `main` route |
| `github_pr_merge` | `repository`, `pull_request_number`, `merge_strategy?`, `message?` | Merges an open, mergeable pull request on the allowed route |
| `github_tag_create` | `repository`, `tag`, `message?` | Creates an annotated tag matching `tag_pattern` at the configured target branch HEAD |

## Audit Log

Each call records client, tool, repository, relevant branch/PR/tag fields, result, duration, and error code in `<install-root>\logs\githubie-yyyyMMdd.log`. Tokens, authorization headers, and raw error messages are excluded.
