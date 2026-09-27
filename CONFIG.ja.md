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
| `provider_authentication` | 任意 | object | なし | Moyai Provider AssertionのTrust、Replay、Project対応設定。Serverを`--moyai`付きで起動した場合だけ使用し、既定の単体動作モードでは無視する |
| `repositories` | 必須 | object | なし | SQLiteへの初回移行用Entry。Database初期化後のJSON変更は再取込みしない |

## `provider_authentication`項目

`issuer`は空ではない`moyai:`識別子、`protocol_version`は`1`とする。`assertion_lifetime_seconds`は30～300、`clock_skew_seconds`は0～60とする。`trust_bundle_path`と`replay_database_path`には異なる絶対Pathを指定する。`projects`はGithubieのRepository IDを、それぞれ異なる空でないMoyai Project UUIDへ明示対応させる。Assertion Claimからこの管理者設定を作成・上書きしない。動作モードはこの項目の有無ではなくServerの起動引数で決まる。`--moyai`なしでは単体動作となりAssertionを検証しない。`--moyai`付きではこの項目が必須となる。Project対応のないRepository（`github_repository_create`で作成した直後など）があってもサービスは起動し、そのRepositoryへのMoyai要求は`auth_project_mismatch`で拒否し、直接呼び出しは`direct_connection`に従う。起動ログと`githubie config check --moyai`は警告として報告する。Moyai連携モードでは、`Authorization`と`X-Moyai-Operation-Id`のどちらも持たないRepository Tool呼び出しをローカル直接呼び出しとし、Moyai Consumer Contractの`direct_connection`に従って扱う。`--moyai`だけ（`read_only`、既定）では、Scopeが`repository.read`だけのToolを許可し、ほかのRepository Toolは`auth_assertion_missing`で拒否する。`--moyai --direct-unrestricted`（`unrestricted`）では、単体動作と同じくすべてのRepository Toolを、従来のRepository Policyと対話承認のもとで許可する。`require_assertion`（boolean、既定`false`）を`true`にすると、`--direct-unrestricted`がない限り読み取りの直接呼び出しも拒否する。どちらかのHeaderを持つ要求は常にMoyai要求として検証し、直接呼び出しへ切り替えない。`github_provider_capabilities`は`authentication.integration_mode`（`standalone`／`moyai`）と`authentication.direct_connection`（`read_only`／`unrestricted`）を返し、起動ログにも両方を記録する。

Trust BundleはMoyai公開署名鍵のsnake_case JSON配列、Replay DatabaseはServerが初期化する専用SQLite Fileである。PathまたはProject対応の変更後は`config check`とService再起動を行う。

## `repositories.<id>`項目

Repository IDはItogurumaのProject Inbox ID規則に合わせ、保存値を`^[a-z][a-z0-9]*$`、最大128文字とする。登録・名称変更の入力はInvariant lowercaseへ正規化し、検索時は大文字小文字を区別しない（`Githubie.Application.Repositories.RepositoryId`で検証）。MCP Agentはこの内部IDだけを指定し、`github_owner` / `github_repo` / `local_root`を自由指定できない。
Upgrade時は旧規則`^[A-Za-z0-9._-]+$`のIDから`.`、`_`、`-`を除去し、Invariant lowercaseへ変換して移行する。変換結果が不正または重複する場合は、Repositoryを暗黙選択せず起動を停止する。

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
| `commit_author_name` / `commit_author_email` | 任意 | stringの組 | なし | 登録Repository単位で保持するCommit作成者。両方同時に指定する |

Workflow Policyは`allowed_refs`を必須とし、input型は`string`／`boolean`／`integer`、`max_length`は1～4096とする。`max_concurrent`は1～10、`correlation_timeout_seconds`は1～120。`github_repository_update`による変更は対話承認を必要とする。

Pull Request経路（source→destination）は`develop_branch → main_branch`固定で、設定ファイルに個別項目はない。Agentや設定ファイルに自由な経路を指定させない設計上の判断による。

Repository登録とPolicyの正本は`<install-root>\data\githubie.db`とする。`github_repository_register`は既存Local Git Repositoryを実行中にSQLiteへ追加する。`github_owner`と`github_repo`は指定remote URLから導出し、対話Desktop承認を必要とする。指定remoteは`https://github.com/OWNER/REPOSITORY.git`形式に限定し、SSH形式は拒否する。Service再起動は不要である。

登録時に`commit_author_name`と`commit_author_email`を指定できる。省略時はRepositoryローカルの`user.name`と`user.email`が有効なら登録情報へ保存する。Service実行アカウントのGlobal Git設定は参照しない。既存登録には対話承認付きの`github_repository_update`で両方を設定できる。保存値がない既存登録はRepositoryローカルの設定を使用できる。どちらにも両方の値がなければ、`github_repository_commit`はFileをStageする前に`author_identity_missing`を返す。

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
