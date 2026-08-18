# ADR 0012: Configuration-file-only repository registration in Phase 1

- Status: Accepted

## Context

Buckettie's ADR 0012 introduces MCP-driven repository registration with an interactive desktop approval prompt (a separate WinForms process communicating over a named pipe), reasoning that chat-only confirmation is insufficient because it can be spoofed or skipped within the same trust boundary as the requesting Agent.

## Decision

Githubie Phase 1 does not implement dynamic, MCP-driven repository registration or its interactive approval flow. Every entry under `repositories` in `githubie.json` is added by an operator editing the file directly (or via a future `config` command), and `githubie.exe config check` / `doctor` validate it. No `github_repository_register` MCP Tool exists, and no `Githubie.ApprovalPrompt` / `Githubie.Interactive` project exists.

## Alternatives

- Port Buckettie's interactive registration/approval flow as-is: deferred rather than rejected. The underlying security reasoning (chat-only confirmation is insufficient for a trust-boundary-crossing change) applies equally to Githubie, so if dynamic registration is added in a later phase, it must carry the same out-of-band human approval design, not a simpler one.

## Impact

Registering a new repository requires filesystem access to `githubie.json` and a service restart (see OPERATIONS.md); it cannot be done through the MCP Tool surface at all in Phase 1. This keeps Phase 1's project structure smaller (no WinForms dependency, no named-pipe protocol) at the cost of a less convenient onboarding flow for new repositories.

## Security conditions

Because registration is filesystem-only, it is already outside the MCP Agent's reach by construction; no separate approval mechanism is needed for Phase 1's threat model.

## Operational conditions

None beyond standard configuration-file editing (CONFIG.md).

## Implementation, tests, and documentation

N/A (explicitly out of scope). Revisit this ADR before adding any tool that can add or modify `repositories` entries at runtime.
