# Package Layout

[English](PACKAGES.md) | [日本語](PACKAGES.ja.md)

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

The x64 MSI installs under `%ProgramFiles%\Githubie`, creates runtime directories with the required ACLs, registers the Windows Service, supports major upgrades, and preserves user configuration and data during uninstall.

## Portable ZIP

The ZIP contains the same application and public documentation but does not register the service. Users must configure and install the service explicitly.

## Build

```powershell
powershell -ExecutionPolicy Bypass -File scripts\Build-Msi.ps1 -Version 1.0.0.0
powershell -ExecutionPolicy Bypass -File scripts\Build-Zip.ps1 -Version 1.0.0.0
```

Outputs are written under `.local\installer\output` and `.local\release\output`. See [Release](RELEASE.md).
