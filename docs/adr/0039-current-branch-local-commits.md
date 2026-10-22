# ADR 0039: Current-branch local commits

## Status

Accepted for CR-2026-10-21-current-branch-local-commit on 2026-10-22. Supersedes local commit restrictions inferred from PR routes and protected branches. Implementation and acceptance are tracked separately in the private progress record.

## Context

Ordinary local commits record changes on the branch already checked out. Reusing push, PR-route or protected-branch permissions prevents that operation on unrelated branches. Removing those checks alone leaves a race between Gateway validation and Git execution. The old selected-commit implementation updates symbolic HEAD and then resets the real index, which can affect a different branch after concurrent checkout or fail after the ref has already changed.

## Decision

- Both ordinary and selected commits require a registered, path-validated repository, existing invocation authorization, valid author, and an attached current branch. They do not consult branch allowlists, PR routes or protected branches. Return the actual recorded `branch` and `commit_sha`. Push, merge, release, branch switching and history rewrite retain their own policies.
- Capture branch and HEAD at the Gateway. On Windows, hold a read handle on the per-worktree HEAD file with FileShare.Read, denying writes/deletion/replacement while permitting Git reads and Git-owned HEAD.lock. Hold the real index.lock exclusively and recheck branch/HEAD before publication. Explicit-ref compare-and-swap updates only refs/heads/<captured-branch> with its expected old object. On platforms without this tested Windows sharing guarantee, fail closed with atomic_not_supported. No caller-selected ref or symbolic HEAD is updated.
- Build both kinds of commit with a private index and commit-tree. Ordinary commit starts from the real index and adds the current worktree; selected commit starts from the expected HEAD and applies only its patch. Reject existing staged changes for selected commits. Git object creation does not update refs. The working tree is never written. Unborn attached branches are supported with an empty expected HEAD; detached HEAD is rejected.
- Prepare and flush the resulting index before publishing the branch ref. Install it atomically while holding the index lock. On index installation failure, compare-and-swap rollback only our own ref update; if rollback cannot be established, return `commit_result_unknown` and require inspection without automatic retry. Leave other branches and remote refs unchanged.
- Reject an in-progress merge/rebase/cherry-pick/revert instead of inventing parentage. The fixed commit-tree pipeline, like existing selected commits, does not run repository hooks or automatic signing; explicit merge workflows and signing/hook policy are separate operations, not silently emulated by this service.
- `local_commit_branches` is not a normal-commit permission. Add a compatibility-only JSON member that accepts legacy arrays but discards them and omits them when saving. Legacy SQLite columns are left in place; neither can gate commits. The field is not part of the Settings read/update contract. No removal of stored user policy is required.

## Alternatives

- Add develop to an allowlist: rejected because it does not implement arbitrary currently checked-out branches.
- Remove only Gateway checks: rejected because branch/HEAD changes during execution can redirect writes.
- Update symbolic HEAD and reset the real index afterwards: rejected because checkout can change its target, and failure can leave staging altered.
- Hold HEAD.lock outside Git: rejected after real-Git tests showed that Git must acquire this lock itself. The Windows HEAD read handle blocks replacement without occupying HEAD.lock.
- Use symref-verify transactions: rejected for the deployed Git compatibility baseline after actual Git rejected that command; no unsupported or unsafe transaction fallback is used.

## Security and operations

Moyai assertions, exact tool binding, direct read-only mode, repository/path boundaries, author validation and audit remain enforced. The private index preserves staged contents until success and all unselected worktree edits. Stale or foreign lock files are never removed. A hard process crash across ref/index publication is not a filesystem-wide transaction; inspect HEAD, index and reflog before recovery. Do not automatically retry a result marked unknown.

## Implementation, tests and documentation

Use one shared guarded commit implementation behind CLI/MCP Gateway entry points, typed detached/branch-change/HEAD-change/busy/unknown errors, and additive branch output. Verify develop/main/arbitrary branches, empty or unrelated policy lists, unborn/detached HEAD, equal-SHA branch races, changed-head CAS, locks, invalid patches, staged/index preservation, author identity and unchanged unrelated refs. Keep push/merge/history authorization regression tests. Update bilingual configuration/command/troubleshooting references, tool descriptions and versioned local packages together.

## References

- [Git update-ref: explicit old-object comparison](https://git-scm.com/docs/git-update-ref)
- [Git commit-tree](https://git-scm.com/docs/git-commit-tree)
- [Git per-worktree metadata paths](https://git-scm.com/docs/git-rev-parse)
