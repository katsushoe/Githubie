# Githubie

[English](README.md) | [日本語](README.ja.md)

Githubieは、許可したLocal Git RepositoryとGitHub.comをMCP Clientから操作するWindows向けGatewayです。任意のGit Command、Repository URL、認証情報をAgentへ公開しません。[Buckettie](https://github.com/katsushoe/Buckettie)のGitHub版姉妹Projectです。

## はじめに

MSIをInstallし、`githubie.example.json`を`<install-root>\config\githubie.json`へCopyして、Repositoryを1件以上設定します。

```powershell
githubie.exe config check
githubie.exe auth set <repository-id>
githubie.exe start
githubie.exe doctor
```

MCP Clientへ`http://127.0.0.1:45460/mcp`を登録します。Client別の手順は[MCPセットアップ](MCP_SETUP.ja.md)を参照してください。

## インストール

推奨配布形式はx64 MSIです。Portable ZIPと再現可能なSource Build手順も提供します。詳細は[インストール](INSTALLATION.ja.md)を参照してください。

開発者は.NET 9 SDKとGit for Windowsを用意します。

```powershell
dotnet build Githubie.slnx
dotnet test Githubie.slnx
```

## 設定

既定では`<install-root>\config\githubie.json`を読み込みます。Repository IDを、固定したGitHub Owner／Repository、Local Root、Branch Policyへ対応付けます。詳細は[設定](CONFIG.ja.md)を参照してください。

## 使用方法

設定、認証情報、診断、Windows Service管理には`githubie.exe`を使います。MCP Clientには承認付きRepository登録、Repository Status／Description、GitHub Actions workflow起動・run取得、fetch／pull／push、承認付き履歴訂正、Branch、Pull Request、Tag、Release、Version取得の37個の型付きToolを公開します。詳細は[コマンド](COMMANDS.ja.md)を参照してください。

`github_repository_register`は既存Local Repositoryのremote URLからGitHub Owner／Repositoryを導出し、対話Desktopでの承認後に設定を保存して、Service再起動なしで実行中Allowlistへ反映します。

Release Toolは一覧・詳細取得・更新・成果物の再試行可能な登録に対応します。成果物はRepository配下のMSI／ZIP／SHA-256／`SHA256SUMS.txt`／PowerShellに限定し、同名置換には明示指定が必要です。

`github_history_rewrite`は公開済みbranch／tagを訂正する専用Toolです。必ずdry-runで旧SHA・新SHA・拒否理由を確認し、実更新では対話Desktop上の承認を行います。全refは`--atomic`とrefごとの`--force-with-lease`で一括更新され、通常の`github_push`の保護方針は変わりません。実行前にmirrorまたはbackup refを保存し、復旧時は保存SHAを指定して同じ手順を逆向きに実施してください。

## ドキュメント

- [インストール](INSTALLATION.ja.md)
- [設定](CONFIG.ja.md)
- [MCPセットアップ](MCP_SETUP.ja.md)
- [コマンド](COMMANDS.ja.md)
- [運用](OPERATIONS.ja.md)
- [セキュリティ](SECURITY.ja.md)
- [トラブルシューティング](TROUBLESHOOTING.ja.md)
- [パッケージ構成](PACKAGES.ja.md)
- [リリース](RELEASE.ja.md)
- [Architecture Decision Records](docs/adr/README.md)（英語）

## セキュリティ

MCP EndpointはLoopbackだけで待ち受けます。Personal Access Tokenを設定やMCP Clientへ保存せず、`githubie.exe auth set`で登録してください。対象Repositoryだけに限定したFine-grained PATへ`Contents: Read and write`と`Pull requests: Read and write`を付与する方法を推奨します。詳細は[セキュリティ](SECURITY.ja.md)を参照してください。

## ライセンス

Githubieは[MIT License](LICENSE)で提供します。
