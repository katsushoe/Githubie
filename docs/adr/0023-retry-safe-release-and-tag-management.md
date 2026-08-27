# ADR 0023: Retry-safe release assets and policy-bound tag deletion

## Status

Accepted.

## Context

Draft-first release creation prevented partial public releases, but an interrupted upload could not be resumed. Existing releases could not be listed, inspected, updated, or supplied with additional assets. Distribution scripts and checksum manifests were also rejected. A separately approved correction required deletion of a mistargeted tag.

## Decision

Add typed release list/get/update/asset-upload operations and a typed tag-delete operation. Release creation first checks for a matching draft and resumes only that draft; an unrelated or already-public release remains a fixed `release_already_exists` error. Existing asset names are skipped during draft resumption. Direct asset upload rejects name collisions unless replacement is explicitly requested.

Assets remain limited to one through ten unique files under the configured repository root. Allowed types are MSI, ZIP, `.sha256`, the exact `SHA256SUMS.txt` manifest, and PowerShell distribution scripts. Tag deletion validates the configured tag policy before using GitHub's tag-ref endpoint.

## Alternatives

- Permit arbitrary paths or extensions: rejected because it expands the filesystem disclosure boundary.
- Dispatch arbitrary GitHub Actions workflows: rejected because release management now provides the required direct path and arbitrary workflow inputs would add a separate trust boundary.
- Automatically replace same-name assets: rejected because silent replacement can change published binaries.

## Impact

The MCP surface grows by five tools. The generic CLI `mcp call` route exposes the same operations and server-side constraints. Release asset results now include GitHub asset IDs used for explicit replacement.

## Security conditions

Repository allowlisting and stored-token resolution remain mandatory. Responses and audit events contain fixed error codes and metadata only; GitHub error bodies, credentials, and file contents are not exposed. Asset paths must resolve under the configured repository root.

## Operational conditions

Operators should create releases as drafts, upload all assets, and publish only after completion. Retrying the same matching draft skips already uploaded names. Replacing a published asset requires `replace_existing=true`.

## Implementation, tests, and documentation

The Application gateway applies repository and tag policy. Infrastructure owns REST calls, validation, draft resumption, and replacement ordering. MCP schemas, audit wrappers, stable error mapping, English/Japanese command documentation, API tests, and tool-contract tests are updated together.
