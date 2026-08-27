# ADR 0025: SQLite repository configuration store

- Status: Accepted

## Context

Repository registration and policy were persisted by replacing the complete `repositories` object in `githubie.json`. Each process retained the object loaded at startup, so a later write from another CLI or Server process could replace registrations written after that snapshot. Runtime repository mutation requires process-safe, row-level persistence while Personal Access Tokens remain protected by the existing DPAPI boundary.

## Decision

Store repository registrations and policies in `<install-root>\data\githubie.db`. Use `Microsoft.Data.Sqlite` directly, one row per repository ID, parameterized SQL, explicit transactions for schema migration and rename, a five-second busy timeout, and WAL journaling. Keep MCP endpoint startup settings in `githubie.json` and keep Personal Access Tokens in DPAPI-encrypted files.

On the first database initialization, import the validated `repositories` object from `githubie.json` in the same transaction that records the `legacy_json_import_v1` migration marker. Existing database rows win through `INSERT OR IGNORE`. Later startups never re-import JSON repository entries. The legacy JSON remains unchanged as a rollback and migration record; runtime repository commands and `repo list` use SQLite as the source of truth.

## Alternatives

- Add a process-wide lock around JSON replacement: rejected because it still requires whole-document read-modify-write coordination and makes schema evolution fragile.
- Store Personal Access Tokens in SQLite: rejected because it provides no security benefit over the existing ACL-restricted DPAPI files and increases database compromise impact.
- Normalize every policy collection into separate tables: rejected because repository policy is replaced as one aggregate and does not need independent SQL queries.

## Impact

Concurrent processes update independent repository rows without losing unrelated registrations. The database, WAL, and shared-memory files become operational data. Manual changes to `githubie.json` repository entries are import-only after the migration marker exists; approved management operations must be used for later changes.

## Security conditions

The database remains under the ACL-restricted data directory and contains no tokens. SQL values are parameterized. Repository option JSON is strict-deserialized at startup, and database initialization failure prevents the service from starting with an empty or partial allowlist.

## Operational conditions

Backups must include `data\githubie.db` and any present `githubie.db-wal`/`githubie.db-shm` files from a consistent stopped-service snapshot. Upgrades preserve the data directory. Removing the database intentionally causes the next startup to perform the one-time legacy JSON import again into a new database.

## Implementation, tests, and documentation

Infrastructure owns schema initialization, migration, serialization, and row mutations. The Composition Root replaces the JSON repositories with database rows before constructing the allowlist. Tests cover one-time import, independent multi-instance writes, deletion, and atomic rename collision behavior. Configuration and operations documentation identify SQLite as the repository source of truth and DPAPI files as the credential store.
