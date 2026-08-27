# Security

[English](SECURITY.md) | [日本語](SECURITY.ja.md)

## Trust Boundary

Githubie listens only on `127.0.0.1` and connects outward to GitHub.com over HTTPS. Do not expose the MCP endpoint through a reverse proxy. When present, the HTTP `Origin` header must match the configured loopback origin.

## Security Principles

Githubie never gives agents access to Personal Access Tokens, arbitrary remote URLs, arbitrary Git arguments, arbitrary REST requests, force push, or direct push to protected branches. Every operation resolves a configured repository ID through an allowlist. The standard release route is `develop` to `main`, and tags target `main` HEAD.

## Credentials

Use a fine-grained PAT restricted to the required repository with `Contents: Read and write` and `Pull requests: Read and write`. Repository Description updates additionally require `Administration: Read and write`; workflow dispatch and run reads require `Actions: Read and write`. `githubie.exe auth set` encrypts tokens with DPAPI LocalMachine. The secrets directory ACL is limited to LocalSystem, Administrators, and the current user. Tokens are passed to Git through `GIT_ASKPASS`, never through command-line arguments or remote URLs. The repository registry database at `data\githubie.db` contains policies but never tokens.

## Repository and Path Validation

Local operations are limited to configured `local_root` values. Parent traversal and symlink/junction escape are rejected. Before Git network operations, the configured remote must be HTTPS and resolve to the configured `github.com/<owner>/<repo>` target. SSH remotes are rejected rather than rewritten implicitly.

## Audit Logging

Audit records include operation identifiers, targets, result, duration, and stable error codes. Tokens, authorization headers, passwords, file contents, and other secrets are excluded.

## Vulnerability Reporting

Do not disclose vulnerabilities in a public issue. Contact the repository maintainer [katsushoe](https://github.com/katsushoe) directly with reproduction steps, affected versions, and impact.
