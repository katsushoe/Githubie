# Operations

[English](OPERATIONS.md) | [日本語](OPERATIONS.ja.md)

## Repository registration

Call `github_repository_register` with an unused repository ID and an existing absolute local root. Githubie derives the GitHub identity from the local remote and displays the resulting owner/repository, root, remote, and branch route for desktop approval. After approval, run `githubie auth set <repository>` when network operations need a token, then verify `github_repository_status` and `github_fetch`.

## History rewrite

Retain a repository mirror or `refs/backup/*` before rewriting. Run `github_history_rewrite` with `dry_run=true` and verify every remote SHA, local SHA, and rejection reason. A real update rechecks remote SHAs after desktop approval and aborts on atomic-capability, lease, or permission failure. Recover by selecting the retained SHAs as local targets and running the same dry-run and approved workflow in reverse.

## GitHub Release

Create the version tag at main HEAD first and generate approved assets under the repository local root. MSI, ZIP, `.sha256`, `SHA256SUMS.txt`, and distribution `.ps1` files are accepted. Pass their absolute paths to `github_release_create`. Githubie creates a draft, uploads every missing asset, and publishes only after all uploads succeed. Retrying the same matching draft skips completed uploads. Use `github_release_list`/`get` to inspect state, `github_release_update` for metadata, and `github_release_asset_upload` for later additions; same-name replacement requires `replace_existing=true`.

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

After manually editing `githubie.json`, run `githubie.exe config check` and `githubie.exe restart`. Manual edits are loaded at startup; entries added through `github_repository_register` are applied immediately.

## Backup

Back up `config\githubie.json` and, only when required, `data\secrets\`. DPAPI LocalMachine ciphertext cannot be restored on another machine; register tokens again after migration. Retain `logs\` according to your audit policy.
