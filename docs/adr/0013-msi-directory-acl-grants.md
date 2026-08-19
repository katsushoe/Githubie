# ADR 0013: Explicit ACL grants on MSI-created directories

- Status: Accepted

## Context

Real-machine verification of the MSI installer found that a fresh `msiexec /i` install left `%ProgramFiles%\Githubie\data\secrets` (and by the same mechanism, `logs`, `data`, and `config`) with a DACL that blocked even an elevated Administrator from modifying it. `githubie.exe auth set` failed with `[NG] IoError` because `WindowsSecretDirectorySecurity.Ensure()` internally calls `DirectoryInfo.SetAccessControl()`, which throws `UnauthorizedAccessException` (mapped to `IoError`) when the caller lacks `WRITE_DAC` on the target. Folders created by Windows Installer running in its elevated system context can end up without an ACE that grants `WRITE_DAC` to the `Administrators` group, even though the same group can read/write file contents inside `%ProgramFiles%` normally.

## Decision

Grant `Administrators` and `SYSTEM` `GenericAll` explicitly on `CONFIGDIR`, `LOGDIR`, `DATADIR`, and `SECRETSDIR` at install time, via the WiX Util extension's `util:PermissionEx` inside each directory's `<CreateFolder>` element (`installer/Githubie.Installer/Package.wxs`). This guarantees `WRITE_DAC` is present from the moment the MSI creates the folder, so the runtime `WindowsSecretDirectorySecurity.Ensure()` call (which itself disables inheritance and re-grants LocalSystem / Administrators / the current user) can always succeed regardless of which admin account first runs `auth set`.

## Alternatives

- Make `WindowsSecretDirectorySecurity.Ensure()` take ownership of the directory before modifying its ACL (via `SE_TAKE_OWNERSHIP_NAME` privilege and native `AdjustTokenPrivileges` P/Invoke): rejected as significantly more complex or a pure .NET/WiX-only fix, and it would still require the calling account to hold the "Take ownership" privilege, which is not guaranteed for every admin scenario.
- Grant `Everyone: GenericAll`: rejected because it defeats the purpose of Security principle 10 (secrets must not be recoverable through any path other than the intended one).
- Ask the operator to manually run `icacls`/`takeown` after every fresh install: rejected as exactly the kind of manual step Version 1's DPAPI+ACL design (ADR 0004) exists to avoid.

## Impact

`Githubie.Installer.wixproj` now references `WixToolset.Util.wixext`. Any future directory added under `INSTALLROOT` that the application writes to at runtime (as opposed to read-only documentation) must repeat this `util:PermissionEx` pattern or inherit it from a parent that has it.

## Security conditions

Granting `Administrators`/`SYSTEM` `GenericAll` at install time is not a weakening of the design: it only grants the ability to *manage permissions on* the directory, and `WindowsSecretDirectorySecurity.Ensure()` still runs on every `Save()` to reset the ACL to the minimal LocalSystem/Administrators/current-user set described in ADR 0004. No additional principal gains file-content access beyond what ADR 0004 already grants.

## Operational conditions

Verified through the actual MSI lifecycle on real Windows hardware: fresh `/i` install, `auth set` (failed pre-fix with `IoError`, succeeded post-fix), a `MajorUpgrade`-driven in-place upgrade (`/i` over an existing installation with a different rebuilt `ProductCode`), and `/x` uninstall. The uninstall step also revealed that `ServiceControl Remove="uninstall"` did not reliably deregister the Windows Service from the Service Control Manager during a manual `/x` run in this environment; the leftover registration had to be removed with `sc delete` before the service could be re-created pointing at a different `binPath`. This is tracked as a known rough edge for manual uninstall/reinstall cycles rather than fixed in this ADR, since the documented supported upgrade path is the in-place `MajorUpgrade` (`/i` with a newer build), not uninstall-then-reinstall.

## Implementation, tests, and documentation

`installer/Githubie.Installer/Package.wxs`, `installer/Githubie.Installer/Githubie.Installer.wixproj`. `Githubie.Cli.CliApplication.AuthSet`/`ReadMaskedLine` were also hardened during the same verification pass to trim incidental leading/trailing whitespace from pasted tokens and echo the captured character count, after a separate (unrelated) masked-paste input incident was observed while diagnosing this issue.
