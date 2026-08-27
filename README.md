# Githubie

[English](README.md) | [日本語](README.ja.md)

Githubie is a Windows gateway that lets MCP clients operate allowlisted local Git repositories and GitHub.com without exposing arbitrary Git commands, repository URLs, or credentials. It is the GitHub companion to [Buckettie](https://github.com/katsushoe/Buckettie).

## Getting Started

Install the MSI, copy `githubie.example.json` to `<install-root>\config\githubie.json`, and configure at least one repository. Then run:

```powershell
githubie.exe config check
githubie.exe auth set <repository-id>
githubie.exe start
githubie.exe doctor
```

Register `http://127.0.0.1:45460/mcp` with your MCP client. Server Instructions sent during connection explain Githubie's purpose, safety constraints, and recommended tool selection to the agent. See [MCP setup](MCP_SETUP.md) for client-specific instructions.

## Installation

The recommended distribution is the x64 MSI. A portable ZIP and reproducible source-build instructions are also available. See [Installation](INSTALLATION.md).

Developers need the .NET 9 SDK and Git for Windows:

```powershell
dotnet build Githubie.slnx
dotnet test Githubie.slnx
```

## Configuration

Githubie reads endpoint settings from `<install-root>\config\githubie.json` and stores repository registrations and policies in `<install-root>\data\githubie.db`. Existing JSON repository entries are imported once during upgrade. See [Configuration](CONFIG.md).

## Usage

Use `githubie.exe` for configuration, credentials, diagnostics, and Windows Service management. MCP clients receive 37 typed tools for approval-backed repository registration, repository status/description, GitHub Actions workflow dispatch/run inspection, fetch/pull/push, approval-backed history rewrite, branches, pull requests, tags, releases, and version information. See [Commands](COMMANDS.md).

The `githubie_usage` MCP prompt gives agents a concise guide to Githubie's purpose, repository ID usage, the inspect-before-mutate workflow, protected branches, credentials, and history-rewrite safety. MCP clients that support prompts can select it at the start of repository work.

`github_repository_register` derives the GitHub owner/repository from an existing local remote, requires desktop approval, persists the configuration, and updates the running allowlist without a service restart.

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

The MCP endpoint listens only on loopback. Keep Personal Access Tokens out of configuration and MCP client settings; store them with `githubie.exe auth set`. A fine-grained PAT limited to the configured repository with `Contents: Read and write` and `Pull requests: Read and write` is recommended. See [Security](SECURITY.md).

## License

Githubie is available under the [MIT License](LICENSE).
