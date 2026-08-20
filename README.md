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

Register `http://127.0.0.1:45460/mcp` with your MCP client. See [MCP setup](MCP_SETUP.md) for client-specific instructions.

## Installation

The recommended distribution is the x64 MSI. A portable ZIP and reproducible source-build instructions are also available. See [Installation](INSTALLATION.md).

Developers need the .NET 9 SDK and Git for Windows:

```powershell
dotnet build Githubie.slnx
dotnet test Githubie.slnx
```

## Configuration

Githubie reads `<install-root>\config\githubie.json` by default. Repository IDs map to fixed GitHub owner/repository pairs, local roots, and branch policies. See [Configuration](CONFIG.md).

## Usage

Use `githubie.exe` for configuration, credentials, diagnostics, and Windows Service management. MCP clients receive 15 typed tools for repository status, fetch/pull/push, branches, pull requests, tags, and version information. See [Commands](COMMANDS.md).

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
