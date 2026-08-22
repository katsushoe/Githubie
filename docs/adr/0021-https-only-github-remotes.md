# ADR 0021: HTTPS-only GitHub remotes

## Status

Accepted

## Context

Githubie authenticates Git network operations with a repository-scoped personal access token through a fixed askpass helper. It previously accepted SCP-style SSH remotes and rewrote them to HTTPS only for each Git process. This made the configured transport differ from the actual transport and supported only one SSH spelling.

## Decision

Githubie accepts only `https://github.com/OWNER/REPOSITORY.git` remotes for registration and all Git operations. GitHub SSH remotes return the stable `remote_https_required` error. Existing repository configuration and Git remotes are never rewritten automatically; an operator must explicitly run `git remote set-url` before retrying.

The process-local SSH-to-HTTPS rewrite is removed. Owner and repository validation remains mandatory before every operation.

## Consequences

The configured URL, authentication mechanism, and actual transport now agree. SSH keys and SSH-specific environment behavior are outside Githubie's trust boundary. Existing SSH-configured repositories require a one-time explicit migration.
