# Release Process

[English](RELEASE.md) | [日本語](RELEASE.ja.md)

## Versioning

Githubie uses four-part display versions: `major.minor.patch.revision`. MSI comparison uses the first three parts and permits same-three-part upgrades so revision-only releases can replace an installed build. The current display version is `1.8.4.0`.

## Tags

Create annotated tags in the form `v<display-version>` on the configured `main` branch HEAD.

## Procedure

1. Confirm a clean `develop` branch and passing tests.
2. Update version references and release notes.
3. Build the MSI and ZIP with the scripts documented in [Package layout](PACKAGES.md).
4. Verify filenames, versions, SHA-256 files, package layout, and bundled documentation.
5. Validate MSI install, major upgrade, and uninstall on Windows.
6. Merge through the configured `develop` to `main` pull-request route.
7. Create the tag and GitHub Release, then attach the MSI, ZIP, and SHA-256 files.

## Validation Record

Phase 1 core implementation, Windows execution, live GitHub read/write operations, Windows Service operation, 114 automated tests, and MSI install/upgrade/uninstall were validated for version `1.0.0.0`. ADR 0013 records the MSI directory ACL correction found during that validation.

Version `1.1.0.0` adds the interactively approved, atomic, force-with-lease history rewrite tool and its dedicated approval prompt executable. ADR 0014 records its safety and recovery model.

Version `1.3.0.0` adds out-of-band approved MCP repository registration with GitHub identity derived from the local remote. ADR 0016 records the registration model.

Version `1.3.1.0` fixes MCP tool activation after repository registration support was added by registering the repository registration service in the HTTP host dependency injection container.

Version `1.3.2.0` normalizes Windows repository paths for Git `safe.directory`, allowing approved registration of repositories owned by another Windows account.

Version `1.3.3.0` adds real-Git registration regression tests and secret-free audit logging for repository registration results.

Version `1.3.4.0` routes registered GitHub SSH remotes through HTTPS for PAT-authenticated network operations without changing repository configuration.

Version `1.3.7.0` adds pull-request close/reopen operations and pull-request conversation comment listing/creation through the official GitHub REST API.

Version `1.3.8.0` adds pull-request approval and change-request review submission through the official GitHub REST API.

Version `1.3.9.0` requires HTTPS GitHub remotes so repository configuration matches PAT-authenticated Git transport.

Version `1.3.10.0` adds generic CLI access to every MCP tool through the running server, preserving the same policies, approvals, and audit path.

Version `1.4.0.0` adds retry-safe GitHub Release inspection, update, and asset upload/replacement, expands approved distribution asset types, and adds policy-validated tag deletion.

Version `1.5.0.0` adds repository Description operations, policy-restricted GitHub Actions workflow dispatch and run inspection, and standard-user audit-log ACL support with non-fatal logger failures.

Version `1.6.0.0` adds Server Instructions and the `githubie_usage` MCP prompt so agents receive Githubie's purpose, recommended workflow, and safety constraints from the server. It also allows the first push to create an absent remote branch after existing repository, remote, working-tree, and branch-policy checks pass.

Version `1.6.0.1` aligns pull-request mergeability with the shared retry contract, distinguishes calculating, mergeable, conflicting, blocked, and temporarily unknown states, and adds bounded pre-merge polling.

Version `1.6.0.2` persists repository registrations in SQLite with one-time JSON migration, targets approval prompts to active console or Remote Desktop sessions, and reports malformed prompt responses as unavailable instead of denied.

Version `1.6.0.4` aligns repository IDs with the Itoguruma Project Inbox ID rule, normalizes registration and rename inputs to invariant lowercase, and makes repository lookup case-insensitive across Git, GitHub, AskPass, and token access.

Version `1.6.0.4` passed all 285 automated tests. Its MSI install and upgrade at `C:\Githubie`, preservation of existing configuration and repository data, Windows Service startup, MCP response, and case-insensitive authenticated repository lookup were validated on a Windows machine.

