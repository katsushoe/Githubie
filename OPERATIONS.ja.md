# 運用

[English](OPERATIONS.md) | [日本語](OPERATIONS.ja.md)

## Repository登録

未使用のRepository IDと既存Local Repositoryの絶対Pathを`github_repository_register`へ渡す。GithubieはLocal remoteからGitHub接続先を導出し、Owner／Repository、Local Root、remote、Branch経路を対話Desktopへ表示する。承認後、Network操作にTokenが必要なら`githubie auth set <repository>`を実行し、`github_repository_status`と`github_fetch`で確認する。

## 履歴訂正

履歴訂正前に対象Repositoryのmirrorまたは`refs/backup/*`を保存する。`github_history_rewrite`を`dry_run=true`で実行し、全refのremote SHA、local SHA、拒否理由が想定どおりであることを確認してから実更新する。実更新は対話承認後にremote SHAを再検証し、atomic非対応、lease競合、権限不足では全体を中止する。復旧時は保存したSHAを新しいlocal SHAとして同じdry-run／承認手順を逆向きに実施する。

## GitHub Release

先にmain HEADへバージョンTagを作成し、Repository local root配下へ許可成果物を生成する。MSI、ZIP、`.sha256`、`SHA256SUMS.txt`、配布用`.ps1`を指定できる。`github_release_create`はdraftへ未登録成果物だけを追加し、全件成功後にのみ公開する。同一入力の再試行では一致するdraftを再利用する。状態確認は`github_release_list`／`get`、情報更新は`github_release_update`、後からの追加は`github_release_asset_upload`を使い、同名置換時だけ`replace_existing=true`を明示する。

## サービス起動・停止

```powershell
githubie.exe start
githubie.exe stop
githubie.exe restart
githubie.exe status
```

内部では`sc.exe start|stop|query Githubie`を実行する。Windows Serviceとして常駐させる場合は事前に`githubie.exe service install`を実行しておく（[INSTALLATION.md](INSTALLATION.md)）。

## ログ

```powershell
githubie.exe logs
```

でログディレクトリのパスを表示する。ファイルは`<install-root>\logs\githubie-yyyyMMdd.log`に日次ローテーションされる。監査ログ（Tool呼び出し結果）とアプリケーションの警告・エラーのみを記録し、ASP.NET Core内部の詳細診断ログ（Information以下）は既定で抑制している。

監査ログの行フォーマット:

```text
2026-08-18T05:32:08Z [Information] Githubie.Server.GithubieAuditLogger client=mcp tool=github_push repository=hataori branch=develop pull_request_number= tag= result=success duration_ms=1174 error_code=
```

## 診断

```powershell
githubie.exe doctor
```

Configuration・Git実行可否・Service Composition・Repository単位のToken有無/Git状態を`[OK]`/`[NG]`で一覧表示する。`[NG]`が出た場合は該当Repositoryの`config check`や`auth test`で切り分ける。

```powershell
githubie.exe config check
githubie.exe repo status <repository>
githubie.exe auth test <repository>
```

## Personal Access Tokenのローテーション

GitHub側でTokenを再発行した場合は、Githubie側も再登録する。

```powershell
githubie.exe auth set <repository>
```

既存Tokenは新しい値で上書きされる（同一ファイルへのatomic置換）。旧Tokenの明示的な削除は不要だが、GitHub側でRevokeされていれば以後の呼び出しは`authentication_failed`になる。

Repositoryの登録自体を止める場合:

```powershell
githubie.exe auth delete <repository>
```

## Workflow起動

対話承認付きRepository更新でworkflowファイル名／ID、許可ref、input schemaを設定する。`github_workflow_dispatch`が返したrun IDを保持し、`github_workflow_run_get`で完了まで確認する。関連付け失敗時は`github_workflow_run_list`で既存runを確認し、曖昧性が解消するまで再起動しない。

## MCP Endpointの疎通確認

```powershell
githubie.exe mcp status
githubie.exe mcp tools
```

Serverが起動していない、またはPort/Path設定が不一致だと`[NG] MCP endpoint unreachable`になる。`config show`で現在のPort/Pathを確認する。

## リポジトリ設定の変更

`githubie.json`を編集した後は、Windows Serviceを再起動して設定を反映する。

```powershell
githubie.exe config check
githubie.exe restart
```

手動編集した設定はComposition Rootで起動時に読み込む。`github_repository_register`で追加したEntryだけは即時反映される。

## バックアップ対象

- `config\githubie.json`（Secretは含まれない）
- `data\secrets\`（DPAPI暗号化済みだが、LocalMachineスコープのため同一マシン以外へ復元しても復号できない。移設時は各RepositoryでToken再登録が必要）

`logs\`はバックアップ対象外（監査証跡として必要な期間だけ別途保全する）。
