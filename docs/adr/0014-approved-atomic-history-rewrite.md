# ADR 0014: Approved atomic history rewrite

## Status

Accepted.

## Context

Published Git history occasionally requires correcting commit metadata. Normal `github_push` deliberately rejects protected branches and non-fast-forward updates, while direct Git or GitHub API use would bypass Githubie's policy and audit boundary.

## Decision

Add a separate destructive MCP tool, `github_history_rewrite`. It accepts only fully qualified `refs/heads/*` and `refs/tags/*` names, a local 40-hex SHA, and the expected remote 40-hex SHA for every ref. Dry-run resolves every local object and queries every remote ref with `git ls-remote --refs`, returning old/new SHA and any lease rejection without mutation.

Every real rewrite requires an out-of-band Windows desktop approval. The LocalSystem service launches `Githubie.ApprovalPrompt` in the active user's session through a one-shot Task Scheduler task and exchanges a secret-free request over a per-request ACL-restricted named pipe. Denial, timeout, missing interactive session, launch failure, and protocol failure all fail closed.

After approval, Githubie queries every remote ref again. Any change aborts the entire operation. The update uses one `git push --atomic`, one explicit `--force-with-lease=<ref>:<old-sha>` per ref, and explicit `<new-sha>:<ref>` refspecs. A remote that cannot provide atomic push is rejected. Existing `github_push` policy is unchanged.

## Alternatives

- Unconditional `--force`: rejected because it can overwrite concurrent remote work.
- Chat-only confirmation: rejected because requester and approver share the same trust boundary.
- Sequential pushes with rollback: rejected because rollback cannot make partially visible ref updates atomic.

## Consequences, security, and operations

- The approval executable is packaged independently because the service runs in Session 0.
- Ref names and SHAs are validated before reaching Git; secrets are never displayed or returned.
- Tests cover dry-run, denial, lease conflict, atomic invocation/failure, and normal protected-branch behavior.
- Operators retain a mirror or backup refs before rewriting. Recovery prepares the backed-up SHAs and runs the same dry-run and approved workflow in reverse. Atomic or permission failure must be corrected before retrying; sequential fallback is forbidden.
