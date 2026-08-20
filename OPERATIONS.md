# Operations

[English](OPERATIONS.md) | [日本語](OPERATIONS.ja.md)

## History rewrite

Retain a repository mirror or `refs/backup/*` before rewriting. Run `github_history_rewrite` with `dry_run=true` and verify every remote SHA, local SHA, and rejection reason. A real update rechecks remote SHAs after desktop approval and aborts on atomic-capability, lease, or permission failure. Recover by selecting the retained SHAs as local targets and running the same dry-run and approved workflow in reverse.

## GitHub Release

Create the version tag at main HEAD first and generate MSI/ZIP plus `.sha256` files under the repository local root. Pass their absolute paths to `github_release_create`. Githubie creates a draft, uploads every asset, and publishes only after all uploads succeed. If the operation fails, inspect the retained GitHub draft and correct duplicate assets or permissions before retrying.

## Service Management

```powershell
githubie.exe start
githubie.exe stop
githubie.exe restart
githubie.exe status
```

Install the service first with `githubie.exe service install` when using a portable or source build.

## Logs and Diagnostics

`githubie.exe logs` prints the log directory. Daily files use `<install-root>\logs\githubie-yyyyMMdd.log`. Run `githubie.exe doctor`, followed by `config check`, `repo status`, or `auth test` to isolate failures.

## Token Rotation

Replace a token with `githubie.exe auth set <repository>` and revoke the previous token on GitHub. Remove a stored token with `githubie.exe auth delete <repository>`.

## Configuration Changes

After editing `githubie.json`, run `githubie.exe config check` and `githubie.exe restart`. Configuration is loaded at startup and is not hot-reloaded.

## Backup

Back up `config\githubie.json` and, only when required, `data\secrets\`. DPAPI LocalMachine ciphertext cannot be restored on another machine; register tokens again after migration. Retain `logs\` according to your audit policy.
