# ADR 0015: Draft-first GitHub Release publication

## Status

Accepted.

## Context

Githubie could merge a release PR and create a tag, but operators still needed a separate GitHub client to create the GitHub Release and attach MSI/ZIP checksums. Sequential asset uploads can fail, so publishing the Release before every asset succeeds exposes an incomplete distribution.

## Decision

Add `github_release_create`. The Gateway accepts only an existing policy-compliant tag that targets the configured main branch's current HEAD. Assets are limited to 1–10 unique `.msi`, `.zip`, or `.sha256` files under the allowlisted repository local root.

Githubie always creates the GitHub Release as a draft, uploads every asset through the server-provided HTTPS `uploads.github.com` URL after validating its host and repository path, and publishes only after every upload succeeds. An explicitly requested draft remains unpublished. Upload failure leaves the Release as a draft for operator inspection and never exposes a partial public Release.

## Alternatives

- Publish first and upload afterward: rejected because users could download an incomplete release.
- Accept arbitrary asset paths or URLs: rejected because it widens the filesystem and network trust boundary.
- Use `gh` or direct API calls outside Githubie: rejected because remote GitHub operations must remain policy-controlled and audited.

## Consequences

The configured token needs GitHub Contents write permission sufficient for Releases. Release creation, asset uploads, and publication are tested independently. Results expose release and asset metadata but no local paths or credentials.
