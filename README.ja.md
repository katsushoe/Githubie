# Githubie

候補2.1.4.0は固定SHAでのPR元ブランチ新規公開とPR作成・結果照合を追加します。実GitHubとConsumer相互受入は別工程です。 [契約](docs/specifications/pr-source-publication.ja.md)。

開発候補に[GitHub側のブランチ保護操作](docs/specifications/github-branch-protection.ja.md)を追加中です。候補の検証、実機導入、Moyai相互受入は別工程です。

候補2.1.1.0はPR読取・マージ能力、期待source SHAによる条件付きマージ、結果不明時の照合に対応します。[PRマージ契約](docs/specifications/pr-merge-provider.ja.md)を参照してください。候補の検証、Moyai実相互受入、Windows実機導入は別工程です。

[English](README.md) | [日本語](README.ja.md)

`<install-root>` はユーザーが選択した実際のインストール先です（既定は `C:\Githubie`）。設定は実行ファイルの `bin` の親にある `config\githubie.json` 一つだけを使用します。

Githubieは、許可したLocal Git RepositoryとGitHub.comをMCP Clientから操作するWindows向けGatewayです。任意のGit Command、Repository URL、認証情報をAgentへ公開しません。[Buckettie](https://github.com/katsushoe/Buckettie)のGitHub版姉妹Projectです。
## Getting Started

2.0 MSIと同じ場所の `Install-UserHost.ps1` を、対象ユーザーの非昇格PowerShellから実行します。MSIの昇格と現在ユーザーの自動起動登録を同じ導入工程で確認した後、保護されたstdio中継でAIクライアントへ接続します。

```powershell
.\Install-UserHost.ps1 -MsiPath .\Githubie-2.1.0.0-win-x64.msi
githubie host status
githubie configure codex
# または: githubie configure claude
```

Repositoryを再登録し、Tokenを専用画面へ入力します。新規プロフィールへ旧Serviceデータを取り込みません。自動起動・接続移行・安全条件・復旧は[ユーザーホスト](USER_HOST.ja.md)、既存Tool仕様への導線は[MCPセットアップ](MCP_SETUP.ja.md)を参照してください。

`githubiemcp` は起動済みGithubieへ接続するクライアントです。`githubiemcp --help` で操作を確認できます。MSIとZIPに `githubie` と並べて収録します。[コマンド](COMMANDS.ja.md)を参照してください。

## インストール

推奨配布形式はx64 MSIです。Portable ZIPと再現可能なSource Build手順も提供します。詳細は[インストール](INSTALLATION.ja.md)を参照してください。

開発者は.NET 9 SDKとGit for Windowsを用意します。

```powershell
dotnet build Githubie.slnx
dotnet test Githubie.slnx
```

## 設定

本体設定・自動起動・連携モードは、唯一の `<install-root>\config\githubie.json` から読みます。DB・CurrentUserで保護したToken・識別情報・接続情報・ログはユーザー別のLocalApplicationData/Akatsukisoft/Githubieに保存します。旧共有DB・Tokenは自動取り込みしません。[ユーザーホスト](USER_HOST.ja.md)と[設定schema](CONFIG.ja.md)を参照してください。

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

## 同一Windowsユーザー認証（候補2.1.0.0）

唯一のJSONの `user_host.authentication_mode` に `windows_owner` を明示すると、所有者認証済みユーザーホスト接続でMoyai/nativeを同じWindowsユーザーとして扱います。省略値 `legacy` は従来動作を保持します。新規MSIの既定JSONは `windows_owner` を明示しますが、既存JSONは上書きしません。このモードは `--user-host` 専用で、旧Serviceでは拒否します。

全HTTP要求にschema1 discoveryの `X-Githubie-Session` と `X-Githubie-Generation` が必要です。ES256 Assertion・Issuer鍵・Project署名scope・legacy tokenは必須にしません。旧Moyai/direct設定とtrust資産は保持しますが、このモードの認可に使用しません。Repository/branch制限・本人承認・外部サービス資格・監査は維持します。監査主体はWindows所有SIDです。

Consumerは版と `github_provider_capabilities.authentication.owner_authentication` の明示契約・対象tool名を確認してから操作します。通常のcapabilityだけでは署名不要と判断できません。ownerモードへAuthorizationを渡すと403/`user_host_auth_mode_mismatch`、所有者資格・世代等の拒否は既存の403/`user_host_owner_rejected`です。別経路fallbackと結果不明mutationの自動再送を行いません。

正確なwire・能力フィールド・対象操作・エラーは[Windows owner認証v1](docs/specifications/windows-owner-authentication-v1.md)、設計判断は[ADR0042](docs/adr/0042-windows-owner-authentication.md)を参照してください。現在は契約提案・候補の段階です。Moyai Consumer合意/相互受入と通常環境切替は別工程で、実装候補を稼働中の設定へ自動適用しません。既存鍵・DB・trustを削除しないでください。
