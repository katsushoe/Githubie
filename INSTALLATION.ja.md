# インストール

[English](INSTALLATION.md) | [日本語](INSTALLATION.ja.md)

Githubieではx64 MSI、Portable ZIP、ソースからのBuildを利用できます。推奨する配布形式はMSIです。初回のMSI Install／Major Upgrade／Uninstall検証はVersion `1.0.0.0`で完了しています。Version `1.8.3.2`では、単独の`githubie auth set`からToken登録画面を開く場合も登録済みRepository URLを解決して表示し、`C:\Githubie`へのMSI Install、Install済みVersion、Windows Service自動起動、CLI設定検査、登録済みProjectの保持、実PATによるHTTPS pull／tag push経路、手動Uninstall／ReinstallをWindows実機で検証しました。UninstallでServiceを消去しながら設定とDataを保持し、ReinstallでServiceを再作成・起動することを確認しています。Version `1.8.4.0`では`C:\Githubie`へのUpgrade Install、CLI／MCP Version、Service自動起動、設定検査、登録済み9 Projectの保持、Issue一覧・詳細Toolの公開を実機検証しました。`logs`には組み込みUsersの読み取り・書き込み・走査権限を付与し、一般ユーザーの管理CLIが昇格なしで監査ログを追記できるようにします。Secret ACLは別に制限します。

Version `1.8.5.0`では`C:\Githubie`へのUpgrade Install、CLI／MCP Version、Service自動起動、設定検査、登録済み9 Projectの保持、初回Commit前のRepository状態取得を実機検証しました。

Version `1.8.6.3`では`C:\Githubie`へのUpgrade Install、CLI／MCP Version、Service自動起動、外部`ready`状態、読み取り専用doctor Composition、登録済み9 Projectの保持、初回Commit前のRepository差分取得を実機検証しました。RepositoryのToken未登録は独立したdoctor失敗として扱われます。

Version `1.8.8.0`では`C:\Githubie`へのUpgrade Install、Install済みCLI／File Version `1.8.8.0`、Service自動起動、設定検査、登録済み4 Projectの保持を実機検証しました。MSIのSHA-256は`A62C4D1305CFBE9F37E26257FFF219B924AFE4B6D4EDCC9A10E14539274085BC`です。

Version `1.8.8.1`では`C:\Githubie`へのUpgrade Install、Install済みCLI／File Version `1.8.8.1`、Service自動起動、設定検査、登録済み9 Projectの保持を実機検証しました。MSIのSHA-256は`A3F4B6E12CB93D4346CDE2662C9E928E0846AB12B881E0237FE5B977D99142B6`です。

Version `1.8.8.2`では`C:\Githubie`へのUpgrade Install、Install済みCLI／MCP Version `1.8.8.2`、Service自動起動、設定検査、登録済み9 Projectの保持を実機検証しました。MSIのSHA-256は`6C9FB9D4BC5AB3E44DF1EAC203D3D78E9365B22D64FE2900C0E296824172E8F3`です。

Version `1.8.8.3`では`C:\Githubie`へのUpgrade Install、Install済みCLI／MCP／File Version `1.8.8.3`、Service自動起動、設定検査、登録済み9 Projectの保持を実機検証しました。MSIのSHA-256は`213FCC5B8C97D3EB2CB7DF2CB593875B9FE83CE722BA3DA05F5DC62D56E226BF`です。

Version `1.8.8.4`では`C:\Githubie`へのUpgrade Install、Install済みCLI／MCP／File Version `1.8.8.4`、Service自動起動、設定検査、登録済み9 Projectの保持を実機検証しました。MSIのSHA-256は`9B833E22899D66988F3B7834E3EC491A0B5AEAA79032DF922E14B6AE05AEF694`です。

Version `1.8.8.5`では`C:\Githubie`へのUpgrade Install、Install済みCLI／MCP／File Version `1.8.8.5`、Service自動起動、設定検査、登録済み9 Projectの保持、Token Dialogの要求元Project名と要求先Repository URL表示を実機検証しました。MSIのSHA-256は`47E355E1A594B800AF1DD6F0BB9C0F828C6B05122D4DE78DC3F5EF8520F80FEB`です。

Version `1.8.9.1`では、`C:\Githubie`へのUpgrade Installを実機で検証しました。検証項目は、Install済みCLI／MCP／File Version `1.8.9.1`、Service自動起動、`provider_authentication`を含む設定検査、同梱`System.Text.Json`のAssembly Version `10.0.0.0`、認証のないRepository Tool呼び出しの拒否です。先行版`1.8.9.0`の最初のMSIは、共有Publish先に古い`System.Text.Json`が残ったため起動に失敗しました。現在の`Build-Msi.ps1`はProjectごとに個別Publishし、Serverが要求するAssembly Versionを検査します。MSIのSHA-256は`1D660C848E6C3AC23453D454C09B6FDAB1C8E7B1756638DA30E29596F8EF1115`です。

