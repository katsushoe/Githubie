# ADR 0006: Common MCP tool result

- Status: Accepted

## Context

Git and GitHub REST Gateways use operation-specific result and error enums (`GitGatewayError`, `GitHubError`). Exposing those internal shapes directly gives MCP clients inconsistent success fields, error locations, enum serialization, and not-found meanings. Tool output must remain stable and must not disclose raw Git stderr, HTTP response bodies, exceptions, or credentials. Fixed error codes alone do not provide enough information to diagnose an unclassified Git failure.

## Decision

Every Githubie MCP Tool returns the common structured shape `{ ok, operation, repository, data, error }`. Successful results place operation-specific typed content in `data` and set `error` to null. Failures set `data` to null. The error retains the compatible `code`, `message`, `recommendation`, and `retryable` fields and also returns the explicit diagnostic aliases `summary`, `suggestedAction`, and `correlationId`. A failed Git process also returns `diagnostic` and `exit_code`. The diagnostic is limited to 2,048 characters after URL user information, authorization values, secret assignments, and common GitHub token formats are redacted. The Git audit decorator writes the same correlation ID, fixed internal error code, safe diagnostic, and exit code. `GithubieToolResultMapper` converts every `GitGatewayError` and `GitHubError` to a fixed snake_case code and a fixed non-secret English summary. Authentication, network, permission, remote availability, non-fast-forward, worktree, and remote configuration failures are classified when safely identifiable; only unclassified failures use `git_failed`.

## Alternatives

- Expose Gateway results unchanged: rejected because Git and REST output contracts differ and leak internal enum organization into MCP schemas.
- Return raw exception or upstream error text: rejected because it is unstable and may contain sensitive data.

## Impact

All 15 MCP output schemas share the same envelope while retaining typed success data. MCP clients can evaluate `ok` and `error.code` uniformly.

## Security conditions

- Fixed error messages never include command output, HTTP response bodies, exceptions, paths, URLs, tokens, or caller-provided text.
- Git diagnostics are exposed only after credential redaction and length limiting. Raw Git stderr is never logged or returned.
- Only stable snake_case codes are exposed for automated handling.

## Operational conditions

Transport- or schema-level MCP failures remain protocol errors because no Tool operation result exists.

## Implementation, tests, and documentation

`Githubie.Server.GithubieToolResult`, `GithubieToolResultMapper`. Full error code list documented in TROUBLESHOOTING.md. Verified live: an unregistered repository returned `{ ok:false, error:{ code:"repository_not_allowed", ... } }` and a missing token returned `{ ok:false, error:{ code:"authentication_failed", ... } }` via real `tools/call` requests.
