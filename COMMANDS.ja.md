# コマンド

[English](COMMANDS.md) | [日本語](COMMANDS.ja.md)

`githubie.exe`（管理CLI）と、MCP Serverが公開するTool一覧を記載する。

## グローバルオプション

| オプション | 説明 |
| --- | --- |
| `--config <path>` | `githubie.json`の場所を明示指定する。省略時は`<install-root>\config\githubie.json` |

## CLIコマンド

### 基本

| コマンド | 説明 |
| --- | --- |
| `githubie help` | コマンド一覧を表示する |
| `githubie version` | `githubie.exe`のバージョンを表示する |
| `githubie logs` | ログディレクトリのパスを表示する |

### 設定

| コマンド | 説明 |
| --- | --- |
| `githubie config check` | `githubie.json`の構文・値・各Repositoryの`local_root`/`.git`実在を検証し`[OK]`/`[NG]`を表示する |
| `githubie config show` | 読み込んだ設定内容（Port、Path、Repository一覧）を表示する |

### リポジトリ

| コマンド | 説明 |
| --- | --- |
| `githubie repo list` | 設定済みRepository ID一覧を表示する |
| `githubie repo status <repository>` | 指定Repositoryの`local_branch` / `local_head` / `ahead` / `behind` / `working_tree_clean`を実Gitコマンド経由で取得する |
| `githubie repo description get <repository>` | Repository Descriptionを取得する |
| `githubie repo description update <repository> <description>` | Descriptionを更新する。空文字列で削除する |
| `githubie repo rename <old> <new>` | Repository設定と暗号化Tokenを新IDへ一括移行する |

### 認証

| コマンド | 説明 |
| --- | --- |
| `githubie auth set <repository>` | Personal Access Tokenをマスク入力で受け取り、DPAPI暗号化してsecretsディレクトリへ保存する |
| `githubie auth test <repository>` | 保存済みTokenで`github_branch_list`相当のAPI呼び出しを行い、認証が通るか確認する |
| `githubie auth delete <repository>` | 保存済みTokenを削除する |

### MCP疎通確認

| コマンド | 説明 |
| --- | --- |
| `githubie mcp status` | MCP Endpointへ`initialize`リクエストを送り、応答を表示する |
| `githubie mcp tools` | MCP Endpointへ`tools/list`リクエストを送り、公開Tool定義を表示する |
| `githubie mcp call <tool> [<arguments-json>]` | 実行中Server経由で任意の公開Toolを呼び出す。引数省略時は`{}` |
| `githubie mcp call <tool> --file <path>` | ファイル内のJSON Objectを引数として公開Toolを呼び出す |
| `githubie mcp test` | `mcp status`と同じ疎通確認を行う |

いずれもStreamable HTTP Transportの要件に従い、`Accept: application/json, text/event-stream`を付与してリクエストする。

`mcp call`はJSON-RPC応答をJSONで出力し、通信・JSON-RPC・MCP・構造化Tool結果の失敗時は非0を返す。処理は実行中MCP Serverへ委譲するため、Allowlist・承認・監査・安全PolicyはMCP Client利用時と共通になる。Tool引数へSecretを含めてはならない。

### 診断

| コマンド | 説明 |
| --- | --- |
| `githubie doctor` | Configuration・Git実行可否・Service Composition・各Repositoryの認証Token/Git状態を`[OK]`/`[NG]`形式で診断する |

### サービス管理

| コマンド | 説明 |
| --- | --- |
| `githubie start` / `stop` / `restart` / `status` | Windows Service「Githubie」の起動・停止・再起動・状態確認（内部で`sc.exe`を実行） |
| `githubie service install` | Windows Serviceとして登録する（`binPath`は`Githubie.Server.exe <config-path>`、`start=auto`） |
| `githubie service uninstall` | Windows Serviceの登録を解除する |
| `githubie service status` | サービスの状態を確認する（`status`と同じ） |

## MCP Tool一覧

Tool名は`github_`を接頭辞とする（`get_version`のみ例外）。すべて`{ ok, operation, repository, data, error }`の構造化結果を返す（[TROUBLESHOOTING.md](TROUBLESHOOTING.md)にエラーコード一覧）。パラメータ名はStructured Outputと同様すべてsnake_caseで統一している。

### 読み取り専用（readOnlyHint = true）

