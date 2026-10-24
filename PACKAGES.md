# Package Layout

[English](PACKAGES.md) | [日本語](PACKAGES.ja.md)

## 2.0 user host

Use the [canonical user-host guide](USER_HOST.md) for 2.0 installation, paths, CLI/MCP registration, startup, security, uninstall and recovery. Binaries stay in `C:\Githubie`; mutable data is per user. Old Service settings/tokens are not imported: re-register repositories and credentials. `configure codex|claude` registers a credential-free stdio command.

## 1.x reference and recovery

The following Service paths, direct token input and installation procedures describe 1.x. The 2.0 guide above takes precedence. Unchanged typed MCP tool argument/result schemas still apply to 2.0. Historical acceptance statements apply only to their stated versions.

## Package Contract

| Path | Contents |
| :--- | :--- |
| `bin/githubie.exe` | Management CLI |
| `bin/Githubie.Server.exe` | MCP Windows Service |
| `bin/Githubie.AskPass.exe` | Git credential helper |
| `bin/*.dll` and runtime files | Application and runtime dependencies |
| `config/githubie.example.json` | Configuration template without secrets |
| `docs/*.md` | Public English and Japanese user documentation |
| `docs/LICENSE` | MIT License |

Packages exclude active configuration, tokens, application data, logs, test results, and `.local/` content.

Runtime application data includes `data/githubie.db`, which stores repository registrations and policies but no tokens.

## MSI

The x64 MSI installs under the standard installation directory `C:\Githubie`, creates runtime directories with the required ACLs, registers the Windows Service, supports major upgrades, and preserves user configuration and data during uninstall.

## Portable ZIP

The ZIP contains the same application and public documentation but does not register the service. Users must configure and install the service explicitly.

## Build

```powershell
pwsh scripts\Build-Msi.ps1 -DisplayVersion <display-version> -ProductVersion <three-part-product-version>
pwsh scripts\Build-Zip.ps1 -DisplayVersion <display-version>
```

Outputs are written under `.local\installer\output` and `.local\release\output`. See [Release](RELEASE.md).
