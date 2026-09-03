# GitHub Release Provider contract

Githubie maps Moyai's Release lifecycle to native GitHub Releases.

- `github_release_create(repository, version, notes?, artifact_path?, project?)` creates or resumes `v{version}` as a draft Release. The tag must already exist and target the configured tag target branch HEAD.
- `github_release_publish(repository, version, artifact_path?, notes?, project?)` uploads or replaces the optional artifact and publishes the Release.
- `github_release_get(repository, version?, tag?, project?)` reads the Release. Supply exactly one of `version` or `tag`.
- `github_release_withdraw(repository, version, project?)` deletes the Release. The Git tag and its target commit are intentionally retained.

`project`, when supplied by Moyai, must equal `repository`. Versions do not include the `v` prefix. Create and publish are idempotent for matching state; an incompatible Release at the same tag returns `release_already_exists`. Withdraw returns `release_not_found` when absent. After a successful withdraw, get returns `release_not_found` while the tag remains available.

Moyai must map its `artifactPath` provider argument to Githubie's snake-case MCP parameter `artifact_path`. No other Moyai-side behavior change is required.
