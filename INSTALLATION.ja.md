# インストール

[English](INSTALLATION.md) | [日本語](INSTALLATION.ja.md)

Githubieではx64 MSI、Portable ZIP、ソースからのBuildを利用できます。推奨する配布形式はMSIです。初回のMSI Install／Major Upgrade／Uninstall検証はVersion `1.0.0.0`で完了しています。Version `1.8.3.2`では、単独の`githubie auth set`からToken登録画面を開く場合も登録済みRepository URLを解決して表示し、`C:\Githubie`へのMSI Install、Install済みVersion、Windows Service自動起動、CLI設定検査、登録済みProjectの保持、実PATによるHTTPS pull／tag push経路、手動Uninstall／ReinstallをWindows実機で検証しました。UninstallでServiceを消去しながら設定とDataを保持し、ReinstallでServiceを再作成・起動することを確認しています。Version `1.8.4.0`では`C:\Githubie`へのUpgrade Install、CLI／MCP Version、Service自動起動、設定検査、登録済み9 Projectの保持、Issue一覧・詳細Toolの公開を実機検証しました。`logs`には組み込みUsersの読み取り・書き込み・走査権限を付与し、一般ユーザーの管理CLIが昇格なしで監査ログを追記できるようにします。Secret ACLは別に制限します。

Version `1.8.5.0`では`C:\Githubie`へのUpgrade Install、CLI／MCP Version、Service自動起動、設定検査、登録済み9 Projectの保持、初回Commit前のRepository状態取得を実機検証しました。

Version `1.8.6.3`では`C:\Githubie`へのUpgrade Install、CLI／MCP Version、Service自動起動、外部`ready`状態、読み取り専用doctor Composition、登録済み9 Projectの保持、初回Commit前のRepository差分取得を実機検証しました。RepositoryのToken未登録は独立したdoctor失敗として扱われます。

Version `1.8.8.0`では`C:\Githubie`へのUpgrade Install、Install済みCLI／File Version `1.8.8.0`、Service自動起動、設定検査、登録済み4 Projectの保持を実機検証しました。MSIのSHA-256は`A62C4D1305CFBE9F37E26257FFF219B924AFE4B6D4EDCC9A10E14539274085BC`です。

Version `1.8.8.1`では`C:\Githubie`へのUpgrade Install、Install済みCLI／File Version `1.8.8.1`、Service自動起動、設定検査、登録済み9 Projectの保持を実機検証しました。MSIのSHA-256は`A3F4B6E12CB93D4346CDE2662C9E928E0846AB12B881E0237FE5B977D99142B6`です。

Version `1.8.8.2`では`C:\Githubie`へのUpgrade Install、Install済みCLI／MCP Version `1.8.8.2`、Service自動起動、設定検査、登録済み9 Projectの保持を実機検証しました。MSIのSHA-256は`6C9FB9D4BC5AB3E44DF1EAC203D3D78E9365B22D64FE2900C0E296824172E8F3`です。

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
