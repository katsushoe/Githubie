using System.Text.Json.Nodes;
using Githubie.Application.GitHub;

namespace Githubie.Infrastructure.GitHub;

/// <summary>GitHub応答を保全可能なポリシーに変換する非公開transport境界です。</summary>
public sealed class GitHubBranchProtectionBackend(GitHubApiClient client) : IGitHubBranchProtectionBackend
{
    public async Task<GitHubResult<GitHubBranchProtectionSnapshot>> ReadAsync(
        string repository, string owner, string repo, string branch, CancellationToken cancellationToken)
    {
        var result = await client.ReadBranchProtectionAsync(repository, owner, repo, branch, cancellationToken);
        if (!result.IsSuccess)
        {
            if (result.HttpStatus == 404)
                return GitHubResult<GitHubBranchProtectionSnapshot>.Failure(GitHubError.BranchProtectionNotFoundOrInaccessible, httpStatus: 404);
            return GitHubResult<GitHubBranchProtectionSnapshot>.FailureFrom(result);
        }

        var raw = result.Value!;
        var canUpdate = GitHubBranchProtectionPayload.TryNormalize(raw, out var policy);
        return GitHubResult<GitHubBranchProtectionSnapshot>.Success(new(branch, canUpdate, policy, raw));
    }

    public GitHubResult<GitHubBranchProtectionPlan> Prepare(GitHubBranchProtectionSnapshot? current, GitHubBranchProtectionEdit edit)
    {
        if (!edit.IsValid()) return GitHubResult<GitHubBranchProtectionPlan>.Failure(GitHubError.BranchProtectionInvalidInput);
        if (current is not null && !current.CanUpdate) return GitHubResult<GitHubBranchProtectionPlan>.Failure(GitHubError.BranchProtectionUnsupportedPolicy);
        if (!GitHubBranchProtectionPayload.TryBuild(current?.Raw, edit, out var after))
            return GitHubResult<GitHubBranchProtectionPlan>.Failure(GitHubError.BranchProtectionUnsupportedPolicy);
        return GitHubResult<GitHubBranchProtectionPlan>.Success(new(
            current?.Policy?.DeepClone().AsObject(), after!, current?.Raw.DeepClone().AsObject()));
    }

    public Task<GitHubResult<bool>> PutAsync(string repository, string owner, string repo, string branch, JsonObject approvedPayload, CancellationToken cancellationToken) =>
        client.PutBranchProtectionAsync(repository, owner, repo, branch, approvedPayload, cancellationToken);

    public Task<GitHubResult<bool>> DeleteAsync(string repository, string owner, string repo, string branch, CancellationToken cancellationToken) =>
        client.DeleteBranchProtectionAsync(repository, owner, repo, branch, cancellationToken);
}
