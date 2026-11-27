# Windows owner authentication v1

Status: proposed wire contract for candidate 2.1.0.0, implementing CR-2026-10-25-githubie-owner-session-trust. Product policy is accepted; Consumer agreement, implementation verification and rollout are separate milestones.

## Selection and trust boundary

Set `user_host.authentication_mode` to `windows_owner` in the sole `<install-root>\config\githubie.json`, and run the user host. Omission means `legacy`, retaining the previous standalone/assertion behavior. No new environment or command-line selector exists. `windows_owner` cannot run as the legacy Service. The old `moyai_integration` / `direct_unrestricted` settings and `provider_authentication` assets remain stored but are not used to authorize this mode. No Issuer, signing key, Project binding or legacy token is required. No per-application registration or Project delegation grant replaces them.

Use the current owner's schema1 discovery described in [USER_HOST](../../USER_HOST.md#versioned-discovery-contract-candidate-consumer-agreement-pending): validate owner SID, owner-only ACL, Provider/profile/runtime hash, live PID/start ticks, generation, session and loopback endpoint before decrypting the CurrentUser credential. Retain the existing 13-field record and DPAPI envelope. A port, a static endpoint and an old Service token are insufficient. Every HTTP request, including initialize, discovery/capabilities and version, needs exactly one `X-Githubie-Session` and `X-Githubie-Generation` from that discovery generation. Never send the protected blob as the header. Redirects are disabled.

## Capability handshake

Before a business call, invoke `github_provider_capabilities` through the authenticated connection for the selected Provider Repository ID. Require Provider version at least `2.1.0.0`, `authentication.integration_mode="windows_owner"`, `authentication.direct_connection="unrestricted"` and a non-null `authentication.owner_authentication` object:

```json
{
  "version": 1,
  "mode": "windows_owner",
  "transport": "user_host_schema1",
  "principal": "windows_owner",
  "assertion_required": false,
  "project_binding_required": false,
  "legacy_token_required": false,
  "credential_header": "X-Githubie-Session",
  "generation_header": "X-Githubie-Generation",
  "operations": ["github_repository_status", "github_repository_commit", "github_release_create"]
}
```

The operations above illustrate the shape; the real response enumerates every supported Repository/PR/Settings/branch/tag/workflow/issue/release tool in [GithubieAssertionPolicy's registered operation table](../../src/Githubie.Server/GithubieAssertionPolicy.cs). Require the target tool's exact name in that list as well as its ordinary operation capability, where defined. This list describes authentication coverage, not permission to bypass Repository/branch policy or approval. An unregistered Repository still returns `repository_not_found`. Unknown MCP tools remain unsupported. Legacy mode reports no owner-authentication object; ordinary capability booleans alone cannot prove support.

## Requests, errors and audit

Owner-mode requests omit `Authorization`. Supplying an assertion or legacy Bearer token returns HTTP403 with `X-Githubie-Error: user_host_auth_mode_mismatch`; it never selects another mode. `X-Moyai-Operation-Id`, when supplied, is optional correlation context, not authentication or a required signed scope. Project/Repository selection remains routing context. Audit records identify the host's verified Windows owner SID and `windows_owner` mode; caller-supplied identity never replaces that owner. Existing operation correlation, policy/error logging and redaction remain.

Missing/wrong session credentials, missing/wrong generation or rejected Host/Origin return HTTP403 with `X-Githubie-Error: user_host_owner_rejected`. Discovery continues to use the documented stable `UserHostDiscoveryException` categories. Missing owner contract/operation support is a Consumer-side unsupported-capability failure; do not fall back to a static endpoint, assertion, legacy token or unauthenticated request. The existing bounded metadata-only rediscovery after explicit owner rejection remains; an unknown-outcome mutation is never automatically replayed. Policy refusals and GitHub authentication errors keep their existing operation results.

## Ownership and acceptance

Githubie owns its fixture/config/host lifecycle and cleanup; Moyai owns its isolated Consumer DB/config/results and cleanup. No ordinary profile, live Service, trust/key/DB deletion, normal installation or release is authorized by this contract. Reconcile the prior ES256 Consumer cases against this mode before testing. Cover unsigned read/commit/push/branch/Settings/PR/release where supported, policy/approval failures, wrong owner/credential, restart rotation, stale process/runtime, redirects, capability refusal, redaction and mutation non-replay. A self-test is not evidence that the real Moyai Consumer interoperates.
