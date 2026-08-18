# 運用

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

設定はComposition Root（起動時1回）で読み込まれるため、動作中のホットリロードには対応していない。

## バックアップ対象

- `config\githubie.json`（Secretは含まれない）
- `data\secrets\`（DPAPI暗号化済みだが、LocalMachineスコープのため同一マシン以外へ復元しても復号できない。移設時は各RepositoryでToken再登録が必要）

`logs\`はバックアップ対象外（監査証跡として必要な期間だけ別途保全する）。
