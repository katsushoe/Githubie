# ADR 0011: snake_case MCP tool parameter names via literal C# identifiers

- Status: Accepted

## Context

`GithubieMcpJson.CreateOptions()` configures `PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower` so that Structured Output (`GithubieToolResult<T>` and its payload types) serializes consistently in snake_case. Real-machine verification showed this policy is *not* applied by the underlying `Microsoft.Extensions.AI.AIFunctionFactory` reflection-based schema generator to MCP Tool *input* parameter names: a C# parameter named `pullRequestNumber` was published in `tools/list`'s `inputSchema` verbatim as `"pullRequestNumber"`, inconsistent with every other snake_case field in the same schema.

## Decision

Name multi-word MCP Tool method parameters using literal snake_case C# identifiers (e.g. `pull_request_number`, `merge_strategy`) rather than idiomatic C# camelCase, whenever a parameter is exposed on a `[McpServerTool]`-decorated method. This is an explicit, deliberate deviation from standard C# naming convention, scoped only to MCP tool method signatures in `GithubieMcpTools`, in exchange for a fully consistent snake_case surface across input and output.

## Alternatives

- Accept the camelCase/snake_case inconsistency: rejected because it is confusing for MCP client authors and inconsistent with the specification's stated goal of a uniform snake_case API surface.
- Post-process the generated `inputSchema` to rename properties: rejected as fragile (would need to also rename incoming argument keys before binding, duplicating what the SDK already does based on the literal parameter name).

## Impact

Every future MCP Tool parameter with more than one word must be named in snake_case in the C# source, which will look unconventional in code review; this ADR exists precisely so that choice is not re-litigated or "corrected" back to camelCase later.

## Security conditions

None.

## Operational conditions

`Githubie.Server.Tests.GithubieMcpToolsTests.AllParameterNames_AreSnakeCase` enforces this as a regression guard (rejects any uppercase letter in a tool parameter name other than `CancellationToken`, which is excluded).

## Implementation, tests, and documentation

`Githubie.Server.GithubieMcpTools`. Verified live: `tools/list`'s `inputSchema` for `github_pr_get`/`github_pr_diff`/`github_pr_merge` shows `pull_request_number` and `merge_strategy` in snake_case after the fix, confirmed via a real MCP HTTP request during Phase 1 real-machine verification.
