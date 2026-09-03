# ADR 0010: Two-step Git Data API tag creation

- Status: Accepted

## Context

Bitbucket Cloud's REST API creates an annotated tag with a single POST call. GitHub's REST API has no equivalent single-call tag creation endpoint; annotated tags require building a Git object graph.

## Decision

`github_tag_create` resolves its explicit source to a commit SHA as specified by ADR 0027, validates the tag name against `RepositoryPolicy.ValidateTag`, then performs two sequential GitHub API calls: `POST /repos/{owner}/{repo}/git/tags` to create the annotated tag object (returning a `sha` distinct from the target commit SHA), followed by `POST /repos/{owner}/{repo}/git/refs` with `ref = refs/tags/<tag>` and `sha = <tag object sha>` to publish the reference. If the ref-creation step fails with `422 Unprocessable Entity`, the error is mapped to `tag_already_exists`.

## Alternatives

- Lightweight tag only (ref pointing directly at the commit, skipping the tag object step): rejected because Buckettie's semantics (and the `tagger`/`message` fields Githubie's `GitHubTagInfo` model exposes) assume an annotated tag; a lightweight tag would silently drop the message.
- GitHub Releases API (`POST /repos/{owner}/{repo}/releases`, which implicitly creates a tag): deferred to Phase 2 (`github_release_create`) because it couples tag creation to release note management, which is out of Phase 1 scope.

## Impact

`github_tag_create` performs three sequential HTTP calls (branch lookup, tag object, ref) instead of Bitbucket's one, increasing latency and the number of distinct failure points.

## Security conditions

Superseded by ADR 0027: the client must explicitly provide a literal branch or verified full commit SHA; omission never selects a configured default.

## Operational conditions

None beyond standard GitHub API rate limits (three calls per tag creation instead of one).

## Implementation, tests, and documentation

`Githubie.Infrastructure.GitHub.GitHubApiClient.CreateTagAsync`, `GitHubRepositoryGateway.CreateTagAsync`. Not yet exercised end-to-end against a live GitHub repository (requires a Personal Access Token); read paths (`GetTagAsync`'s two-step ref → tag-object resolution) share the same Git Data API mechanics and are implemented but likewise untested live pending Token registration.
