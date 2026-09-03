# ADR 0026: Explicit source for branch creation

## Status

Accepted, 2026-09-04. Supersedes implicit MainBranch selection for branch creation.

## Context

Githubie selected MainBranch while Buckettie was reported to select DevelopBranch. A caller could not express the intended history. The agreed common specification requires an explicit source and an error when it is omitted.

## Decision

`github_branch_create(repository, branch, source)` requires `source`: a branch name or a full 40-character hexadecimal commit SHA in the same remote repository. Full SHAs are resolved as commits; other nonblank values are resolved as literal branch names. Tags and revision expressions are not supported. A 40-character hexadecimal branch name can be addressed by a different branch name pointing to the same commit or by its commit SHA.

No MainBranch, DevelopBranch, HEAD, or other default is substituted. MCP rejects a missing required argument; null, empty, or whitespace source values reaching Application return `branch_source_invalid`. Missing source branches return `branch_not_found`; missing commits return `branch_source_not_found`. Resolution failures never invoke ref creation.

The CLI uses the same server contract through `mcp call` (ADR 0022). Moyai callers must pass `source` unchanged; upgrading a caller is required before it can use this breaking contract. Existing checkout is a separate operation and is not added here.

## Alternatives

- Default to main or develop: rejected because it silently chooses history.
- Use the current HEAD: rejected because implicit state must not replace caller intent.
- Separate optional branch/SHA fields: not selected; a single required source keeps omission visible in the MCP input schema.

## Impact and security

MCP, Application, API resolution, audit forwarding, CLI help, and tests are updated together. Repository registration, credentials, destination branch allowlists, and provider permissions remain enforced. Resolution is read-only; only the explicit resolved SHA is sent to ref creation. Local branches and working trees are not changed. No installation or release is part of this change.

## Verification and operations

Tests cover initial develop from main, nondefault branches, explicit commits, missing/blank source, resolution failures, destination policy, MCP schema and forwarding, API requests, and CLI JSON forwarding. COMMANDS.md documents migration and examples. Runtime rollout and cross-product integration require separate verification; passing local tests does not imply deployment.
