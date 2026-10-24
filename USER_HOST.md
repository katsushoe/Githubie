# User host (2.0)

[English](USER_HOST.md) | [日本語](USER_HOST.ja.md)

Githubie 2.0 runs the HTTP host and tray in one signed-in user's process. Machine binaries stay in `C:\Githubie\bin`; configuration, repositories, credentials and logs belong to that user's profile. A matching 1.x MSI and the preserved machine data provide recovery to the old Service model.

## Install and upgrade

Run the versioned package's coordinator from a **non-elevated** PowerShell window as the intended user:

```powershell
.\Install-UserHost.ps1 -MsiPath .\Githubie-2.0.0.0-win-x64.msi
```

Only MSI execution requests UAC elevation. Another administrator may approve UAC; user setup resumes under the original SID and Windows session. Fresh setup enables sign-in startup and reads the entry back. Upgrade/repair preserves a saved OFF preference. A rejected UAC prompt, MSI failure, incorrect installed version, user-context change or failed startup readback reports incomplete setup. MSI binary installation alone is not complete user setup; a fresh MSI requires the coordinator's pending-user-setup properties.

For managed deployment, the administrator may stage MSI binaries with `GITHUBIE_USER_SETUP_PENDING=1 GITHUBIE_TARGET_SID=<intended SID>`. Finish the same deployment by running `Install-UserHost.ps1 -UserSetupOnly -Quiet` in that intended non-elevated user's session. Session 0/System installation cannot register another user's HKCU startup. Do not mark a managed installation complete until the user phase succeeds. `-Quiet` does not start the current host; the sign-in entry is still registered and verified.

Upgrade removes the legacy `Githubie` Windows Service. It preserves the legacy `C:\Githubie\config`, `data`, encrypted token files and logs. **A fresh user profile does not import any of them.** Re-register repositories and re-enter tokens through the dedicated UI. Do not copy the old shared database or machine-scope DPAPI blobs into a user profile.

## Profile and host operations

The profile root is the Windows LocalApplicationData known folder plus `Akatsukisoft\Githubie` (normally `%LOCALAPPDATA%\Akatsukisoft\Githubie`).

| Item | Location / behavior |
| --- | --- |
| Identity | `profile.json`: schema, profile UUID and owner SID |
| Host settings | `config\host.json`: `moyai_integration`, `direct_unrestricted` |
| HTTP settings | `config\githubie.json`: own port/path and optional Provider trust configuration |
| Startup preference | `config\startup.json`: persisted `enabled` flag |
| Repositories and protected tokens | `data\githubie.db`, single SQLite transaction for registration |
| Connection discovery | `data\connection.dpapi`, protected and rotated each host start |
| Logs / readiness | `logs`, `data\host-state.json` |

A fresh profile selects an available loopback port and stores it. Binding a port later owned by another process fails closed; it never adopts that listener. One profile can be hosted by one Windows session at a time. Another session must wait for the owning host to stop/log off. Discovery checks SID, profile, session, PID and process start time.

```powershell
githubie profile status
githubie host start
githubie host status
githubie host endpoint
githubie autostart off
githubie autostart on
githubie host restart
githubie host stop
githubie logs
```

Startup toggles affect the next sign-in and persist independently of the current host's lifetime. Stopping/restarting does not change the preference. A stale sign-in entry is reconciled with the saved preference before starting business services. The tray offers the same startup toggle, restart and stop. Logoff cancels the host and owned dialogs, with a bounded shutdown.

## Codex and Claude Code

Use the stdio bridge; it reads the current user's protected discovery and adds the HTTP credential internally. No PAT or session credential is written to client settings or stdout.

```powershell
githubie configure codex
githubie configure claude
# Explicitly replace only the documented credential-free 1.x loopback entry:
githubie configure codex --replace-legacy
githubie unconfigure codex
githubie unconfigure claude
```

Configuration is user scoped: Codex `config.toml` under its configuration home (`CODEX_HOME` when set), and Claude Code's user `~/.claude.json` `mcpServers` object. The registered command is the installed `githubie.exe` with `mcp stdio`. Existing unrelated settings and other MCP servers are preserved. Matching owned entries are idempotent; foreign entries, malformed files, secret-bearing legacy transport entries and observed concurrent edits are preserved with an error. The writer validates TOML/JSON and stages a complete replacement before atomic publication. Close other configuration writers while changing registration.

