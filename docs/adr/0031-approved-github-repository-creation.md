# ADR 0031: Approved GitHub repository creation

- Status: Accepted

## Context

Githubie registered only repositories that already had a local `origin` pointing to GitHub. A new local repository therefore had to be published through `gh` or the GitHub API directly, which the shared rules forbid when Githubie or Moyai manages GitHub operations. Hataori requested a Githubie operation that creates a GitHub repository and, optionally, links and registers an existing local repository (CR-2026-09-24-repository-create).

## Decision

Add `github_repository_create(repository, owner, name, visibility?, description?, local_root?, develop_branch?, main_branch?, commit_author_name?, commit_author_email?)`. The operation:

1. Validates the new Githubie repository ID, the GitHub owner and repository name, and `visibility` (`private` by default, or `public`). With `local_root`, it validates the path like registration and requires that `origin` is not configured; it never rewrites an existing remote.
2. Shows the interactive desktop approval with the owner, name, visibility, description, local root, and branches before any token prompt or GitHub call.
3. Uses the stored token for the repository ID, or opens the token dialog and saves the entered token. A token saved by this call is deleted when the GitHub call fails.
4. Reads the authenticated login (`GET /user`). When it matches `owner`, the repository is created with `POST /user/repos`; otherwise with `POST /orgs/{owner}/repos`. `auto_init` is false so the local history can be pushed unchanged.
5. With `local_root`, adds `origin` as `https://github.com/{owner}/{name}.git` and registers the repository without a second approval or token prompt, using the same defaults and commit-author resolution as registration.

Distinct typed errors are returned for a name that already exists (`repository_already_exists`, detected from GitHub's 422 `errors[].message`), missing permission (`permission_denied`, GitHub 403/404), invalid token (`authentication_failed`), denied approval (`approval_denied`), an existing `origin` (`remote_already_configured`), and a missing token (`token_unavailable`). GitHub's rejection reason is returned in `error.diagnostic`, using the diagnostic contract introduced in 1.8.9.5.

## Alternatives

- Create through `gh repo create`: rejected because Githubie must not depend on an interactive external CLI or bypass its approval and token boundary.
- Require a separate `github_repository_register` call after creation: rejected because it repeats the approval and token prompt for one user intent.
- Overwrite an existing `origin`: rejected because it can silently redirect pushes of an existing repository.
- Create an initial commit on GitHub (`auto_init`): rejected because it would make the first push of an existing local history non-fast-forward.

## Impact

Moyai can delegate repository creation because `provider_capabilities` reports `repository_create: true`. The tool is outside the repository-scoped Provider Assertion policy, like registration, because no repository ID exists before creation; the interactive approval is its authorization boundary in both standalone and Moyai integration modes. Pushing the local history remains a separate `github_push`.

## Security conditions

The approval appears before any token prompt or GitHub request. Tokens are read from and written to the existing DPAPI store only, never returned or logged. Remote URLs are built from validated owner and name values and passed to Git as separate arguments. Audit records contain the owner/name, result, error code, and GitHub diagnostic, not credentials.

## Operational conditions

A repository created without `local_root` is not registered; its token remains stored under the repository ID so a later `github_repository_register` with the same ID can use it. If the GitHub repository is created but linking or persistence fails, the error diagnostic states that the GitHub repository exists so the operator can finish with `github_repository_register`.

## Implementation, tests, and documentation

`RepositoryRegistrationService.CreateAsync`, `IGitHubApiClient.GetAuthenticatedUserLoginAsync` / `CreateRepositoryAsync`, and `IGitCommandClient.AddRemoteAsync` implement the flow. Tests cover the user and organization endpoints, request body, duplicate name, permission and authentication failures, denied approval without GitHub calls, an existing `origin`, stored and entered tokens, input validation, and registration after linking. `COMMANDS` and `TROUBLESHOOTING` describe the tool and errors.
