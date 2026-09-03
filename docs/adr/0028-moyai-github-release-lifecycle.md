# ADR 0028: Moyai-compatible GitHub Release lifecycle

## Status

Accepted

## Decision

Keep the existing GitHub Release tools and extend their contract with Moyai's `version`, `notes`, `artifact_path`, and `project` inputs. A version maps deterministically to tag `v{version}`. Creation always starts as draft, publication is a separate idempotent transition, and withdrawal deletes only the GitHub Release resource.

The existing explicit `tag`, `name`, `body`, `draft`, `prerelease`, and `assets` inputs remain supported for direct clients. Supplying both tag and version is accepted only when they map to the same tag. A supplied project must equal the repository identifier.

## Consequences

Moyai can execute create, publish, get, and withdraw without bypassing Githubie. Release cleanup cannot accidentally remove the associated tag; tag cleanup remains an explicit `github_tag_delete` operation. Moyai must translate its camel-case `artifactPath` field to the public MCP parameter `artifact_path`.
