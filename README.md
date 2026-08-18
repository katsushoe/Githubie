# Githubie

Githubieは、許可したローカルGitリポジトリとGitHub.comをMCPクライアントから操作するWindows向けゲートウェイです。リポジトリAllowlist、ブランチ保護、監査ログ、DPAPIで保護したPersonal Access Tokenにより、AIクライアントへ必要な操作だけを公開します。

[Buckettie](https://github.com/katsushoe/Buckettie)（Bitbucket Cloud向け）の姉妹プロジェクトで、同じアーキテクチャ・Security原則をGitHub向けに踏襲しています。

## 状態

Phase 1のコア実装（Domain / Application / Infrastructure / MCP Server / 管理CLI）が完了しています。MSIインストーラー、統合テスト、ドキュメント一式は未整備です。

## 構成

```text
src/
├─ Githubie.Domain          Repository Policy等の純粋ドメインモデル
├─ Githubie.Application     Port(interface)層
├─ Githubie.Infrastructure  GitHub REST Client / Git実行 / DPAPI等の実装
├─ Githubie.AskPass         GIT_ASKPASS相当の一時Credential受け渡し
├─ Githubie.Server          MCP Server本体(Streamable HTTP)
└─ Githubie.Cli             管理CLI(githubie.exe)

tests/
└─ 各層に対応するテストプロジェクト(xUnit v3 + FluentAssertions + NSubstitute)
```

## ビルド

.NET 9 SDKとGit for Windowsを導入し、次を実行します。

```powershell
dotnet build Githubie.slnx
dotnet test Githubie.slnx
```

## 設定

`githubie.example.json`を`<install-root>\config\githubie.json`へコピーし、リポジトリを設定します。

```powershell
<install-root>\bin\githubie.exe config check
<install-root>\bin\githubie.exe auth set <repository-id>
<install-root>\bin\githubie.exe service install
<install-root>\bin\githubie.exe start
<install-root>\bin\githubie.exe doctor
```

MCPクライアントへ`http://127.0.0.1:45460/mcp`を登録します。

## 公開するMCP Tool

```text
github_repository_status

github_fetch
github_pull
github_push

github_branch_list
github_branch_get

github_pr_list
github_pr_get
github_pr_diff
github_pr_create
github_pr_merge

github_tag_list
github_tag_get
github_tag_create
```

## セキュリティ

MCP EndpointはLoopbackだけで待ち受けます。Personal Access Tokenを設定ファイルやMCPクライアント設定へ保存しないでください。認証はFine-grained Personal Access Token（対象リポジトリへの`Contents: Read and write` / `Pull requests: Read and write`）を推奨します。

## ライセンス

Githubieは[MIT License](LICENSE)で提供します。
