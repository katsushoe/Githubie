# Architecture Decision Records

| ADR | Decision |
| --- | --- |
| [0001](0001-fixed-git-command-gateway.md) | Fixed Git command gateway — one typed method per allowed operation, no arbitrary shell/args |
| [0002](0002-git-askpass-fixed-username.md) | `GIT_ASKPASS` helper with a fixed `x-access-token` username convention |
| [0003](0003-fixed-github-repository-gateway.md) | Fixed GitHub repository gateway — Allowlist resolves owner/repo, PR route is fixed |
| [0004](0004-dpapi-acl-secret-storage.md) | DPAPI (LocalMachine) token storage with ACL-restricted secrets directory |
| [0005](0005-local-streamable-http-mcp-server.md) | Local Streamable HTTP MCP server (initially 15 typed tools) |
| [0006](0006-common-mcp-tool-result.md) | Common `{ ok, operation, repository, data, error }` MCP tool result |
| [0007](0007-structured-audit-log.md) | Structured audit log with ASP.NET Core framework noise filtered out |
| [0008](0008-management-cli-doctor.md) | Management CLI (`githubie.exe`) with `doctor` diagnostics |
| [0009](0009-windows-service-via-sc.md) | Windows Service management via fixed `sc.exe` invocation |
| [0010](0010-git-data-api-tag-creation.md) | Two-step Git Data API tag creation (GitHub has no single-call tag endpoint) |
| [0011](0011-snake-case-tool-parameter-names.md) | snake_case MCP tool parameter names via literal C# identifiers |
| [0012](0012-configuration-only-repository-registration.md) | Superseded Phase 1 configuration-file-only repository registration |
| [0013](0013-msi-directory-acl-grants.md) | Explicit ACL grants on MSI-created directories (fixes `auth set` failing after a fresh install) |
| [0014](0014-approved-atomic-history-rewrite.md) | Out-of-band approved, atomic, force-with-lease history rewrite for explicit refs |
| [0015](0015-draft-first-github-release-publication.md) | Draft-first GitHub Release publication with bounded local assets |
| [0016](0016-approved-mcp-repository-registration.md) | Out-of-band approved MCP registration with GitHub identity derived from the local remote |
| [0023](0023-retry-safe-release-and-tag-management.md) | Retry-safe Release asset management and policy-bound Tag deletion |

ADRs are written in English, matching Buckettie's convention. All other project documentation is in Japanese.
