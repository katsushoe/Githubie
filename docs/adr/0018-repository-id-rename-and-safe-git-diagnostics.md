# ADR 0018: Repository ID rename and safe Git diagnostics

## Context

Repository IDs key both configuration and DPAPI-encrypted token files. Re-registering under a new ID loses that association. Git failures and dirty-tree results also lacked enough safe information to distinguish common remote rejection and execution-identity differences.

## Decision

Provide CLI and MCP repository rename operations. The encrypted token file is renamed first, configuration keys are replaced by one atomic configuration-file write, and the live allowlist is updated last. Any failure rolls completed steps back to the old ID. Reject invalid IDs, missing old IDs, duplicate new IDs, and missing tokens with fixed codes.

Classify history-rewrite stderr into fixed, non-secret categories for lease conflict, atomic capability, branch protection/rulesets, token repository permission, workflow permission, and authentication. Never return raw stderr. Repository status returns at most 20 porcelain status codes and relative paths, never file contents.

## Status

Accepted.

## Alternatives

Unregister/register was rejected because token identity is lost. Returning raw stderr was rejected because credential-bearing URLs and helper output may contain secrets. Ignoring untracked files for the service was rejected because it weakens clean-tree policy.

## Consequences and controls

Rename requires the source token to exist and leaves no old setting or token after success. Audit records contain old/new IDs, result, and fixed error code only. Tests cover success, missing token, rollback, rejection classification, and bounded path-only dirty diagnostics. CLI/MCP and troubleshooting documents describe the operation and remediation.
