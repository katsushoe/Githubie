# トラブルシューティング

[English](TROUBLESHOOTING.md) | [日本語](TROUBLESHOOTING.ja.md)

MCP Toolの`error.code`一覧と、原因・対処法を記載する。エラー形式は共通で以下。

```json
{
  "ok": false,
  "operation": "push",
  "repository": "hataori",
  "data": null,
  "error": { "code": "protected_branch", "message": "Direct push to a protected branch is not allowed." }
}
```

## Repository / Local Path関連

| error.code | 原因 | 対処 |
| --- | --- | --- |
| `repository_not_found` | `repository`パラメータが未登録のRepository ID | `githubie.exe repo list`で登録済みIDを確認する |
| `repository_not_allowed` | Repository IDの形式は正しいがAllowlistに存在しない | `githubie.json`の`repositories`にエントリを追加する（[CONFIG.md](CONFIG.md)） |
| `local_root_not_found` | 設定済み`local_root`がファイルシステム上に存在しない | パスを修正するか、リポジトリを再クローンする |
| `git_metadata_not_found` | `local_root`直下に`.git`がない | 正しいGitリポジトリのルートを指しているか確認する |
| `reparse_point_detected` | `local_root`の経路にsymlink/junctionが含まれる | Local Path Security上意図的に拒否している。実体パスを直接指定する |
| `remote_mismatch` | `git remote get-url`の結果が`github.com/<owner>/<repo>`と一致しない | ローカルRemote URLを設定値に合わせて修正する、または`github_owner`/`github_repo`を修正する |

## Git実行関連

| error.code | 原因 | 対処 |
| --- | --- | --- |
| `git_not_found` | `git`実行ファイルがPATHにない | Git for Windowsを導入しPATHを通す（`githubie.exe doctor`の`[NG] Git`で切り分け可能） |
| `git_failed` | Gitコマンドが非0で終了した | `repo status`や手動`git status`で状態を確認する |
| `timeout` | Gitコマンド（fetch/pull/push）が既定時間内に完了しなかった | ネットワーク状態を確認する。大きなリポジトリでは再試行する |
| `working_tree_dirty` | `require_clean_working_tree=true`でWorking Treeに未コミット変更がある | ローカルでcommitまたはstashしてから再実行する |
| `branch_not_allowed` | 現在のBranchが`direct_push_branches`/Pull対象Branchに含まれない | 許可Branchへ切り替えるか、設定を見直す |
| `protected_branch` | `protected_branches`に含まれるBranchへ直接Pushしようとした | PR経由（`github_pr_create` → `github_pr_merge`）で更新する。設定からの解除手段はMCP Toolに存在しない |
| `nothing_to_push` | ローカルCommitがRemoteより先行していない | 変更を先にcommitする |
| `non_fast_forward` | `github_pull`がFast-forwardできない | ローカルで手動rebase/mergeしてから再実行する（Version 1は自動merge/rebaseしない） |

## GitHub API関連

