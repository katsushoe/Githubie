# ADR 0032: Git remote resolution by repository URL

- Status: Accepted

## Context

Githubie used one fixed remote name per registered repository (normally `origin`) for every Git operation. On 2026-09-27 the Moyai Repository Provider Contract defined a common rule for all Providers ("Gitリモートの解決", Moyai commit `8dc61cc`): a Provider selects the remote whose URL matches the registered repository, Moyai may pass its `gitRemoteName` as the optional tool argument `remote`, and three common error codes describe failures. Buckettie requested the same behavior from Githubie (CR-2026-09-27-provider-git-remote-resolution).

## Decision

Before each operation that contacts the remote (`github_repository_status`, `github_fetch`, `github_pull`, `github_push`, `github_tag_push`, `github_tag_create` local persistence, `github_history_rewrite`), Githubie resolves the remote in this order:

1. The tool argument `remote`, when given.
2. The remote name stored in the registration, when it is not empty.
3. Automatic resolution: `git config --get-regexp ^remote\..*\.url$` lists the remotes, and the HTTPS remotes whose URL points to `github.com/<github_owner>/<github_repo>` are candidates. One candidate is used; with several, exactly one named `github-origin-https` (`<host>-origin-<scheme>`) is used; otherwise the call fails.

A named remote (1 or 2) is verified: a missing or invalid name returns `provider_remote_not_found`, a URL for another repository returns `provider_remote_mismatch`, and an SSH URL returns `provider_remote_not_found` with `error.provider.code` `remote_https_required` (ADR 0021). Automatic resolution returns `provider_remote_not_found` or `provider_remote_ambiguous` (with the matching names in `error.diagnostic`). Githubie never falls back to `origin` or to automatic resolution after a named remote fails.

URL comparison reuses `GitHubRemoteUrlValidator`: `.git` and a trailing `/` are ignored, URLs with credentials, query, or fragment never match, and owner and repository names are compared case-insensitively because GitHub treats them so. SSH remotes are excluded from candidates because Githubie supplies credentials only through its HTTPS AskPass.

The tool argument reaches the Git gateway through an `AsyncLocal` scope (`GitRemoteSelection`), so the gateway interface is unchanged. `github_repository_diff` and `github_repository_commit` accept `remote` for Moyai compatibility but do not contact a remote. `github_provider_capabilities` reports `remote_resolution: { version: 1, mode: "repository_url" }`.

## Alternatives

- Keep the fixed remote: rejected because it contradicts the common Contract and cannot choose among several remotes for the same repository.
- Always auto-resolve and ignore the stored name: rejected because the Contract allows the stored name as the first candidate and existing registrations rely on it.
- Keep the old `remote_mismatch` and `remote_https_required` codes: rejected because Moyai relays the common codes unchanged; the SSH detail stays in `error.provider.code`.

## Impact

Existing registrations keep `origin` and behave as before when it points to the registered repository. `github_repository_status` now fails with the common codes when the stored remote does not match, instead of returning empty remote heads. Registration still requires a named remote because it reads the GitHub owner and repository from that URL; a registration whose stored `remote` is empty uses automatic resolution.
