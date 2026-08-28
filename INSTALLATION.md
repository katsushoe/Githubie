# Installation

[English](INSTALLATION.md) | [日本語](INSTALLATION.ja.md)

Githubie supports an x64 MSI, a portable ZIP, and source builds. Windows 10/11 or Windows Server and Git for Windows are required.

## MSI Installation

Use the versioned `Githubie-<version>-win-x64.msi` package. It installs Githubie under `C:\Githubie`, creates the required runtime directories and ACLs, and registers the Windows Service. The `logs` directory grants built-in Users read, write, and traverse access so the management CLI can append audit records without elevation; secrets remain restricted separately. After installation:

```powershell
Copy-Item githubie.example.json "C:\Githubie\config\githubie.json"
githubie.exe config check
githubie.exe auth set <repository-id>
githubie.exe start
githubie.exe doctor
```

`auth set` opens a centered foreground Token dialog by default. Use `auth set <repository-id> --console` for masked terminal input.

MSI install, major upgrade, and uninstall have been validated for version `1.0.0.0`.

## Portable ZIP

Extract the ZIP to a fixed directory, copy `githubie.example.json` to `config\githubie.json`, and run `githubie.exe service install` before starting the service. The ZIP does not register the service automatically.

## Source Build

Install the .NET 9 SDK and Git for Windows, then run:

```powershell
git clone https://github.com/katsushoe/Githubie.git
Set-Location Githubie
dotnet test Githubie.slnx
$InstallRoot = "C:\Githubie"
dotnet publish src\Githubie.Server\Githubie.Server.csproj -c Release -o "$InstallRoot\bin"
dotnet publish src\Githubie.Cli\Githubie.Cli.csproj -c Release -o "$InstallRoot\bin"
dotnet publish src\Githubie.AskPass\Githubie.AskPass.csproj -c Release -o "$InstallRoot\bin"
```

Copy `githubie.example.json` to `$InstallRoot\config\githubie.json`, then follow the commands in the MSI section. See [Configuration](CONFIG.md) and [MCP setup](MCP_SETUP.md).

## Uninstallation

MSI uninstall preserves configuration, credentials, application data, and logs. Revoke GitHub tokens separately when they are no longer required. For a portable installation, stop and unregister the service before removing the chosen installation directory.
