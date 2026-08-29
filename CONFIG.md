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
| `repositories` | Yes | object | None | Legacy import seed. On first database initialization these entries are imported into SQLite; later JSON changes are not re-imported |

## `repositories.<id>` Properties

Repository IDs follow the Itoguruma Project Inbox ID rule: stored IDs match `^[a-z][a-z0-9]*$` and contain at most 128 characters. Registration and rename inputs are normalized to invariant lowercase, and lookups are case-insensitive.
During upgrade, legacy IDs matching the former `^[A-Za-z0-9._-]+$` rule are migrated by removing `.`, `_`, and `-`, then converting the result to invariant lowercase. An ambiguous or invalid result stops startup instead of selecting a repository implicitly.

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
| `workflows` | No | object | `{}` | Allowlisted workflow filename/ID mapped to refs, input schemas, concurrency, and correlation timeout |

Each workflow policy requires `allowed_refs`; inputs may use `string`, `boolean`, or `integer`, with `required`, `max_length` (1–4096), and `secret`. `max_concurrent` is 1–10 and `correlation_timeout_seconds` is 1–120. Policy changes through `github_repository_update` require desktop approval.

The pull-request route is always `develop_branch` to `main_branch`; clients cannot supply another route.

The repository source of truth is `<install-root>\data\githubie.db`. `github_repository_register` adds an existing local GitHub repository to that database at runtime. It derives `github_owner` and `github_repo` from the selected local remote, requires desktop approval, and applies safe branch-policy defaults. The selected remote must use `https://github.com/OWNER/REPOSITORY.git`; SSH remotes are rejected. A service restart is not required.

At the first startup after upgrading, validated entries under `repositories` are imported transactionally. A migration marker prevents later startups from overwriting database changes with stale JSON. Keep the legacy JSON until migration and backup verification are complete; use repository management operations for later changes.

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

`githubie.exe config check` validates JSON and any legacy import entries, including `local_root` and `.git`. Startup additionally initializes and reads SQLite. Use `githubie.exe repo list` and `doctor` to inspect the effective database-backed registrations.

## Personal Access Token

Never place tokens in JSON. `githubie.exe auth set <repository-id>` encrypts a token with DPAPI LocalMachine and stores it under `<install-root>\data\secrets`. See [Security](SECURITY.md).
