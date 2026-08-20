# Release Process

[English](RELEASE.md) | [日本語](RELEASE.ja.md)

## Versioning

Githubie uses four-part display versions: `major.minor.patch.revision`. MSI comparison uses the first three parts. The current display version is `1.3.5.2`.

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