Restart/reconnect the AI client after registration. Project or managed settings may override user settings; Githubie does not alter those scopes. `unconfigure` removes only this installation's matching command/arguments. If the AI client is absent and has no user configuration, configure reports an error without creating its settings.

The bridge preserves JSON-RPC IDs, notifications, SSE responses and cancellation. Killing/closing the bridge cancels its outstanding HTTP calls; it does not stop the host. Mutations are never automatically retried after a transport failure. Inspect the operation's state before retrying.

See the official [Codex MCP documentation](https://learn.chatgpt.com/docs/extend/mcp?surface=cli) and [Claude Code MCP documentation](https://code.claude.com/docs/en/mcp) for client scope and precedence.

## Repository registration and credentials

Invoke `github_repository_register` through MCP or `githubie mcp call github_repository_register '<JSON arguments>'`. Consent and token screens launch in the host's own user/session; they do not select another active desktop. Token input uses the dedicated UI. An explicit **Register without token** action creates a local-only registration. Cancel/close, rejected consent, launch/protocol/timeout failure, encryption failure or persistence failure leaves no new registration. Settings and encrypted token commit together; duplicate IDs do not overwrite existing data. Rename/unregister update the user database and credential IDs in one transaction.

`githubie auth set <repository>` opens the host's token UI. Cancel or save failure preserves an existing token. `auth delete` and `auth test` also go through the host; the CLI does not open the repository database. Keep tokens out of command arguments, JSON and logs. The old `--console` token path is a 1.x feature, not a 2.0 user-host input method.

GitHub repository creation involves an external API and cannot be a local SQLite transaction. If GitHub creation succeeds but local linking/registration fails, the result identifies the created repository. Inspect that result and remote state; do not blindly retry or delete a remote repository as an implicit rollback.

## HTTP, IPC and Moyai

Loopback is not user authentication. Business HTTP calls require the random per-start `X-Githubie-Session` credential and valid Host/Origin. Only non-repository bootstrap metadata is anonymous. Credentials are protected with CurrentUser DPAPI and private profile ACLs; legacy machine-format blobs are not imported. The same-user control pipe additionally checks the kernel-reported client Windows session and per-start credential.

Moyai mode is enabled in `config\host.json` and requires the existing `provider_authentication` issuer/trust/replay/project settings in `githubie.json`. ES256 assertion validation, replay protection, operation binding and direct read-only mode remain enforced. A valid session credential does not bypass those checks. Known signed Provider tools may enter the assertion validator; an arbitrary Bearer header never grants a new tool permission.

A user-session Moyai client must use the matching user's discovery/credential for unsigned discovery and other direct calls, and its signed assertion for protected Provider operations. The old shared LocalSystem client's access is not silently inherited. Rebind the endpoint/trust/project mappings for the fresh profile and validate the consumer before switching normal workflows. Do not add a protected connection credential to a shared machine configuration or print it via a helper.

## Uninstall and recovery

Before removing binaries, run `unconfigure` for the AI clients you configured, stop the user host and set autostart OFF. Remove the installed MSI with administrator approval. User and legacy data are retained; other users' profiles are not modified.

To recover the previous Service deployment, stop the new user host, disable its sign-in startup, remove the 2.0 MSI, then install the saved matching 1.x MSI with its previous Moyai/direct-mode choices. Verify the restored version, Service readiness and old data before reconnecting clients. Never copy new CurrentUser credentials back into machine storage. Retain the user profile separately for a later retry.

## Acceptance

Deployment acceptance includes fresh install, upgrade/repair preserving OFF, alternate-administrator UAC, cancellation/failure, user-phase completion readback, uninstall/reinstall/recovery, sign-in/logoff, tray actions and same-session dialogs. Verify consent/token success, skip, cancel, launch/timeout/protocol failure, transaction rollback and existing registration preservation. Test unauthorized HTTP/IPC, port/session conflicts, credential rotation, and representative read/write/error behavior with both Codex and Claude Code plus the intended Moyai consumer. Building an MSI does not by itself establish those live results.
