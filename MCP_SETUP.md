# MCP Setup

[English](MCP_SETUP.md) | [日本語](MCP_SETUP.ja.md)

## 2.0 user host

Use the [canonical user-host guide](USER_HOST.md) for 2.0 installation, paths, CLI/MCP registration, startup, security, uninstall and recovery. Binaries stay in `C:\Githubie`; mutable data is per user. Old Service settings/tokens are not imported: re-register repositories and credentials. `configure codex|claude` registers a credential-free stdio command.

## 1.x reference and recovery

The following Service paths, direct token input and installation procedures describe 1.x. The 2.0 guide above takes precedence. Unchanged typed MCP tool argument/result schemas still apply to 2.0. Historical acceptance statements apply only to their stated versions.

Githubie exposes MCP over Streamable HTTP at `http://127.0.0.1:45460/mcp` by default. Start `Githubie.Server.exe` or the Windows Service and register credentials with `githubie.exe auth set <repository>` first.

## Connectivity Check

```powershell
githubie.exe mcp status
githubie.exe mcp tools
```

The second command should return 15 tool definitions documented in [Commands](COMMANDS.md).

## Claude Code

```bash
claude mcp add --transport http githubie http://127.0.0.1:45460/mcp
```

## Codex

Confirm the configuration format supported by the installed Codex version. A typical configuration is:

```toml
[mcp_servers.githubie]
url = "http://127.0.0.1:45460/mcp"
```

## Origin Validation

When an `Origin` header is present, it must exactly match `http://127.0.0.1:<mcp_port>` or `http://localhost:<mcp_port>` with no query or fragment. Clients that omit `Origin` are accepted. Remote or cloud-hosted clients cannot reach the loopback endpoint.

## Multiple Repositories

Configure the allowlist in `githubie.json`. MCP clients specify only the internal `repository` ID and cannot choose arbitrary GitHub owners, repositories, or local paths. See [Configuration](CONFIG.md).
