# ADR 0029: Moyai Provider Assertion boundary

- Status: Accepted (amended 2026-09-24: Moyai is optional; see "Standalone operation")

## Context

Loopback and Origin validation prevent remote exposure and browser rebinding, but they do not identify which Moyai Project authorized one repository operation. Repository tools need an operation-bound identity that cannot be replayed, redirected to another Provider or repository, or remain valid after an approval wait expires.

## Decision

Reference `Moyai.ProviderAuthentication` 1.0.2 as a packaged dependency. Require protocol v1 ES256 Assertions for every repository operation and use the canonical `githubie` audience. Build the expected Project UUID from administrator configuration and the canonical repository from the allowlist; never trust either value from the Assertion as registration data. Validate the exact tool and fixed scope set, operation ID, issuer, lifetime, signing-key state, and one-time JTI before MCP dispatch.

Store replay reservations in a dedicated SQLite database and reload the administrator-protected Trust Bundle for every validation. Retain the validated Principal in an async request context. Call `EnsureCurrentAsync` immediately before dispatch and again after the interactive history-rewrite approval, immediately before the Git push. Repository management, discovery, and version tools remain in the existing separate local administrator/bootstrap boundary.

## Alternatives

- A static service Bearer token: rejected because it does not bind a request to one Project, repository, tool, scope, operation, or expiry.
- Derive the Project mapping from JWT claims: rejected because a signed request must not create the Provider's authorization mapping.
- Validate only in HTTP middleware: rejected because an Assertion can expire or its key can be revoked while an interactive approval is pending.
- Reference Moyai source projects: rejected because Provider distribution must not depend on Moyai's repository layout or internal database.

## Impact

Moyai must issue one Assertion per repository operation and send `X-Moyai-Operation-Id`. Requests that carry `Authorization` or `X-Moyai-Operation-Id` are always validated as Moyai requests. The existing GitHub Personal Access Token remains the outbound GitHub credential and is unrelated to the Provider Assertion. There is no legacy MCP service token to support through dual acceptance.

## Standalone operation

Githubie is a Moyai sub-tool but must not depend on Moyai. The operating mode is selected on the server command line, not by configuration presence: by default the server runs standalone, ignores `provider_authentication`, and registers neither the validator nor the middleware. Only `Githubie.Server.exe <config> --moyai` enables Moyai integration mode, which requires `provider_authentication` and a Project mapping for every registered repository. The MSI exposes this as the remembered `MOYAI` property, and `githubie service install --moyai` / `githubie config check --moyai` provide the same choice on the CLI. In Moyai integration mode, `require_assertion` (default `false`) decides how repository tool calls without any Moyai header are handled: `false` accepts them as local direct calls under the existing loopback/Origin boundary, repository policy, and interactive approvals; `true` rejects them. A request with either Moyai header is never downgraded to a direct call, so an invalid Assertion cannot fall back to another path. The original "every repository operation requires an Assertion" decision was reverted because it removed the only write path when Moyai had not yet been granted the matching tool scopes.

## Security conditions

Trust and replay paths are distinct absolute paths. Each configured repository ID maps to one unique non-empty Project UUID. Authentication failures expose stable codes and never log Assertions, authorization headers, keys, or credentials. Missing trust or replay state fails closed. The Replay database and Trust Bundle must be writable only by administrators or the service identity as operationally appropriate.

## Operational conditions

Key, issuer, path, and mapping changes require configuration validation and service restart; key-state changes within the same Trust Bundle path are reloaded per request. Isolated integration testing uses separate port, config, repository database, Trust Bundle, replay database, disposable issuer/key, Project mappings, and client profiles. Installation, service update, push, and release remain separate operations.

## Implementation, tests, and documentation

The Server middleware constructs the expected context and validates the Assertion. `GithubieAssertionExecutionContext` limits the Principal to the current async request, and `GitGateway` rechecks it after history-rewrite approval. Tests cover audience isolation, persistent replay rejection across validator restart, Trust Bundle revocation during a simulated wait, no Gateway dispatch after rejection, configuration mapping, and approval-to-push ordering. Configuration, security, and operations documents define the administrator and runtime procedures.

The HTTP host must register the composed `GithubieOptions` alongside the validators; registration in the separate application service provider does not satisfy middleware injection. `scripts/Test-ProviderAssertionHttp.ps1` starts the built product in a temporary installation with a disposable signer, loopback port, Trust Bundle, and SQLite replay database. It checks bootstrap, authentication rejection, authenticated MCP dispatch, replay across restart, trust revocation, and absence of Assertions from logs. Its repository is deliberately absent, so an authenticated Gateway error proves dispatch but does not establish repository operation success. This HTTP harness does not replace Moyai or native Codex/Claude client integration testing.
