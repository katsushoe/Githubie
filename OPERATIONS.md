# Operations

[English](OPERATIONS.md) | [日本語](OPERATIONS.ja.md)

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