| error.code | 原因 | 対処 |
| --- | --- | --- |
| `branch_not_found` | `github_branch_get`等で指定したBranchがGitHub上に存在しない | Branch名を確認する（`github_branch_list`） |
| `authentication_failed` | Personal Access Tokenが未登録、または無効（GitHub側でRevoke済み等） | `githubie.exe auth set <repository>`で再登録し、`auth test`で確認する |
| `permission_denied` | Tokenは有効だが対象操作の権限が不足 | Fine-grained PATの`Contents` / `Pull requests`権限を`Read and write`に見直す（[SECURITY.md](SECURITY.md)） |
| `token_scope_missing` | 必要なScope/Permissionが不足 | `permission_denied`と同様にToken発行時の権限を見直す |
| `github_api_error` | 上記以外のGitHub API側エラー、または応答が想定外の形式 | ログの`error_code`とGitHub側のステータスを照合する。GitHub側の障害情報も確認する |
| `rate_limited` | Primary Rate Limitを超過（`x-ratelimit-remaining: 0`） | Rate Limitのリセットを待つ。頻繁に発生する場合は呼び出し頻度を見直す |
| `secondary_rate_limited` | Secondary Rate Limit（Abuse Detection）に抵触 | しばらく間隔を空けてから再実行する |
| `pull_request_not_found` | 指定した`pull_request_number`が存在しない | PR番号を確認する（`github_pr_list`） |
| `pull_request_not_open` | Mergeしようとした PR が既にclosed/merged | 対象PRの状態を確認する |
| `pull_request_not_mergeable` | GitHub側でConflict等によりmerge不可 | GitHub UI/CLIでConflictを解消してから再実行する |
| `pull_request_route_not_allowed` | Source/Destinationが`develop_branch → main_branch`以外 | 許可経路のPRのみ操作対象にする。経路自体を変えたい場合は設定の`develop_branch`/`main_branch`を見直す |
| `tag_not_found` | `github_tag_get`で指定したTagがGitHub上に存在しない | Tag名を確認する（`github_tag_list`） |
| `tag_invalid` | Tag名が`tag_pattern`に一致しない | 命名規則（既定は`^v[0-9]+\.[0-9]+\.[0-9]+.*$`）に沿ったTag名にする |
| `tag_already_exists` | 同名Tagが既に存在する | 別のTag名にする、または既存Tagを確認する（Tag削除Toolは公開していない） |
| `tag_target_not_allowed` | Tag対象Branchが`tag_target_branch`と異なる | 既定では`main` HEADのみ許可。設定を確認する |
| `network_error` | GitHub REST APIへの接続に失敗した | ネットワーク疎通・プロキシ設定を確認する |
| `timeout`（GitHub API） | GitHub REST API呼び出しが既定時間内に完了しなかった | 再実行する。継続する場合はGitHub側の状態を確認する |

## 起動・疎通に関する問題

| 症状 | 原因 | 対処 |
| --- | --- | --- |
| `Githubie.Server.exe`が起動直後に終了する | `githubie.json`が存在しない/不正、または非Windows環境（DPAPI要件） | 標準エラー出力に表示される内容を確認する。`config check`で事前検証する |
| `githubie.exe mcp status`が`[NG] MCP endpoint unreachable` | Serverが未起動、または`mcp_port`/`mcp_path`が設定と不一致 | `githubie.exe status`でService状態、`config show`でPort/Pathを確認する |
| CLIのヘルプ等で日本語が文字化けする | ターミナルの codepage がUTF-8以外 | `githubie.exe`はConsole出力をUTF-8に固定しているため、通常は発生しない。発生する場合はターミナル側のフォント/codepage設定を確認する |
| `auth set`が`[NG] IoError`で失敗する | MSIでインストールした直後の環境で、`data\secrets`ディレクトリのACLに管理者でも変更できない制限が残っている場合がある（[docs/adr/0013](docs/adr/0013-msi-directory-acl-grants.md)で修正済み） | v1.0.0.0以降のMSIを使用しているか確認する。旧バージョンの場合は管理者で`icacls "<install-root>\data\secrets" /inheritance:r /grant:r Administrators:F /grant:r SYSTEM:F`を実行してから再試行する |
| `auth set`直後の`auth test`が`AuthenticationFailed`になる | マスク入力へのペーストが二重に入ってTokenが破損した可能性がある（保存されたファイルサイズが想定より大きい） | `auth delete`後に`auth set`をやり直す。1.0.0.0以降は入力後に文字数が表示されるため、明らかに長すぎる場合は貼り付け方法を見直す |

## それでも解決しない場合

`githubie.exe logs`でログディレクトリを確認し、該当日付の`githubie-yyyyMMdd.log`から`error_code`と前後の文脈を確認する。監査ログにはSecretを含まないため、ログ自体の共有は問題ない。