| Tool | パラメータ | 説明 |
| --- | --- | --- |
| `github_repository_status` | `repository` | local/remote head、ahead/behind、working tree cleanを取得 |
| `github_repository_description_get` | `repository` | Repository Descriptionを取得 |
| `github_branch_list` | `repository` | Remote Branch一覧を取得 |
| `github_branch_get` | `repository`, `branch` | 指定Branchのhead commit sha等を取得 |
| `github_pr_list` | `repository`, `state?`, `source?`, `destination?` | Pull Request一覧を取得 |
| `github_pr_get` | `repository`, `pull_request_number` | Pull Request詳細を取得 |
| `github_pr_diff` | `repository`, `pull_request_number` | diff・変更統計を取得 |
| `github_pr_comment_list` | `repository`, `pull_request_number` | PR全体の会話コメント一覧を取得 |
| `github_tag_list` | `repository` | Tag一覧を取得 |
| `github_tag_get` | `repository`, `tag` | Tag詳細を取得 |
| `github_release_list` | `repository` | Releaseと成果物一覧を取得 |
| `github_release_get` | `repository`, `tag` | Tagに対応するReleaseと成果物詳細を取得 |
| `get_version` | — | Githubie Serverのバージョンを取得 |

### 変更操作（destructiveHint = true。MCP Client側のApproval対象）

| Tool | パラメータ | 説明 |
| --- | --- | --- |
| `github_repository_register` | `repository`, `local_root`, `remote?`, `develop_branch?`, `main_branch?` | Local Git remoteからGitHub接続先を導出し、対話Desktop承認後に登録 |
| `github_repository_update` | `repository`、Branch Policy項目 | 対話Desktop承認後にBranch Policyだけを更新。識別情報とPathは変更しない |
| `github_repository_unregister` | `repository` | Githubie設定と実行中Allowlistから登録解除。GitHub／Localのデータは削除しない |
| `github_repository_rename` | `old_repository`、`new_repository` | 設定と暗号化Tokenを一括移行し、失敗時は旧IDを維持する |
| `github_repository_description_update` | `repository`, `description` | Descriptionだけを更新。空文字列で削除、最大350文字 |
| `github_push` | `repository` | develop等へのGit push。Protected Branchへの直接Pushは`protected_branch`で拒否 |
| `github_pr_create` | `repository`, `title`, `description?`, `draft` | develop→mainのPRを作成（Source/Destinationは設定固定） |
| `github_pr_merge` | `repository`, `pull_request_number`, `merge_strategy?`, `message?` | PRをmerge。State==open、Source/Destinationが許可経路であることを検証 |
| `github_pr_close` | `repository`, `pull_request_number` | 未マージのPRをクローズ。クローズ済みの場合は状態を維持 |
| `github_pr_reopen` | `repository`, `pull_request_number` | 未マージのPRを再オープン。オープン済みの場合は状態を維持 |
| `github_pr_comment_create` | `repository`, `pull_request_number`, `body` | PR全体へ空でない会話コメントを追加 |
| `github_pr_review_approve` | `repository`, `pull_request_number`, `body?` | 開いているPRを承認。Review本文は任意 |
| `github_pr_review_request_changes` | `repository`, `pull_request_number`, `body` | 開いているPRへ変更を要求。Review本文は必須 |
| `github_tag_create` | `repository`, `tag`, `message?` | main HEADへAnnotated Tagを作成（Git Data APIの2段階呼び出し） |
| `github_tag_delete` | `repository`, `tag` | Policyに適合するTagを削除。存在しない場合は`tag_not_found` |
| `github_release_create` | `repository`, `tag`, `name`, `body?`, `draft`, `prerelease`, `assets` | 一致するdraftを再利用し、未登録成果物を追加後、全件成功時だけ公開 |
| `github_release_update` | `repository`, `release_id`, `name?`, `body?`, `draft?`, `prerelease?` | 明示指定したRelease項目だけを更新 |
| `github_release_asset_upload` | `repository`, `release_id`, `assets`, `replace_existing` | 許可成果物を最大10件追加。同名置換は`replace_existing=true`の場合のみ |

### 未分類（局所的な状態変更を伴うが破壊的操作ではない）

| Tool | パラメータ | 説明 |
| --- | --- | --- |
| `github_fetch` | `repository` | `git fetch`相当 |
| `github_pull` | `repository`, `branch` | `git pull --ff-only`相当。Fast-forward不能ならエラー |

## 監査ログ

すべてのTool呼び出しは`<install-root>\logs\githubie-yyyyMMdd.log`へ、`client` / `tool` / `repository` / `branch` / `pull_request_number` / `tag` / `result` / `duration_ms` / `error_code`の構造化行として記録する。Personal Access Token・Authorization Header・生エラーメッセージは記録しない。
