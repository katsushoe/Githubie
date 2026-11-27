# ADR 0044: GitHub branch protection through Githubie

## Status

Proposed, 2026-10-28. The implementation and its verification are in progress. This document does not claim that the currently installed Githubie or Moyai supports this contract.

## Context

Githubie's `protected_branches` is a local policy for operations routed through Githubie; GitHub branch protection is a separate server-side rule. The requested feature must offer authenticated read, update and delete through MCP and CLI, and require interactive Desktop approval for each mutation. GitHub's classic branch-protection PUT requires several fields including explicit nulls and replaces supplied arrays. An incomplete translation can unintentionally remove existing reviews, restrictions or checks. GitHub rulesets are a separate mechanism and cannot be inferred from the classic-rule endpoint alone.

## Decision under implementation

- Resolve owner/repository only from the registered allowlist and validate an exact branch name; reject wildcards and unsafe paths. Use the existing token store and GitHub API client.
- Provide separate read, update and delete operations, with explicit capability declarations only after the operations are complete. Existing `protected_branches` enforcement remains independent.
- Treat a PUT as a potentially destructive policy replacement. Require explicit desired state and an interactive Desktop approval that identifies the repository, branch, operation and relevant policy changes. Preserve policy fields outside the requested changes; if preservation cannot be proven, reject before PUT instead of silently weakening the rule. Prevent this process's own concurrent writes to the same rule; document that GitHub's API provides no documented atomic precondition against external writers.
- A missing or malformed GET result, a 404, a 403 and an unknown mutation result must not be silently converted into an unprotected state or retried as a write. A post-dispatch transport failure or ambiguous response requires a fresh read and manual reconciliation.
- Expose distinct permission and plan errors only when the upstream evidence establishes the cause. A generic 403 does not by itself prove which cause applies; return an indeterminate denial with a useful next action rather than guess. Limit diagnostics and do not log or return tokens, authorization headers or raw upstream error content.
- The existing owner-session policy requires the same Windows owner and session generation. Legacy signed mode requires an exact operation and dedicated scope; neither mode substitutes GitHub Administration permission for owner approval. The same application operation is exposed through the generic MCP CLI route and audited at the server boundary.

## Alternatives

- Sending only the user-visible subset in a PUT could silently remove unrelated protection. Rejected; preserve verified settings or fail closed.
- Inferring a free-plan limitation from every 403 could misdiagnose missing permission. Rejected; preserve the ambiguous result when evidence is insufficient.
- Using a GitHub ruleset or direct external API as a fallback changes scope or bypasses the configured Provider path. Rejected; classic branch protection is the initial requested API and rulesets remain distinct.

## Effects and validation

Tests must cover preservation of unrelated rules, nulls, unsupported response fields, approval refusal/unavailability, permissions/plan ambiguity, no automatic mutation replay, redaction, MCP/CLI parity and the two authentication modes. English and Japanese user documentation must match the final behavior. Unit tests, an isolated Consumer/Provider acceptance, and actual GitHub policy changes are separate milestones. No existing Repository settings, rules, live credentials or frozen candidate packages are changed by this proposal.

## Source

GitHub's [branch protection REST documentation](https://docs.github.com/en/rest/branches/branch-protection) describes the required PUT fields, replacement semantics and Administration permissions. Its documented HTTP status alone does not reliably identify a plan limitation versus missing permission.
