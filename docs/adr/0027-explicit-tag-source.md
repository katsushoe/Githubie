# ADR 0027: Explicit tag source

- Status: Accepted

## Context

Selecting `tag_target_branch` implicitly can create a release tag on a different commit from the one the caller reviewed. Repository defaults also vary, so omission cannot express a safe intent.

## Decision

`github_tag_create(repository, tag, source, message?)` requires `source`. A full 40-character hexadecimal value is verified as a commit in the same GitHub repository. Any other accepted value is resolved as a literal branch name. Tags, abbreviated SHAs, and revision expressions are not supported. Blank or invalid values return `tag_source_invalid`; a missing branch or commit returns `tag_source_not_found`. Neither error creates a tag or falls back to `main`, `develop`, or `tag_target_branch`.

The existing repository allowlist, tag-name policy, authentication, and two-step annotated-tag publication remain unchanged. The audit event includes the supplied source and the typed result.

## Consequences

Clients must state the reviewed branch or exact commit. Creating a release still independently enforces that the existing tag points to the current configured `tag_target_branch` HEAD, so a tag created from another commit cannot be published as a release under the current policy.

## Verification

Application tests cover exact SHA resolution, explicit branch resolution, blank and revision-expression rejection, missing sources, and absence of fallback or tag creation. CLI isolation tests verify that generic `mcp call` preserves the `source` argument and propagates server failure.
