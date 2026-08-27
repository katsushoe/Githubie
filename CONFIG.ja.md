# 設定

[English](CONFIG.md) | [日本語](CONFIG.ja.md)

`githubie.json`の全項目と検証ルールを記載する。読み込みは`Githubie.Infrastructure.Configuration.JsonGithubieOptionsLoader`が行い、プロパティ名はすべてsnake_caseで統一する。JSON中の未知プロパティ・コメント・末尾カンマは拒否する。

サンプルは[githubie.example.json](githubie.example.json)を参照する。

## ファイル配置

既定では`<install-root>\config\githubie.json`を読み込む。`githubie.exe` / `Githubie.Server.exe`のいずれも第1引数、または`githubie.exe`は`--config <path>`で明示指定できる。

```powershell
Githubie.Server.exe C:\path\to\githubie.json
githubie.exe --config C:\path\to\githubie.json config check
```

## ルート項目

| キー | 必須 | 型 | 既定値 | 説明 |
| --- | --- | --- | --- | --- |
| `mcp_port` | 必須 | integer（1～65535） | なし | MCP Endpointの待受Port。付属Sampleは`45460`を指定する |
| `mcp_path` | 必須 | string（`/`始まり） | なし | MCP EndpointのPath。付属Sampleは`/mcp`を指定する |
| `repositories` | 必須 | object | なし | SQLiteへの初回移行用Entry。Database初期化後のJSON変更は再取込みしない |

## `repositories.<id>`項目

Repository IDは`^[A-Za-z0-9._-]+$`、最大128文字（`Githubie.Application.Repositories.RepositoryId`で検証）。MCP Agentはこの内部IDだけを指定し、`github_owner` / `github_repo` / `local_root`を自由指定できない。

| キー | 必須 | 型 | 既定値 | 説明 |
| --- | --- | --- | --- | --- |
| `github_owner` | 必須 | string | なし | 空ではないGitHub User名またはOrganization名 |
| `github_repo` | 必須 | string | なし | 空ではないGitHub Repository名 |
| `local_root` | 必須 | string | なし | 実在するLocal Repository Root。`.git`を必要とし、reparse pointを拒否する |
| `remote` | 必須 | string | なし | Git操作に使う固定Remote名。Sampleは`origin` |
| `develop_branch` | 必須 | string | なし | 許可するPR経路のSource Branch |
| `main_branch` | 必須 | string | なし | 許可するPR経路のDestination Branch |
| `direct_push_branches` | 必須 | string[] | なし | `github_push`を許可するBranch一覧 |
| `pull_branches` | 必須 | string[] | なし | `github_pull`を許可するBranch一覧 |
| `protected_branches` | 必須 | string[] | なし | Direct Pushを拒否するBranch一覧 |
| `tag_target_branch` | 必須 | string | なし | Tag作成を許可するTarget Branch |
| `tag_pattern` | 必須 | string | なし | Tag名を検証する有効な正規表現 |
| `merge_method` | 必須 | string | なし | `merge`、`squash`、`rebase`のいずれか |
| `require_clean_working_tree` | 必須 | boolean | なし | `true`なら未Commit変更があるPushを拒否する |
| `workflows` | 任意 | object | `{}` | 起動可能workflowごとの許可ref、input schema、同時実行数、run関連付けtimeout |

Workflow Policyは`allowed_refs`を必須とし、input型は`string`／`boolean`／`integer`、`max_length`は1～4096とする。`max_concurrent`は1～10、`correlation_timeout_seconds`は1～120。`github_repository_update`による変更は対話承認を必要とする。

Pull Request経路（source→destination）は`develop_branch → main_branch`固定で、設定ファイルに個別項目はない。Agentや設定ファイルに自由な経路を指定させない設計上の判断による。

Repository登録とPolicyの正本は`<install-root>\data\githubie.db`とする。`github_repository_register`は既存Local Git Repositoryを実行中にSQLiteへ追加する。`github_owner`と`github_repo`は指定remote URLから導出し、対話Desktop承認を必要とする。指定remoteは`https://github.com/OWNER/REPOSITORY.git`形式に限定し、SSH形式は拒否する。Service再起動は不要である。

更新後の初回起動時に、検証済みの`repositories` Entryをトランザクション内で取り込む。移行Marker作成後は古いJSONによるDatabaseの上書きを行わない。移行とBackupの確認まではJSONを保持し、その後の変更にはRepository管理操作を使用する。

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
- Workflow Policy、ref、input schema、同時実行数、timeoutが不正（`InvalidWorkflowPolicy`）

`githubie.exe config check`はJSONと初回移行用Entryについて、`local_root`と`.git`の実在も確認する。実際に有効なSQLite登録は`githubie.exe repo list`および`doctor`で確認する。

## Personal Access Token

Tokenは`githubie.json`に含めない。`githubie.exe auth set <repository-id>`でDPAPI（LocalMachineスコープ）暗号化のうえ`<install-root>\data\secrets\<repository-id>.token`へ1ファイル/Repositoryで保存する。詳細は[SECURITY.md](SECURITY.md)を参照する。
