# ADR 0048: Approved repository relocation

- Status: Accepted

## Context

A registration's local root could not be changed. When a workspace moved (for example `F:\Workspace\Projects\Neon_SDK` became a junction to `E:\Workspace\Projects\Neon_SDK`), every Git operation was rejected with `reparse_point_detected`, and the only fix without losing the stored token or settings was to stop the service and edit the registration database by hand (done once for `neonsdk` on 2026-11-21).

## Decision

Add `github_repository_relocate(repository, local_root)` and the CLI `githubie repo relocate <repository> <local-root>`. The operation:

1. Requires a registered repository and validates the new root like registration: it must exist, contain Git metadata and have no reparse point in its path.
2. Verifies the remote at the new root before any prompt: the stored remote name must point to the registered `github_owner/github_repo` over HTTPS (`remote_mismatch`, `remote_https_required`); with an empty stored remote, at least one HTTPS remote must match.
3. Shows a desktop approval with the old and new root, then saves only `local_root` and updates the live allowlist, so it applies without a restart. Setting the current root is a no-op without a prompt.
4. Keeps the repository ID, stored token, branch policy, PR routes, commit author and Moyai mapping. The result and audit record contain the previous and new root.

The tool is a management operation like `github_repository_rename`, listed in the owner-authentication operations, with no separate ordinary capability boolean.

## Alternatives

- Unregister and register again: rejected because PR routes and other settings are lost and the token prompt is repeated.
- Allow junctions in the path: rejected because the reparse-point check is a security boundary for repository roots.
- Edit the database manually: kept only as an emergency procedure; it bypasses approval and audit and needs a service stop.