Version `1.8.9.2`では、既存の`provider_authentication`設定を変更せずに`C:\Githubie`へUpgrade Installし、Install済みMCP／File Version `1.8.9.2`、Service自動起動、設定検査、Moyai Headerを持たない単体の直接`github_repository_status`呼び出しの成功を実機で検証しました。MSIのSHA-256は`618950F593D0330B54E7B1138605994EA4A2B3F236CB6FC441BCED84BCDBE56B`です。

Version `1.8.9.3`では、`MOYAI`プロパティを指定せずに`C:\Githubie`へUpgrade Installしました。実機では次を検証しました。Serviceが`--moyai`なしの`Githubie.Server.exe "<config>"`として登録されること、起動ログに`started in standalone mode`が出力されること、MCP／File Versionが`1.8.9.3`であること、`config check`と`config check --moyai`がともに合格すること、直接の`github_repository_status`呼び出しが成功すること。MSIのSHA-256は`49562B98850D41933E7B0AB57C54859F89872240C7ADD1D246CCA0BD9506D2E6`です。`MOYAI=1`でのInstallは実機で未検証です。

続いて、リリース版の`1.8.9.3` MSI（SHA-256 `5ED7BB9473BA4A847896F0C81CF64D5B5A87ADDB526F100DEACBB38ACAA9D929`）を`MOYAI=1`でUpgrade Installしました。Serviceは`--moyai`付きで登録され、レジストリに`MoyaiIntegration=1`が保存され、起動ログに`started in Moyai integration mode`が出力されました。その後、Moyai 1.3.3.0経由でKotodamaの`provider-capabilities`と`repository-status`、Githubieの`repository-status`と`branch-list`が成功し、偽のAssertionは`auth_assertion_invalid`で拒否されました。

## 前提

- Windows 10/11 または Windows Server（DPAPI / Windows Service / `sc.exe`を使用するためWindows専用）
- .NET 9 SDK
- Git for Windows（system PATHに`git`が通っていること）

## 手順

実機の標準インストール先は、MSI／Portable ZIP／ソースBuildのいずれも`C:\Githubie`とする。

### 1. ソース取得とテスト

```powershell
git clone https://github.com/katsushoe/Githubie.git
Set-Location Githubie
dotnet test Githubie.slnx
```

### 2. Publish

`Githubie.Server` / `Githubie.Cli` / `Githubie.AskPass`の3つを同一の`bin`ディレクトリへpublishする。

```powershell
$InstallRoot = "C:\Githubie"
dotnet publish src\Githubie.Server\Githubie.Server.csproj  -c Release -o "$InstallRoot\bin"
dotnet publish src\Githubie.Cli\Githubie.Cli.csproj         -c Release -o "$InstallRoot\bin"
dotnet publish src\Githubie.AskPass\Githubie.AskPass.csproj -c Release -o "$InstallRoot\bin"
```

`bin`配下に以下が生成される。

```text
bin/
├─ githubie.exe            管理CLI
├─ Githubie.Server.exe     MCP Server
└─ Githubie.AskPass.exe    GIT_ASKPASS実行ファイル
```

### 3. 設定配置

```powershell
New-Item -ItemType Directory -Force "$InstallRoot\config"
Copy-Item githubie.example.json "$InstallRoot\config\githubie.json"
notepad "$InstallRoot\config\githubie.json"
```

設定項目は[CONFIG.md](CONFIG.md)を参照する。`<install-root>`配下には次のディレクトリが実行時に自動生成される。

```text
<install-root>/
├─ bin/
├─ config/
│   └─ githubie.json
├─ logs/
│   └─ githubie-yyyyMMdd.log
└─ data/
    └─ secrets/
        └─ <repository-id>.token   （DPAPI暗号化、ACLで現在ユーザー等に限定）
```

### 4. 検証と起動

```powershell
Set-Location "$InstallRoot\bin"
.\githubie.exe config check
.\githubie.exe auth set <repository-id>
.\githubie.exe service install
.\githubie.exe start
.\githubie.exe doctor
```

`auth set`は既定で画面中央の最前面Token入力ダイアログを表示する。端末でマスク入力する場合は`auth set <repository-id> --console`を使用する。

ZIP/手動配置のためWindows Serviceとしての自動起動は`service install`を実行するまで有効にならない。フォアグラウンド確認だけなら`.\Githubie.Server.exe`を直接実行してもよい（Ctrl+Cで停止）。

### 5. MCPクライアント登録

[MCP_SETUP.md](MCP_SETUP.md)を参照する。

## アンインストール

```powershell
.\githubie.exe stop
.\githubie.exe service uninstall
Remove-Item -Recurse -Force "$InstallRoot"
```

`data\secrets`配下のToken本体もディレクトリ削除で失われる。事前にGitHub側でToken自体を無効化（Revoke）しておくことを推奨する。
