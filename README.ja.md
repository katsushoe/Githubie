# Githubie

[English](README.md) | [日本語](README.ja.md)

Githubieは、許可したLocal Git RepositoryとGitHub.comをMCP Clientから操作するWindows向けGatewayです。任意のGit Command、Repository URL、認証情報をAgentへ公開しません。[Buckettie](https://github.com/katsushoe/Buckettie)のGitHub版姉妹Projectです。
## Getting Started

2.0 MSIと同じ場所の `Install-UserHost.ps1` を、対象ユーザーの非昇格PowerShellから実行します。MSIの昇格と現在ユーザーの自動起動登録を同じ導入工程で確認した後、保護されたstdio中継でAIクライアントへ接続します。

```powershell
.\Install-UserHost.ps1 -MsiPath .\Githubie-2.0.0.0-win-x64.msi
githubie host status
githubie configure codex
# または: githubie configure claude
```

Repositoryを再登録し、Tokenを専用画面へ入力します。新規プロフィールへ旧Serviceデータを取り込みません。自動起動・接続移行・安全条件・復旧は[ユーザーホスト](USER_HOST.ja.md)、既存Tool仕様への導線は[MCPセットアップ](MCP_SETUP.ja.md)を参照してください。

## インストール

推奨配布形式はx64 MSIです。Portable ZIPと再現可能なSource Build手順も提供します。詳細は[インストール](INSTALLATION.ja.md)を参照してください。

開発者は.NET 9 SDKとGit for Windowsを用意します。

```powershell
dotnet build Githubie.slnx
dotnet test Githubie.slnx
```

## 設定

ユーザー別の設定・DB・CurrentUserで保護したToken・ログを、WindowsのLocalApplicationData既知フォルダー配下 `Akatsukisoft/Githubie` に保存します。バイナリは `C:\Githubie`、旧Serviceデータは復旧用に保全し、自動取り込みしません。[ユーザーホスト](USER_HOST.ja.md)と[設定schema](CONFIG.ja.md)を参照してください。

## 使用方法

設定、認証情報、診断、ユーザーホスト管理には`githubie.exe`を使います。MCP ClientにはRepository一覧、承認付きRepository登録、Repository Status／Description、GitHub Actions workflow起動・run取得、fetch／pull／push、承認付き履歴訂正、Branch、Issue、Pull Request、Tag、Release、Version取得の57個の型付きToolを公開します。詳細は[コマンド](COMMANDS.ja.md)を参照してください。

MCP Prompt `githubie_usage`は、Githubieを使用する目的、Repository IDの指定、状態確認から同期・変更へ進む基本手順、保護Branch、認証情報、履歴訂正の安全条件をAgent向けに返します。Promptに対応するMCP Clientでは、作業開始時の利用ガイドとして選択できます。

`github_repository_register`はlocal HTTPS remoteからGitHub Repositoryを導出し、同じユーザーセッションで承認・Token入力を受けた後、設定と保護Tokenを同時確定します。Tokenなし登録は明示選択時だけ許可し、取消・入力や保存の失敗では新規登録を残しません。ホストの再起動なしでAllowlistへ反映します。

Agentは`list_projects`で登録済みRepository IDから対象を選択し、各会話の`github_push`直前にも再確認します。未登録IDでpushした場合は、エラーに登録済みIDの候補一覧を含めます。

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

MCP Endpointはloopbackに加えて、起動ごとのユーザー資格とHost/Originを検証します。ホストとトレイは同じ非昇格ユーザープロセスで実行します。Personal Access Tokenを設定やMCP Clientへ保存せず、Repository登録承認後の最前面画面または`githubie.exe auth set`で登録してください。どちらのGUI経路でも、Token画面には入力前に登録元Project名と登録対象Repository URLを表示します。対象Repositoryだけに限定したFine-grained PATへ`Contents: Read and write`と`Pull requests: Read and write`を付与する方法を推奨します。詳細は[セキュリティ](SECURITY.ja.md)を参照してください。

## ライセンス

Githubieは[MIT License](LICENSE)で提供します。
