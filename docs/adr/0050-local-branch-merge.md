# ADR 0050: Explicit local merge into the current branch

- Status: Accepted

## Context

Kugutsu (CR-2026-11-24-local-branch-merge) had a `develop` → `main` pull request that conflicted in `README.md` and `README.ja.md`. Resolving it requires a merge commit that brings `main` into `develop` locally, but Githubie exposed no operation that merges another branch or commit into a local branch. The shared operating rules forbid falling back to plain Git, so the conflict could only be resolved by a rule exception or by hand on GitHub.

## Decision

Add `github_repository_merge(repository, branch, expected_head, source_kind, source, expected_source_sha, fast_forward, message?, resolutions?)`.

- The source is explicit: `source_kind` is `branch`, `remote_branch` (for example `origin/main`), `tag` or `commit` (a 40-character SHA). Githubie never guesses the kind or completes a name.
- Before any side effect the caller's `branch` must be the attached current branch, `HEAD` must equal `expected_head`, the working tree must be clean including untracked files, no merge, rebase, cherry-pick or revert may be in progress, and the source must resolve to `expected_source_sha`. Branches in `protected_branches` are rejected. No separate allow-list of merge targets is introduced; if one is needed later it will be a new independent setting rather than a reuse of `direct_push_branches`.
- `fast_forward` is explicit: `allow` fast-forwards when possible, `never` always records a merge commit, `only` rejects when a fast-forward is impossible.
- The merge result is computed with `git merge-tree --write-tree`, which does not touch the working tree or index. A conflict therefore leaves the working tree, index and `HEAD` exactly as they were and returns `merge_conflict` with the conflicted paths in `error.candidates`. There is no default that prefers either side.
- Conflicts are resolved only when `resolutions` names every conflicted path with `current`, `source` or `content` (the resolved full text). A missing resolution aborts without committing; a resolution for a path that is not conflicted is rejected.
- On success the new commit is created with `commit-tree`, the working tree is updated with a two-tree `read-tree -m -u` on a private index while Githubie holds `index.lock` and guards `HEAD`, and the branch ref is updated with compare-and-swap against `expected_head`, as local commits already do. If the ref update fails, the working tree is restored.
- The operation never pushes and never calls GitHub. It uses the existing `repository.commit` Provider scope, is listed in the owner-authenticated operations, is announced as `repository_merge: true` in `github_provider_capabilities`, and is recorded in the audit log with the target branch.
- Git 2.38 or later is required for `merge-tree --write-tree`.

## Consequences

Callers such as Moyai and Kugutsu can resolve integration conflicts through Githubie without plain Git. Patch-based resolution is not provided; callers supply the full resolved text instead. Because the merge result is computed off the working tree, line endings of `content` resolutions follow the repository's attributes for the given path.
