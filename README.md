# Githubie

Candidate 2.1.4.0 adds fixed-SHA PR source branch publication, PR creation and result reconciliation. Live GitHub and Consumer interoperability acceptance are separate gates. See the [contract](docs/specifications/pr-source-publication.md).

The development candidate adds [GitHub branch protection operations](docs/specifications/github-branch-protection.md). Candidate verification, actual deployment and Moyai interoperability remain separate milestones.

Candidate2.1.1.0 adds explicit PR read/merge capabilities and conditional source-SHA merge with unknown-result reconciliation. See the [PR merge contract](docs/specifications/pr-merge-provider.md). Candidate verification, actual Moyai Consumer acceptance and Windows deployment are separate milestones.

[English](README.md) | [日本語](README.ja.md)

`<install-root>` means the actual user-selected installation directory (default `C:\Githubie`). The sole configuration is `config\githubie.json` under the parent of the running executable's `bin` directory.

Githubie is a Windows gateway that lets MCP clients operate allowlisted local Git repositories and GitHub.com without exposing arbitrary Git commands, repository URLs, or credentials. It is the GitHub companion to [Buckettie](https://github.com/katsushoe/Buckettie).

## Getting Started

Run `Install-UserHost.ps1` beside the 2.0 MSI from the intended user's non-elevated PowerShell. MSI elevation and current-user startup registration are separate phases of the same setup. Then connect the AI client through the protected stdio bridge:

```powershell
.\Install-UserHost.ps1 -MsiPath .\Githubie-2.1.0.0-win-x64.msi
githubie host status
githubie configure codex
# Or: githubie configure claude
```

Re-register repositories and enter tokens in the dedicated UI. The fresh user profile does not import old Service data. See [User host](USER_HOST.md) for startup, client migration, security and recovery; [MCP setup](MCP_SETUP.md) links the retained tool reference.

`githubiemcp` is a client for an already running Githubie service. Use `githubiemcp --help` for commands; MSI and ZIP include it beside `githubie`. See [commands](COMMANDS.md).

## Installation

The recommended distribution is the x64 MSI. A portable ZIP and reproducible source-build instructions are also available. See [Installation](INSTALLATION.md).

Developers need the .NET 9 SDK and Git for Windows:

```powershell
dotnet build Githubie.slnx
dotnet test Githubie.slnx
```

## Configuration

Githubie reads its only operational configuration from `<install-root>\config\githubie.json`, including `user_host` startup and integration settings. Repository databases, CurrentUser-protected tokens, identity, discovery and logs remain per user under LocalApplicationData/Akatsukisoft/Githubie. Machine databases and token blobs are not imported. See [User host configuration](USER_HOST.md#profile-and-host-operations) and [configuration schema](CONFIG.md).

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

## Windows owner authentication (candidate 2.1.0.0)

Explicitly set `user_host.authentication_mode=windows_owner` in the sole JSON to trust Moyai/native callers authenticated as the same Windows owner. Omission selects `legacy` and preserves previous behavior. Fresh MSI default JSON explicitly selects `windows_owner`; existing JSON is never overwritten. This mode requires `--user-host` and is rejected by the legacy Service.

Every HTTP request requires schema1 discovery's `X-Githubie-Session` and `X-Githubie-Generation`. ES256 assertions, Issuer keys, Project signature scopes and legacy tokens are not required. Old Moyai/direct settings and trust assets are preserved but not used for authorization in this mode. Repository/branch policy, owner approval, external-service credentials and audit remain. The audit actor is the Windows owner SID.

Consumers verify the Provider version and explicit `github_provider_capabilities.authentication.owner_authentication` contract and target tool name before dispatch. Ordinary capability flags alone do not establish signature-free support. An Authorization header in owner mode returns403/`user_host_auth_mode_mismatch`; owner/generation rejection retains403/`user_host_owner_rejected`. No alternate-mode fallback or automatic unknown-outcome mutation replay is allowed.

See [Windows owner authentication v1](docs/specifications/windows-owner-authentication-v1.md) for exact fields/operations/errors and [ADR0042](docs/adr/0042-windows-owner-authentication.md) for the decision. This is a proposed contract/candidate; Moyai agreement, interoperability and normal rollout are separate gates. Do not apply it automatically to the installed configuration or delete existing keys, DBs or trust assets.
