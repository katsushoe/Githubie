# ADR 0033: Approved Moyai project mapping without restart

- Status: Accepted

## Context

In Moyai integration mode, Githubie accepts a Moyai Provider Assertion only for a repository mapped to a Moyai Project UUID. The only mapping source was the administrator-owned `provider_authentication.projects` in `githubie.json`, which is read at startup. Registering a repository in both Githubie and Moyai therefore still left Moyai requests rejected with `auth_project_mismatch` until someone edited the file and restarted the service with administrator rights (aloe_lang CR-2026-10-08-moyai-project-uuid-auto-mapping; the same manual step was needed for KotodamaSpecGuard).

## Decision

Store an optional Moyai Project UUID with each repository registration (`moyai_project_id` in the registration database) and set it through the existing approval boundary:

1. `github_repository_register` accepts an optional `moyai_project_id`, shown in the registration approval.
2. `github_repository_moyai_project_set(repository, moyai_project_id)` maps an already registered repository after its own desktop approval. Setting the current value is a no-op without a prompt.

Before the approval prompt, both reject an empty UUID (`invalid_moyai_project_id`), a UUID already mapped to another repository in either source, and a UUID that differs from an administrator mapping for the same repository (`duplicate_moyai_project_id`); `github_repository_moyai_project_set` also rejects an unregistered repository. The change is saved and applied to the live allowlist, so the next Moyai request uses it without a restart. The tool is audited with the UUID.

During assertion validation the administrator mapping in `provider_authentication.projects` is used first; the registered mapping is used only when the administrator mapping has no entry for the repository. Assertion claims still never create or change a mapping, and the tools stay outside the repository-scoped assertion policy, like registration, because a mapping cannot be authorized by an assertion that depends on it. The startup warning for unmapped repositories counts both sources.

## Alternatives

- Write the mapping into `githubie.json` and reload it: rejected because the service would rewrite an administrator-owned file and the file is not otherwise reloaded at runtime.
- Accept the mapping from a Moyai-signed request: rejected because Moyai would be authorizing the binding that later authorizes Moyai, and no Moyai change is needed with the approval-based path.
- Map automatically by matching the repository URL with Moyai's project list: rejected because it would trust remote data to create an authorization binding without a human decision.

## Impact

A new Moyai-integrated repository needs only Moyai `project_create` followed by Githubie registration with `moyai_project_id` (or `github_repository_moyai_project_set` for an existing registration). Existing administrator mappings keep working unchanged and still win on conflict.
