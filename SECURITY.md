# Security

[English](SECURITY.md) | [日本語](SECURITY.ja.md)

## Trust Boundary

Githubie listens only on `127.0.0.1` and connects outward to GitHub.com over HTTPS. Do not expose the MCP endpoint through a reverse proxy. When present, the HTTP `Origin` header must match the configured loopback origin.

## Security Principles

Githubie never gives agents access to Personal Access Tokens, arbitrary remote URLs, arbitrary Git arguments, arbitrary REST requests, force push, or direct push to protected branches. Every operation resolves a configured repository ID through an allowlist. The standard release route is `develop` to `main`, and tags target `main` HEAD.

Githubie does not require Moyai. Without the `--moyai` server option it runs standalone and ignores `provider_authentication`; in Moyai integration mode with `require_assertion` left `false` it also accepts local direct calls that carry no Moyai headers; both are protected by the loopback/Origin boundary, repository policy, and interactive approvals. A repository-scoped call that carries `Authorization` or `X-Moyai-Operation-Id`, or any call when `require_assertion` is `true`, requires a short-lived Moyai ES256 Provider Assertion and is never downgraded to a direct call. Githubie validates the exact `githubie` audience, administrator-mapped Project UUID, canonical repository, tool, fixed scope set, protocol version, operation ID, time window, current signing key, and one-time JTI before dispatch. It checks validity again immediately before dispatch and after an interactive approval wait. `list_projects`, repository administration, and `get_version` remain in the separate local administrator/bootstrap boundary. The legacy MCP endpoint had no service token, so there is no dual-accept token path.

## Credentials

Use a fine-grained PAT restricted to the required repository with `Contents: Read and write` and `Pull requests: Read and write`. Repository Description updates additionally require `Administration: Read and write`; workflow dispatch and run reads require `Actions: Read and write`. The foreground dialog after repository approval and `githubie.exe auth set` pass tokens through a SID-restricted named pipe and encrypt them with DPAPI LocalMachine. The secrets directory ACL is limited to LocalSystem, Administrators, and the current user. Tokens are never MCP arguments, responses, logs, command-line arguments, or remote URLs. The repository registry database at `data\githubie.db` contains policies but never tokens.

## Repository and Path Validation

Local operations are limited to configured `local_root` values. Parent traversal and symlink/junction escape are rejected. Before Git network operations, the configured remote must be HTTPS and resolve to the configured `github.com/<owner>/<repo>` target. SSH remotes are rejected rather than rewritten implicitly.

## Audit Logging

Audit records include operation identifiers, targets, result, duration, and stable error codes. Tokens, authorization headers, passwords, file contents, and other secrets are excluded.

## Vulnerability Reporting

Do not disclose vulnerabilities in a public issue. Contact the repository maintainer [katsushoe](https://github.com/katsushoe) directly with reproduction steps, affected versions, and impact.
