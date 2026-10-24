# ADR 0040: User session host and fresh user profiles

## Status

Accepted for implementation. Installation and live acceptance remain pending.

## Context

CR-2026-10-22-githubie-user-process-autostart-tray requires the host, tray and consent UI to belong to the logged-on user. Loopback TCP does not isolate Windows users. The legacy registration path saves settings before requesting a token. The user selected a fresh profile with repository and credential re-registration on 2026-10-22.

## Decision

- Keep machine binaries in `C:\Githubie`. Store each user's config, SQLite, logs and credentials under the Windows LocalApplicationData known folder, `Akatsukisoft/Githubie`. Never import legacy Service data or machine DPAPI secrets automatically. Preserve the old installation data for recovery.
- Run HTTP host and tray in one user process. Stop on logoff with bounded shutdown. Consent and token UI launch only in that process's user/session, with SID-limited named pipes. Do not enumerate or launch into another active session.
- Rotate a random HTTP session credential on each host start, protect it with CurrentUser DPAPI and owner-only profile ACL, and remove discovery state on shutdown. Discovery binds profile ID, SID, session, process and port. Port or session collisions fail closed. Validate Host and Origin in addition to client credentials. Moyai requests continue to require the existing ES256 assertion and capability/project checks; metadata bootstrap exposes no repository data.
- Native CLI and a stdio MCP bridge obtain the current user's connection through the protected profile/pipe. They never open the repository database for business commands. No tokens in arguments, client configuration, output or logs.
- For new registration, request consent and token before persistence. An explicit skip permits a local-only registration; cancel, launch/protocol failure and encryption/persistence failure leave no registration. Commit settings and encrypted token in one SQLite transaction, with an insert-only repository boundary. Existing registrations survive failures unchanged.
- Installation must complete current-user startup registration during setup, default ON only for a fresh profile, preserving an existing OFF preference. A non-elevated setup coordinator captures the intended SID and invokes MSI elevation separately; it then performs and reads back that user's registration. A different UAC administrator must not become the profile owner. Silent install requires an explicit intended-user coordinator context. Cancellation or failure cannot report complete user setup. MSI alone installs binaries and reports user setup outstanding; it must not claim the CR's complete-install contract.

## Alternatives

- Retaining LocalSystem and selecting an active desktop was rejected because it crosses user/session boundaries.
- Copying shared state to each user was rejected because DPAPI decryptability does not establish authority to transfer credentials.
- Separate database and token-file writes were rejected because process termination between them leaves partial registration.

## Impact, security and operations

This is a major-version transition. Legacy service startup and new user-host startup must not run concurrently against the same endpoint. Recovery uses the preserved legacy profile and matching legacy MSI; it does not translate new CurrentUser secrets back to machine scope. No live migration or release is implied by implementing this ADR.

## Validation and documentation

Cover profile isolation, DPAPI scope, unauthorized HTTP/IPC, credential rotation, same-user dialogs and cancellation, startup preference readback, rollback, registration transaction failure, existing registration preservation, logoff shutdown and MSI acceptance. Update English/Japanese installation, configuration, CLI and recovery documentation before deployment.
