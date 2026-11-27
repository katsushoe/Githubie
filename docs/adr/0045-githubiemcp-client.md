# ADR 0045: Dedicated Githubie MCP client

Status: Proposed

## Context

The separate `githubiemcp` entry point must reach the running Githubie service while sharing the existing CLI and its security boundary. The later installation policy permits only `<actual install root>/config/githubie.json`; the older request for an arbitrary `--config` override is superseded by that policy.

## Decision

Provide a small executable that calls the existing CLI dispatcher in process. It maps MCP shorthand commands to `githubie mcp`, forwards other supported CLI operations, and rejects server lifecycle and setup commands. The existing dispatcher resolves and validates configuration, connection, authentication and errors. Repository registration and credential entry remain behind the existing service and its interactive UI. No credential is accepted through a new argument or written to logs.

## Alternatives

Duplicating the HTTP client and command implementations risks contract drift. Launching the existing executable as a child would duplicate process management and complicate output and cancellation; direct dispatch preserves the same implementation and installation-relative path.

## Consequences and verification

The executable and its dependencies must be included in MSI and ZIP. English and Japanese command and installation documentation must describe the client, its canonical configuration, service dependency and token UI. Tests cover dispatch, exit codes, help, version, alternative configuration rejection, and prevention of server management. Existing tests continue to cover service authorization, audit and token handling; an isolated running service is needed to verify end-to-end parity.
