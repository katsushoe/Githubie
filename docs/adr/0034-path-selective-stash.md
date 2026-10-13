# ADR 0034: Path-selective local Git stash

## Status

Accepted

## Context

Local changes need a safe way to be split into commits while keeping unrelated files in place. The existing Git gateway executes only typed operations against allowlisted repositories.

## Decision

Add typed stash push/list/apply/drop operations to the Git gateway and expose them through MCP and the generic CLI MCP caller. Push accepts repository-relative paths, passes them as literal pathspecs after `--`, and includes selected untracked files. Omitted paths intentionally means all changes. Reject rooted paths, traversal, `.git` segments, and pathspec namespace syntax. Apply/drop require a full stash object ID; apply restores the index state. Audit records the operation and repository without logging paths.

Selection is path-granular and cannot split hunks within one file.

## Consequences

Every operation remains governed by the repository allowlist and audited gateway. Provider capabilities advertise the four operations, and fixed assertion scopes grant read for listing and `repository.stash.write` for mutations.
