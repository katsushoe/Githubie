# ADR 0004: DPAPI LocalMachine token storage with ACL-restricted directory

- Status: Accepted

## Context

Personal Access Tokens must not be stored in `githubie.json`. Githubie runs both as an interactive CLI session and as a Windows Service under LocalSystem, so any credential store must be readable from both contexts on the same machine. Buckettie's history (its ADR 0001, superseded by its ADR 0010) shows that Windows Credential Manager's user-scoped generic credentials are not readable from a LocalSystem service session.

## Decision

Adopt Windows DPAPI with `DataProtectionScope.LocalMachine` from the start (no user-scoped Credential Manager phase). Each Repository's token is encrypted with a fixed application-specific entropy value and written to `data\secrets\<repository-id>.token` via a temp-file-then-atomic-`File.Move` sequence. The `data\secrets` directory itself has its ACL hardened (`WindowsSecretDirectorySecurity.Ensure`): inheritance is disabled and only LocalSystem, Administrators, and the current user are granted `FullControl`. This directory-level ACL is defense-in-depth beyond DPAPI: even an actor who can read the encrypted bytes off disk cannot do so without also being one of these principals, and the LocalMachine DPAPI scope alone protects the plaintext regardless of file permissions.

## Alternatives

- Windows Credential Manager (user scope): rejected outright based on Buckettie's documented LocalSystem-readability problem; not attempted for Githubie.
- DPAPI with `CurrentUser` scope: rejected for the same reason (unreadable from a different Windows Service logon session).
- No directory ACL hardening (rely on DPAPI alone): rejected because DPAPI protects confidentiality of the *value* but not access to the *file itself*, and a defense-in-depth posture matches Security principle 10 (secrets must not be recoverable through any other path).

## Impact

Githubie has no equivalent of Buckettie's ADR 0001; it starts directly from the ADR 0010-equivalent design.

## Security conditions

- Token bytes are zeroed from memory (`CryptographicOperations.ZeroMemory`) immediately after use in all Save/Read paths.
- Maximum token length (2560 bytes) is enforced before encryption.
- `DpapiFileTokenStore` and `WindowsSecretDirectorySecurity` are marked `[SupportedOSPlatform("windows")]`; all call sites (`GithubieCompositionRoot`, `Githubie.Cli.CliApplication`, `Githubie.AskPass.Program`) guard construction with `OperatingSystem.IsWindows()`.

## Operational conditions

Tokens encrypted under LocalMachine DPAPI on one machine cannot be decrypted after copying `data\secrets` to another machine; re-registration via `auth set` is required after a migration (documented in OPERATIONS.md).

## Implementation, tests, and documentation

`Githubie.Infrastructure.Credentials.DpapiFileTokenStore`, `DpapiProtector`, `WindowsSecretDirectorySecurity`. Verified on real Windows hardware during Phase 1: a live Save → Read → Delete round trip through real DPAPI encryption, and a live ACL inspection confirming `AreAccessRulesProtected` and the current user's `FullControl` rule (`DpapiFileTokenStoreTests`, `WindowsSecretDirectorySecurityTests`).
