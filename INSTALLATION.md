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

The initial MSI install, major-upgrade, and uninstall validation was completed for version `1.0.0.0`. Version `1.6.0.5` was additionally validated by upgrading the MSI at `C:\Githubie`, preserving the existing configuration and repository database, and confirming the installed file version, Windows Service, CLI configuration check, and MCP operation.

Version `1.8.0.0` was validated by upgrading the MSI at `C:\Githubie`, preserving configuration and repository data, and confirming the installed version, running Windows Service, CLI configuration check, MCP response, and repository-registration token-status output schema.

Version `1.8.1.0` updates interactive MCP tool-call timeout handling. Its MSI upgrade at `C:\Githubie`, installed version, automatic Windows Service startup, configuration check, MCP version response, and preservation of registered projects were validated on a Windows machine.

Version `1.8.3.2` resolves and shows the registered repository URL when the token dialog is opened with `githubie auth set`. Its MSI installation at `C:\Githubie`, installed version, automatic Windows Service startup, configuration check, preservation of registered projects, live-PAT HTTPS pull/tag-push paths, and manual uninstall/reinstall lifecycle were validated on a Windows machine. Uninstall removed the service while preserving configuration and data; reinstall recreated and started it.

Version `1.8.4.0` was upgrade-installed at `C:\Githubie`; the installed CLI and MCP versions, automatic running service, configuration check, nine registered projects, and the new Issue list/get tools were verified.

Version `1.8.5.0` was upgrade-installed at `C:\Githubie`; the installed CLI and MCP versions, automatic running service, configuration check, nine registered projects, and repository status before the initial commit were verified.

Version `1.8.6.3` was upgrade-installed at `C:\Githubie`; the installed CLI and MCP versions, automatic running service, external `ready` state, read-only doctor composition, nine registered projects, and repository diff before the initial commit were verified. Missing repository tokens remain independent doctor failures.

Version `1.8.8.0` was upgrade-installed at `C:\Githubie`; installed CLI and file versions `1.8.8.0`, automatic running service, configuration validation, and preservation of four registered projects were verified. The MSI SHA-256 is `A62C4D1305CFBE9F37E26257FFF219B924AFE4B6D4EDCC9A10E14539274085BC`.

Version `1.8.8.1` was upgrade-installed at `C:\Githubie`; installed CLI and file versions `1.8.8.1`, automatic running service, configuration validation, and preservation of nine registered projects were verified. The MSI SHA-256 is `A3F4B6E12CB93D4346CDE2662C9E928E0846AB12B881E0237FE5B977D99142B6`.

Version `1.8.8.2` was upgrade-installed at `C:\Githubie`; installed CLI and MCP versions `1.8.8.2`, automatic running service, configuration validation, and preservation of nine registered projects were verified. The MSI SHA-256 is `6C9FB9D4BC5AB3E44DF1EAC203D3D78E9365B22D64FE2900C0E296824172E8F3`.

Version `1.8.8.3` was upgrade-installed at `C:\Githubie`; installed CLI, MCP, and file versions `1.8.8.3`, automatic running service, configuration validation, and preservation of nine registered projects were verified. The MSI SHA-256 is `213FCC5B8C97D3EB2CB7DF2CB593875B9FE83CE722BA3DA05F5DC62D56E226BF`.

Version `1.8.8.4` was upgrade-installed at `C:\Githubie`; installed CLI, MCP, and file versions `1.8.8.4`, automatic running service, configuration validation, and preservation of nine registered projects were verified. The MSI SHA-256 is `9B833E22899D66988F3B7834E3EC491A0B5AEAA79032DF922E14B6AE05AEF694`.

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
