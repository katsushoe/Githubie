# ADR 0042: Explicit Windows owner authentication

Status: product policy accepted by CR-2026-10-25-githubie-owner-session-trust; wire agreement and implementation validation pending.

## Context and decision

The product owner trusts applications that authenticate as the same Windows user. Githubie already protects user-host transport using private schema1 discovery, CurrentUser credentials and process generation. Requiring an additional ES256 Issuer/Project authorization identity for that selected mode creates an unwanted application boundary.

Introduce explicit `user_host.authentication_mode=windows_owner`, leaving omitted/legacy behavior unchanged. It is user-host-only. Authenticate every request through the existing owner transport, advertise the exact versioned owner contract and unsigned operation coverage, and retain all Provider policy, approval, external credentials and audit. Stored assertion settings are preserved but not activated in this mode. Legacy headers cannot select or downgrade the mode. Runtime configuration remains the sole installation-relative JSON from ADR0041.

The wire and acceptance source of truth is [Windows owner authentication v1](../specifications/windows-owner-authentication-v1.md). Moyai must agree to exact fields before its Consumer implementation; public documentation must distinguish the proposal, verified candidate and installed behavior.

## Alternatives and impact

Keeping required Project assertions contradicts the selected owner trust boundary. Removing headers from old static HTTP does not authenticate an owner. Implicitly enabling owner mode in old Service configurations would change a live security boundary, so explicit selection and user-host-only validation are required. Per-application lists and substitute Project grants are not introduced.

## Security and operations

Keep SID/ACL/runtime/profile/generation/session and endpoint checks, credential redaction and mutation non-replay. Audit the Windows owner rather than a claimed Project identity. Preserve existing keys/DBs/trust until separately approved migration succeeds. Provider fixtures and Consumer fixtures have separate owners. Do not install or alter the live Service as part of source implementation.

## Implementation and validation

Extend strict JSON validation, host composition/gate, capabilities and audit; update EN/JA CONFIG/USER_HOST/COMMANDS/README. Test legacy preservation, mode mismatch, no-signature operations, negative owner/discovery cases and policy/approval retention. Publish candidate 2.1.0.0 after passing checks; real Consumer acceptance and normal rollout remain explicit separate gates.
