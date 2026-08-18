# 設定

`githubie.json`の全項目と検証ルールを記載する。読み込みは`Githubie.Infrastructure.Configuration.JsonGithubieOptionsLoader`が行い、プロパティ名はすべてsnake_caseで統一する。JSON中の未知プロパティ・コメント・末尾カンマは拒否する。

サンプルは[githubie.example.json](githubie.example.json)を参照する。

## ファイル配置

既定では`<install-root>\config\githubie.json`を読み込む。`githubie.exe` / `Githubie.Server.exe`のいずれも第1引数、または`githubie.exe`は`--config <path>`で明示指定できる。

```powershell
Githubie.Server.exe C:\path\to\githubie.json
githubie.exe --config C:\path\to\githubie.json config check
```

## ルート項目

| キー | 型 | 既定値 | 説明 |
| --- | --- | --- | --- |
| `mcp_port` | integer (1-65535) | `45460` | MCP Streamable HTTP Endpointの待受ポート。Buckettieの`45450`と衝突しないよう別値にしてある |
| `mcp_path` | string（`/`始まり） | `/mcp` | MCP Endpointのパス |
| `repositories` | object | — | Repository ID(キー)ごとの設定。空でも起動は可能だが、公開されるTool呼び出しはすべて`repository_not_allowed`になる |

## `repositories.<id>`項目

Repository IDは`^[A-Za-z0-9._-]+$`、最大128文字（`Githubie.Application.Repositories.RepositoryId`で検証）。MCP Agentはこの内部IDだけを指定し、`github_owner` / `github_repo` / `local_root`を自由指定できない。

| キー | 型 | 説明 |
| --- | --- | --- |
| `github_owner` | string | GitHub Owner（ユーザー名またはOrganization名） |
| `github_repo` | string | GitHub Repository名 |
| `local_root` | string | ローカルGitリポジトリのルートパス。存在確認・`.git`存在確認・symlink/junction混入確認を行う |
| `remote` | string | 使用するGit Remote名（通常`origin`） |
| `develop_branch` | string | 日常開発でdirect pushするBranch |
| `main_branch` | string | Release対象Branch |
| `direct_push_branches` | string[] | `github_push`で直接Pushを許可するBranch一覧 |
| `pull_branches` | string[] | `github_pull`で対象にできるBranch一覧 |
| `protected_branches` | string[] | `github_push`を拒否するBranch一覧（`protected_branch`エラー） |
| `tag_target_branch` | string | `github_tag_create`が許可するTag対象Branch（既定でmain HEADのみ） |
| `tag_pattern` | string | Tag名の許可パターン（正規表現） |
| `merge_method` | string | `merge` / `squash` / `rebase`のいずれか。`github_pr_merge`の既定Strategy |
| `require_clean_working_tree` | boolean | `true`の場合、Working Treeが汚れていると`github_push`を拒否する |

Pull Request経路（source→destination）は`develop_branch → main_branch`固定で、設定ファイルに個別項目はない。Agentや設定ファイルに自由な経路を指定させない設計上の判断による。

## 検証ルール

`config check`（および起動時のComposition Root）は以下を検証する。

- JSON構文・スキーマ違反（`InvalidJson`）
- `mcp_port`が1〜65535の範囲外（`InvalidMcpPort`）
- `mcp_path`が空または`/`始まりでない（`InvalidMcpPath`）
- Repository IDが命名規則違反（`InvalidRepositoryId`）
- `github_owner` / `github_repo` / `local_root`が空（`InvalidGitHubOwner` / `InvalidGitHubRepo` / `InvalidLocalRoot`）
- `develop_branch` / `main_branch`が空（`InvalidBranchName`）
- `tag_pattern`が不正な正規表現（`InvalidTagPattern`）
- `merge_method`が`merge` / `squash` / `rebase`以外（`InvalidMergeMethod`）

`githubie.exe config check`はこれに加えて、`local_root`の実在と`.git`の実在をファイルシステム上で確認する。

## Personal Access Token

Tokenは`githubie.json`に含めない。`githubie.exe auth set <repository-id>`でDPAPI（LocalMachineスコープ）暗号化のうえ`<install-root>\data\secrets\<repository-id>.token`へ1ファイル/Repositoryで保存する。詳細は[SECURITY.md](SECURITY.md)を参照する。
