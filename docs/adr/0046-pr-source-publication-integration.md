# ADR 0046: PR source publication in the current user host

## Status

The route-only authorization decision below is superseded by [ADR0047](0047-pr-publication-source-allowlist.md) for candidate2.1.5.0. Other lifecycle and recovery decisions remain. Candidate2.1.4.0 is historical evidence, not proof of the original independent-source requirement.

Proposed implementation candidate for CR-2026-10-18-pr-source-branch-publish. Live deployment and Consumer acceptance remain separate gates.

## Context

The earlier isolated publication implementation predates ADR0039, the canonical installation configuration, Windows owner authentication, and conditional PR merge. Applying its old snapshot would restore discarded settings and remove current behavior.

## Decision

- Add explicit `github_pr_publish_create` and read-only `github_pr_publish_status`. Existing PR creation never publishes a branch implicitly.
- Authorize publication with an exact configured `pull_request_routes` source/destination pair, reject protected sources, retain clean-tree policy and owner approval. Ordinary push permissions are unchanged. Do not restore `local_commit_branches`: ADR0039 accepts that obsolete setting only for compatibility and discards it.
- Validate an explicit immutable source SHA before and after approval and before PR creation. Create only an absent remote source ref, or reuse the same SHA; reject every differing existing SHA including a possible fast-forward. Do not delete a branch as automatic recovery.
- Journal each operation in the current host's repository database before dispatch. Preserve publication and PR states on partial failure or unknown outcomes. Same-ID replay reconciles reads; it never resumes a mutation. Unknown operations on the same source cannot be bypassed with a new ID.
- Bind owner-mode records to the verified Windows owner, repository and local root, independently of unused legacy project/signature configuration. Legacy mode retains project binding and both signed create scopes. Keep current execution guards, session/generation authentication and audit.
- Reuse `SourceCommitSha` from the existing PR model and add head/base repository identity for publication proof. Preserve existing PR merge behavior.
- Both CLI entry points send publication requests through the existing user-host transport. Configuration resolves solely from the actual executable installation root. The current profile database stores operation data, never a second configuration.

## Alternatives

- Apply the old full snapshot: rejected because it would replace subsequent host, configuration and policy changes.
- Expand direct-push permissions: rejected because publication is an explicitly approved PR operation with creation-only transport.
- Reintroduce the discarded local-commit allowlist: rejected because it conflicts with current configuration and ADR0039. Publication remains constrained by explicit PR routes and protected-source checks.

## Security and operations

Retain operation IDs, expected SHAs, independent effect states, safe diagnostics and audit correlation. Never log descriptions, tokens, assertions or raw exception content. No automatic mutation retry, remote cleanup, installation or public release follows from this integration. Frozen prior candidates remain historical evidence.

## Implementation, tests and documentation

Integrate service, journal, creation-only transport, API proof metadata, composition, authentication operation table, MCP and CLI together. Verify route/protection/SHA/approval failures, durable partial and unknown outcomes, owner-mode binding, legacy scopes, CLI forwarding, existing owner/merge/branch-protection behavior and journal restart/locking. Publish a distinct local candidate and WiX MSI after regression. Update English/Japanese contracts together; Consumer and real-GitHub acceptance are not replaced by local tests.
