# Release Process

[English](RELEASE.md) | [日本語](RELEASE.ja.md)

## Versioning

Githubie uses four-part display versions: `major.minor.patch.revision`. MSI comparison uses the first three parts and permits same-three-part upgrades so revision-only releases can replace an installed build. The current display version is `1.6.0.1`.

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
