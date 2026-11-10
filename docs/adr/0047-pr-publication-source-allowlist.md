# ADR 0047: Independent allowlist for PR source publication

## Status

Regression-verified candidate 2.1.5.0. The requester updated the original CR on2026-11-04 after the user selected independent publication permission (notification99e920f90982464cbf6b196053c29c5f; canonical CR read and validated). Consumer review/interoperability, installation and CR completion remain pending.

## Context

The original Kugutsu CR requires both an independent source allowlist and an exact PR route. ADR0046's route-only candidate 2.1.4.0 does not satisfy that requirement. ADR0039 discarded `local_commit_branches`; restoring its old effect on ordinary commits would change unrelated behavior.

## Decision

Supersede only ADR0046's route-only authorization decision. Add `pr_publication_source_branches` to each registered repository. It defaults to empty, requires valid unique exact local branch names, uses ordinal case-sensitive matching, and never permits a protected source. Publication requires both this list and an exact `pull_request_routes` pair. Revalidate current policy after approval and immediately before each external mutation. Preserve fixed SHA checks, owner binding, approval, audit, creation-only refs and partial/unknown non-replay behavior. Status/replay remains read-only and does not grant permission to resume a mutation.

The new list governs publication only. Ordinary commit and direct-push policy remain unchanged. Legacy `local_commit_branches` is still accepted and discarded, never copied into the new list. Existing databases/configurations without the field deny publication. An explicit empty list disables publication; null, duplicates, patterns, full refs and invalid branch names are rejected.

## Alternatives

- Restore `local_commit_branches`: rejected because it reinstates discarded ordinary-commit restrictions.
- Use PR routes alone: rejected because it loses the original independent source constraint.
- Automatically derive a source list from old values or routes: rejected because old broad values do not establish current publication intent.

## Impact, compatibility and migration

This is the independent publication setting selected in the updated original CR; it replaces the earlier coupled setting requirement. For example, ordinary commits on `docs/other` can remain allowed while publication of that branch is denied, even when its PR route exists. Under the old coupled setting, local commit and publication permission were linked.

For the user-selected `codex/docs-main-v1010`, review an explicit candidate settings update containing that source and the exact destination route. Do not infer the destination or enlarge direct-push permissions. Show the full replacement lists, preserve unrelated intended entries, and obtain the normal human settings approval only when applying to the selected runtime. JSON repository entries seed a new repository database only once; edit registered settings through the approved settings operation for existing registrations. Old versions reject the new JSON field, so retain a pre-change backup and do not feed new configuration/database rows to old binaries. An older binary/database may reject a repository row containing the new field; restore the separately preserved baseline before downgrade.

## Security and operations

Missing settings fail closed. No production configuration, repository policy, GitHub refs, installation or credential changes follow from building this candidate. Preserve frozen 2.1.4.0 artifacts and existing WIP. Configuration still resolves only from the actual installation's `config/githubie.json`; registered policy and operation history stay in the existing per-user repository database.

## Implementation, tests and user documentation

Implement options, JSON validation, registered settings get/update and persistence, MCP schema and CLI forwarding together. Test route-only and list-only rejection, empty/missing/legacy-only values, case sensitivity, protected sources, both allowed, revocation during approval/dispatch, restart/clear persistence and unchanged push policy. Maintain both public guides and a reviewable migration example. Consumer acceptance and requester acceptance are still required before original CR closure.
