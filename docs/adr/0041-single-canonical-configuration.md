# ADR 0041: One machine configuration file

Status: Accepted, 2026-10-25, by explicit user instruction. This supersedes ADR0040's per-profile application/host/startup configuration decision.

## Context

The user requires `<actual install root>\config\githubie.json` to be the only operational configuration, explicitly including automatic startup and host integration modes. Multiple profile settings would make the selected file ambiguous.

The user clarified on 2026-10-25 that C:\Githubie is a default, not a fixed location; the user may choose any installation directory. This correction replaces the earlier absolute-path interpretation.

## Decision

- Production host and CLI resolve only `<actual install root>\config\githubie.json`. Omitting the path uses it; an alternative explicit path is rejected. The root is derived from the executing binary directory through GithubiePathLayout.FromBinDirectory. No profile, environment-variable or working-directory fallback is allowed. Internal isolated test entry points inject disposable configuration and are not CLI options.
- The same JSON contains user_host.moyai_integration=false, user_host.direct_unrestricted=false and user_host.autostart_enabled=true by default. direct_unrestricted requires moyai_integration. Legacy host flags may only repeat enabled configuration values, never override them.
- Remove creation/read/write of per-profile githubie.json, host.json and startup.json. Old files remain untouched but are not consumed or silently merged. Profile identity, repositories, CurrentUser-protected credentials, discovery and logs remain per-user data, not alternate configuration files. Existing machine repository declarations are the initial seed for a fresh user's repository database; machine database/token blobs are not imported.
- Autostart initialization reads the shared preference and applies it to the current user's HKCU entry. Changing the preference requires write access to the central file. It atomically changes only the user_host setting while preserving repository/trust/other JSON content, verifies the HKCU write and restores it if publication fails. Initialization does not require a configuration write. Other users apply the shared preference on their next startup/setup; their HKCU is not edited by another user's process.
- The coordinator accepts -InstallRoot; MSI accepts INSTALLROOT, remembers it in the 64-bit HKLM installation metadata, and restores it for upgrades unless a new location is explicitly supplied. User setup uses that recorded location, never a fixed C drive. Runtime uses its own executable location, not the registry, PATH or current working directory. MSI creates a default central file on fresh installation, never overwrites an existing one and retains it on uninstall. Defaults are local-only, startup enabled. Configuration permissions are not broadened to allow every user to modify Provider trust or policy.

## Alternatives

Per-user application/host/startup files and configurable runtime paths were rejected because the user explicitly selected a single file including startup/modes. Automatically merging old profile preferences was rejected because different users may have conflicting values and the canonical file must remain authoritative.

## Security and operations

Shared settings do not transfer credentials or bypass owner/generation and ES256 checks. A missing, unreadable or invalid central file fails without adopting an old profile file. Insufficient central write access leaves config and HKCU unchanged; an authorized administrator can edit the canonical JSON. Existing running 1.x binaries do not understand user_host and must not receive the new schema before the matching binary cutover. Installation, active Service/config updates and secret migration are separate operations.

## Validation and documentation

Test canonical path resolution and alternate rejection, combined JSON round-trip, missing/invalid settings, preserved unrelated fields, startup readback/publication failure rollback and profile initialization without alternate files. Keep CONFIG/USER_HOST/COMMANDS/README in English and Japanese consistent with the installer. Candidate version2.0.0.2 uses MSI ProductVersion2.0.2; no live deployment is implied.
