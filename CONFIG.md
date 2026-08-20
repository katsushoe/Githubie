# Configuration

[English](CONFIG.md) | [日本語](CONFIG.ja.md)

Githubie reads strict `snake_case` JSON. Unknown properties, comments, and trailing commas are rejected. See [githubie.example.json](githubie.example.json).

## File Location

The default path is `<install-root>\config\githubie.json`. Pass another path as the server's first argument or with the CLI `--config <path>` option.

## Root Properties

| Key | Required | Type | Default | Constraints and behavior |
| --- | --- | --- | --- | --- |
| `mcp_port` | Yes | integer | None | `1` through `65535`; the supplied example uses `45460` |
| `mcp_path` | Yes | string | None | Must start with `/`; the supplied example uses `/mcp` |
| `repositories` | Yes | object | None | Maps repository IDs to repository objects; an empty object allows startup but no repository operations |

## `repositories.<id>` Properties

Repository IDs must match `^[A-Za-z0-9._-]+$` and contain at most 128 characters.

| Key | Required | Type | Default | Constraints and behavior |
| --- | --- | --- | --- | --- |
| `github_owner` | Yes | string | None | Non-empty GitHub user or organization |
| `github_repo` | Yes | string | None | Non-empty GitHub repository name |
| `local_root` | Yes | string | None | Existing local repository root; `.git` must exist and reparse points are rejected |
| `remote` | Yes | string | None | Fixed Git remote used by Git operations; the example uses `origin` |
| `develop_branch` | Yes | string | None | Non-empty source branch of the allowed pull-request route |
| `main_branch` | Yes | string | None | Non-empty destination branch of the allowed pull-request route |
| `direct_push_branches` | Yes | string array | None | Branches accepted by `github_push` unless protected |
| `pull_branches` | Yes | string array | None | Branches accepted by `github_pull` |
| `protected_branches` | Yes | string array | None | Branches rejected by direct push |
| `tag_target_branch` | Yes | string | None | Branch whose HEAD may be tagged |
| `tag_pattern` | Yes | string | None | Valid regular expression applied to tag names |
| `merge_method` | Yes | string | None | One of `merge`, `squash`, or `rebase` |
| `require_clean_working_tree` | Yes | boolean | None | Rejects push when the working tree is not clean |

The pull-request route is always `develop_branch` to `main_branch`; clients cannot supply another route.

## Example

```json
{
  "mcp_port": 45460,
  "mcp_path": "/mcp",
  "repositories": {
    "example": {
      "github_owner": "owner",
      "github_repo": "repository",
      "local_root": "C:\\src\\repository"
    }
  }
}
```

## Validation

`githubie.exe config check` validates JSON, all constraints above, `local_root`, and `.git`. Startup performs schema and value validation. Invalid values return named errors such as `InvalidMcpPort`, `InvalidRepositoryId`, `InvalidTagPattern`, or `InvalidMergeMethod`.

## Personal Access Token

Never place tokens in JSON. `githubie.exe auth set <repository-id>` encrypts a token with DPAPI LocalMachine and stores it under `<install-root>\data\secrets`. See [Security](SECURITY.md).
