# ADR 0036: Local commit permission for pull-request source branches

## Status

Proposed

## Context

Repository policy previously coupled local commits and remote pushes to `direct_push_branches`, which required direct-push permission just to create commits on a local-only branch. That could permit a remote write the repository owner did not intend.

## Decision

Authorize `github_repository_commit` and `github_repository_commit_selected` from a separate, exact-match `local_commit_branches` list. Do not infer local-commit permission from PR routes or `direct_push_branches`. Continue to require `direct_push_branches` for `github_push`, and reject commits and pushes on every `protected_branches` entry. Existing configuration migrates its prior `direct_push_branches` list once to preserve existing local-commit access without granting any new push permission. New registrations initialize the list to the registered develop branch. Wildcards are invalid.

Preserve the existing commit safeguards: repository registration and path validation, clean-tree requirements, commit-author validation before staging, selected-patch HEAD matching, staged-change refusal, and preservation of unselected changes. A branch has no local-commit permission unless it is explicitly listed; no wildcard or PR-route-derived permission is introduced.

## Alternatives

- Keep using `direct_push_branches` for both operations: rejected because granting local commit would also grant direct push.
- Permit commits on every non-protected branch: rejected because it grants an implicit wildcard permission.
- Infer PR source permission from `pull_branches`: rejected because pull permission describes a different operation and does not identify a PR route.

## Consequences

Branches can receive local commits without becoming directly pushable or PR-route sources. After migration, local commit and direct push lists can be changed independently. Existing entries are copied during migration only to preserve their previously granted local-commit access.

## Security conditions

Protected-branch checks remain ahead of commit operations. Push policy remains unchanged. The route source comparison uses the registered repository policy and ordinal branch-name matching; untrusted input cannot choose or expand the route.

## Implementation, tests, and documentation

Both commit APIs use the exact `local_commit_branches` allowlist. Tests verify regular and selected commits on a local-commit-only branch, refusal on a direct-push-only branch, protected-branch refusal, and direct-push refusal on a local-commit-only branch. English and Japanese configuration and command references document the separation.