Version `1.6.0.5` improves explicit local tag push diagnostics, distinguishes matching and conflicting remote tags, refuses conflicting remote tag overwrites, and documents that `github_tag_create` publishes a remote annotated tag without creating a local tag.

Version `1.6.0.5` passed all 291 automated tests. Its MSI and Portable ZIP builds and SHA-256 manifests, MSI upgrade at `C:\Githubie`, installed file version, preservation of existing configuration and repository data, Windows Service startup, CLI configuration check, and MCP response were validated on a Windows machine.

Version `1.6.0.6` adds policy-controlled repository working-tree diff and local commit operations, with standard response/error contracts, audit logging, and provider capability reporting.

Version `1.6.0.6` passed all 292 automated tests. Its MSI build and SHA-256 manifest, MSI upgrade at `C:\Githubie`, installed file version, preservation of existing configuration and repository data, Windows Service startup, CLI configuration check, and repository diff/commit MCP schemas were validated on a Windows machine.

Version `1.7.0.0` adds `list_projects` for discovering registered repository IDs, instructs agents to verify the selected project before every push, and returns registered candidates when a push targets an unregistered project.

Version `1.7.0.0` passed all 294 automated tests. Its MSI build and SHA-256 manifest, MSI upgrade at `C:\Githubie`, installed file version, preservation of existing configuration and repository data, Windows Service startup, CLI configuration check, `list_projects` response, and unregistered-push `error.candidates` were validated on a Windows machine.

Version `1.8.0.0` adds optional foreground token setup after repository registration approval, while keeping registration successful when token entry is skipped or storage fails.

Version `1.8.0.0` passed all 296 automated tests. Its MSI build and SHA-256 manifest, MSI upgrade at `C:\Githubie`, installed file version, preservation of existing configuration and repository data, Windows Service startup, CLI configuration check, MCP response, and repository-registration `token_configured`/`token_status` output schema were validated on a Windows machine.

Version `1.8.1.0` extends CLI `tools/call` requests to eleven minutes while retaining five-second status and discovery timeouts, allowing sequential approval and token prompts to complete without client cancellation. All 299 automated tests passed. Its MSI build and SHA-256 manifest, upgrade at `C:\Githubie`, installed file version, automatic Windows Service startup, CLI configuration check, MCP version response, and preservation of registered projects were validated on a Windows machine.

Version `1.8.3.0` also resolves and shows the registered repository URL when `githubie auth set` opens the token dialog. All 301 automated tests passed. Its MSI and portable ZIP builds and SHA-256 manifests, installation at `C:\Githubie`, installed version, automatic Windows Service startup, configuration check, and preservation of registered projects were validated on a Windows machine.

Version `1.8.3.1` records successful live-PAT HTTPS pull and tag-push verification. All 301 automated tests passed. Its MSI build and SHA-256 manifest, same-three-part upgrade at `C:\Githubie`, installed and MCP versions, automatic Windows Service startup, configuration check, preservation of nine registered projects, and manual uninstall/reinstall lifecycle were validated on a Windows machine. Manual uninstall removed the service and process while retaining configuration, database, and secret directories; reinstall recreated and started the service.

Version `1.8.3.2` records the confirmed MSI service-removal lifecycle and the conclusion that no WiX service-control change is required. All 301 automated tests passed. Its MSI build and SHA-256 manifest, same-three-part upgrade at `C:\Githubie`, installed and MCP versions, automatic Windows Service startup, configuration check, and preservation of nine registered projects were validated on a Windows machine.

Version `1.8.4.0` adds read-only GitHub Issue list/get operations to MCP and CLI. All 305 automated tests passed. Its MSI build and SHA-256 manifest, upgrade at `C:\Githubie`, installed and MCP versions, automatic Windows Service startup, configuration check, preservation of nine registered projects, and both Issue tools were validated on a Windows machine.
