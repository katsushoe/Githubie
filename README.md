# Githubie

[English](README.md) | [日本語](README.ja.md)

Githubie is a Windows gateway that lets MCP clients operate allowlisted local Git repositories and GitHub.com without exposing arbitrary Git commands, repository URLs, or credentials. It is the GitHub companion to [Buckettie](https://github.com/katsushoe/Buckettie).

## Getting Started

Run `Install-UserHost.ps1` beside the 2.0 MSI from the intended user's non-elevated PowerShell. MSI elevation and current-user startup registration are separate phases of the same setup. Then connect the AI client through the protected stdio bridge:

```powershell
.\Install-UserHost.ps1 -MsiPath .\Githubie-2.0.0.0-win-x64.msi
githubie host status
githubie configure codex
# Or: githubie configure claude
```

Re-register repositories and enter tokens in the dedicated UI. The fresh user profile does not import old Service data. See [User host](USER_HOST.md) for startup, client migration, security and recovery; [MCP setup](MCP_SETUP.md) links the retained tool reference.

## Installation

The recommended distribution is the x64 MSI. A portable ZIP and reproducible source-build instructions are also available. See [Installation](INSTALLATION.md).

Developers need the .NET 9 SDK and Git for Windows:

```powershell
dotnet build Githubie.slnx
dotnet test Githubie.slnx
```

## Configuration

Githubie stores each user's configuration, database, CurrentUser-protected tokens and logs under the Windows LocalApplicationData known folder, `Akatsukisoft/Githubie`. Binaries stay in `C:\Githubie`. Old Service data is retained for recovery and never imported automatically. See [User host configuration](USER_HOST.md#profile-and-host-operations) and the [configuration schema](CONFIG.md).

## Usage

Use `githubie.exe` for configuration, credentials, diagnostics, and user-host management. MCP clients receive 57 typed tools for repository discovery, approval-backed repository registration, repository status/description, GitHub Actions workflow dispatch/run inspection, fetch/pull/push, approval-backed history rewrite, branches, issues, pull requests, tags, releases, and version information. See [Commands](COMMANDS.md).

The `githubie_usage` MCP prompt gives agents a concise guide to Githubie's purpose, repository ID usage, the inspect-before-mutate workflow, protected branches, credentials, and history-rewrite safety. MCP clients that support prompts can select it at the start of repository work.

Branch creation requires an explicit `source` (branch name or full 40-character commit SHA). Calls without a source no longer default to main; update callers, including Moyai, before using the new contract. See [ADR 0026](docs/adr/0026-explicit-branch-source.md) and [Commands](COMMANDS.md).

`github_repository_register` derives the GitHub repository from a local HTTPS remote, requests consent and token input in the current user session, then commits settings and the protected token together. Explicitly registering without a token is supported; cancel or token/persistence failure leaves no new registration. The running allowlist updates without restarting the host.

Agents call `list_projects` to select a registered repository ID and repeat that check immediately before every `github_push`. A push attempted with an unregistered ID returns the registered IDs as candidates.

Release tools list, inspect, update, and retry asset publication. Approved assets are bounded to repository-local MSI, ZIP, SHA-256, `SHA256SUMS.txt`, and PowerShell files; replacement requires an explicit flag.

`github_history_rewrite` corrects published branch/tag history. Inspect its dry-run and retain a mirror or backup refs first. Real updates require out-of-band desktop approval, recheck every remote SHA, and use atomic push with per-ref force-with-lease. Normal `github_push` protection remains unchanged; recovery uses the same workflow with backed-up SHAs.

## Documentation

- [Installation](INSTALLATION.md)
- [Configuration](CONFIG.md)
- [MCP setup](MCP_SETUP.md)
- [Commands](COMMANDS.md)
- [Operations](OPERATIONS.md)
- [Security](SECURITY.md)
- [Troubleshooting](TROUBLESHOOTING.md)
- [Package layout](PACKAGES.md)
- [Release process](RELEASE.md)
- [Architecture Decision Records](docs/adr/README.md)

## Security

The MCP endpoint uses loopback plus per-start user credentials and Host/Origin checks. The host and tray run in one non-elevated user process. Keep Personal Access Tokens out of configuration and MCP client settings; store them in the foreground dialog after repository approval or with `githubie.exe auth set`. In both GUI flows, the token dialog shows the source project name and target repository URL before token entry. A fine-grained PAT limited to the configured repository with `Contents: Read and write` and `Pull requests: Read and write` is recommended. See [Security](SECURITY.md).

## License

Githubie is available under the [MIT License](LICENSE).
