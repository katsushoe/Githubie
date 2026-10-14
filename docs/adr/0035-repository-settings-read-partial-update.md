# ADR 0035: Registered repository settings read and partial update

## Status

Proposed

## Context

Moyai needs to inspect the registered branch/tag policies and commit-author registration without reading secrets or making a GitHub request. The existing full-policy management tool cannot safely update only author identity because omitted policy values must remain server-owned and current.

## Decision

Add a read-only `github_repository_settings_get` operation that returns an explicit eight-field allowlist from the in-memory registered configuration. It does not access Git, the configuration file, or GitHub. Author null values mean no author identity is registered; effective local Git identity is not included.

Keep `github_repository_update` as the legacy full-policy operation. Add `github_repository_settings_update` with a `changes` object; omitted fields preserve their latest registered values, explicit null and unknown fields are rejected, empty arrays are explicit replacements, and false remains a value. Commit-author name and email are accepted only as a valid pair. The full resulting configuration is validated, human approval is requested, and the configuration store is written once before updating the in-memory allowlist.

Bind the new read and update tools to `repository.read` and `repository.settings.write` respectively. The update scope is reserved for Project-scoped settings management and must not be substituted with ordinary commit or push permissions. The update tool retains interactive approval.

## Consequences

The response cannot disclose repository paths, GitHub identity, workflow input settings, Moyai mappings, or credentials. The update API preserves omitted data without client snapshots. Cross-provider scope and schema agreement and isolated integration testing are required before this decision is accepted or the feature is treated as released.
