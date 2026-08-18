# インストール

MSIインストーラーは未整備のため（[DOCUMENTS.md](DOCUMENTS.md)参照）、Version 1では`dotnet publish`によるバイナリ配置を導入手順とする。

## 前提

- Windows 10/11 または Windows Server（DPAPI / Windows Service / `sc.exe`を使用するためWindows専用）
- .NET 9 SDK
- Git for Windows（system PATHに`git`が通っていること）

## 手順

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
