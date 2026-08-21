# ADR 0017: Repository update and unregistration

## Status

Accepted.

## Context

Githubie supported approved repository registration but could not update branch policy or revoke a registration at runtime. Buckettie exposes both lifecycle operations.

## Decision

Add `github_repository_update` and `github_repository_unregister`. Update changes only branch-policy fields and requires the same out-of-band desktop approval as registration. Identity, local root, remote, and primary branches require unregistering and registering again. Unregister removes the entry from `githubie.json` and the live allowlist without approval because it only revokes Githubie permissions. Neither operation deletes the GitHub repository, local files, Git history, or stored token.

Both operations persist the configuration before changing the live allowlist. Validation, denial, timeout, unavailable approval, missing registration, and persistence failure return fixed errors and leave the effective registration unchanged.

## Alternatives

- Edit JSON and restart: rejected because it does not provide an atomic live lifecycle operation.
- Require approval for unregistration: rejected because revoking permissions does not expand authority.
- Allow identity and path updates: rejected because those values must be re-derived and revalidated through registration.

## Consequences, security, and operations

- Update can expand branch permissions and therefore requires desktop approval.
- Unregistration is destructive in MCP metadata but only reduces authority.
- Tests cover approved update, denied update, unregistration without approval, missing registration, and persistent deletion.
