# ADR 0022: CLI access to all MCP tools

## Status

Accepted

## Context

The administrative CLI exposed diagnostics, credentials, and service management but not most GitHub operations. Reimplementing each MCP tool in the CLI would duplicate validation, approval, audit, and policy behavior and would drift whenever tools change.

## Decision

Add `githubie mcp call <tool> [<arguments-json>]` and a `--file` form. The CLI validates that arguments are a JSON object, sends a `tools/call` request to the configured loopback MCP endpoint, prints the JSON-RPC response, and returns nonzero for transport, protocol, MCP, or structured tool failures.

The CLI does not maintain a duplicate tool catalog. Tool discovery remains available through `githubie mcp tools`, so newly exposed MCP tools are callable without another CLI command implementation. Tool calls execute in the running server and therefore retain its allowlist, approval, audit, token, and safety boundaries.

## Consequences

CLI capability stays aligned with MCP capability while preserving one implementation of state-changing behavior. The generic JSON interface is less concise than dedicated commands but is stable for scripting and avoids divergent semantics.
