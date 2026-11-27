using System.Text.Json.Nodes;
using Githubie.Application.Interactive;
using Githubie.Application.Repositories;

namespace Githubie.Application.GitHub;

/// <summary>承認前後の状態を確認し、許可されたRepositoryの保護設定だけを変更します。</summary>
public sealed class GitHubBranchProtectionGateway(
    RepositoryAllowlist allowlist,
    IGitHubApiClient gitHubApi,
    IGitHubBranchProtectionBackend backend,
    IInteractiveApprovalPrompt approvalPrompt) : IGitHubBranchProtectionGateway
{
    private static readonly SemaphoreSlim MutationLock = new(1, 1);
    private static readonly TimeSpan ApprovalTimeout = TimeSpan.FromMinutes(5);
    private const int MaxApprovalTextLength = 8192;

    public async Task<GitHubResult<GitHubBranchProtectionSnapshot>> GetAsync(string repository, string branch, CancellationToken cancellationToken)
    {
        if (!GitHubProtectionBranchName.IsValid(branch)) return GitHubResult<GitHubBranchProtectionSnapshot>.Failure(GitHubError.BranchProtectionInvalidInput);
        if (!allowlist.TryGet(repository, out var options)) return GitHubResult<GitHubBranchProtectionSnapshot>.Failure(GitHubError.RepositoryNotFound);
        return await backend.ReadAsync(repository, options.GitHubOwner, options.GitHubRepo, branch, cancellationToken);
    }

    public async Task<GitHubResult<GitHubBranchProtectionMutation>> UpdateAsync(
        string repository, string branch, GitHubBranchProtectionEdit edit, bool createIfMissing, CancellationToken cancellationToken)
    {
        if (!GitHubProtectionBranchName.IsValid(branch) || edit is null || !edit.IsValid())
            return GitHubResult<GitHubBranchProtectionMutation>.Failure(GitHubError.BranchProtectionInvalidInput);
        if (!allowlist.TryGet(repository, out var options)) return GitHubResult<GitHubBranchProtectionMutation>.Failure(GitHubError.RepositoryNotFound);

        await MutationLock.WaitAsync(cancellationToken);
        try
        {
            var current = await backend.ReadAsync(repository, options.GitHubOwner, options.GitHubRepo, branch, cancellationToken);
            GitHubBranchProtectionSnapshot? snapshot;
            if (current.IsSuccess) snapshot = current.Value!;
            else if (createIfMissing && current.Error == GitHubError.BranchProtectionNotFoundOrInaccessible)
            {
                var branchState = await gitHubApi.GetBranchAsync(repository, options.GitHubOwner, options.GitHubRepo, branch, cancellationToken);
                if (!branchState.IsSuccess) return SanitizedFailure<GitHubBranchProtectionMutation, GitHubBranchInfo>(branchState);
                if (branchState.Value!.Protected) return GitHubResult<GitHubBranchProtectionMutation>.Failure(GitHubError.BranchProtectionUnsupportedPolicy);
                snapshot = null;
            }
            else return GitHubResult<GitHubBranchProtectionMutation>.FailureFrom(current);

            var prepared = backend.Prepare(snapshot, edit);
            if (!prepared.IsSuccess) return GitHubResult<GitHubBranchProtectionMutation>.FailureFrom(prepared);
            var plan = prepared.Value!;
            var unchanged = snapshot is not null && JsonNode.DeepEquals(plan.Before, plan.After);

            var approval = await ApproveAsync(repository, options.GitHubOwner, options.GitHubRepo, branch,
                snapshot is null ? "create" : "update", plan.Before, plan.After, cancellationToken);
            if (approval is not null) return GitHubResult<GitHubBranchProtectionMutation>.Failure(approval.Value);

            var stable = await IsUnchangedAsync(repository, options.GitHubOwner, options.GitHubRepo, branch, plan.RawBefore, cancellationToken);
            if (!stable.IsSuccess) return GitHubResult<GitHubBranchProtectionMutation>.FailureFrom(stable);
            if (unchanged) return GitHubResult<GitHubBranchProtectionMutation>.Success(new(branch, false, plan.After));
            var written = await backend.PutAsync(repository, options.GitHubOwner, options.GitHubRepo, branch, plan.After, cancellationToken);
            if (!written.IsSuccess) return GitHubResult<GitHubBranchProtectionMutation>.FailureFrom(written);
            return GitHubResult<GitHubBranchProtectionMutation>.Success(new(branch, true, plan.After));
        }
        finally { MutationLock.Release(); }
    }

    public async Task<GitHubResult<GitHubBranchProtectionMutation>> DeleteAsync(string repository, string branch, CancellationToken cancellationToken)
    {
        if (!GitHubProtectionBranchName.IsValid(branch)) return GitHubResult<GitHubBranchProtectionMutation>.Failure(GitHubError.BranchProtectionInvalidInput);
        if (!allowlist.TryGet(repository, out var options)) return GitHubResult<GitHubBranchProtectionMutation>.Failure(GitHubError.RepositoryNotFound);
        await MutationLock.WaitAsync(cancellationToken);
        try
        {
            var current = await backend.ReadAsync(repository, options.GitHubOwner, options.GitHubRepo, branch, cancellationToken);
            if (!current.IsSuccess) return GitHubResult<GitHubBranchProtectionMutation>.FailureFrom(current);
            if (!current.Value!.CanUpdate || current.Value.Policy is null)
                return GitHubResult<GitHubBranchProtectionMutation>.Failure(GitHubError.BranchProtectionUnsupportedPolicy);
            var approval = await ApproveAsync(repository, options.GitHubOwner, options.GitHubRepo, branch,
                "delete", current.Value.Policy, null, cancellationToken);
            if (approval is not null) return GitHubResult<GitHubBranchProtectionMutation>.Failure(approval.Value);
            var stable = await IsUnchangedAsync(repository, options.GitHubOwner, options.GitHubRepo, branch, current.Value.Raw, cancellationToken);
            if (!stable.IsSuccess) return GitHubResult<GitHubBranchProtectionMutation>.FailureFrom(stable);
            var deleted = await backend.DeleteAsync(repository, options.GitHubOwner, options.GitHubRepo, branch, cancellationToken);
            if (!deleted.IsSuccess) return GitHubResult<GitHubBranchProtectionMutation>.FailureFrom(deleted);
            return GitHubResult<GitHubBranchProtectionMutation>.Success(new(branch, true, null));
        }
        finally { MutationLock.Release(); }
    }

    private async Task<GitHubResult<bool>> IsUnchangedAsync(
        string repository, string owner, string repo, string branch, JsonObject? expectedRaw, CancellationToken cancellationToken)
    {
        var current = await backend.ReadAsync(repository, owner, repo, branch, cancellationToken);
        if (expectedRaw is null && current.Error == GitHubError.BranchProtectionNotFoundOrInaccessible)
        {
            var branchState = await gitHubApi.GetBranchAsync(repository, owner, repo, branch, cancellationToken);
            if (!branchState.IsSuccess) return SanitizedFailure<bool, GitHubBranchInfo>(branchState);
            return !branchState.Value!.Protected
                ? GitHubResult<bool>.Success(true)
                : GitHubResult<bool>.Failure(GitHubError.BranchProtectionChanged);
        }
        if (!current.IsSuccess)
            return current.Error == GitHubError.BranchProtectionNotFoundOrInaccessible
                ? GitHubResult<bool>.Failure(GitHubError.BranchProtectionChanged)
                : GitHubResult<bool>.FailureFrom(current);
        return expectedRaw is not null && JsonNode.DeepEquals(expectedRaw, current.Value!.Raw)
            ? GitHubResult<bool>.Success(true)
            : GitHubResult<bool>.Failure(GitHubError.BranchProtectionChanged);
    }

    private async Task<GitHubError?> ApproveAsync(
        string repository, string owner, string repo, string branch, string operation,
        JsonObject? before, JsonObject? after, CancellationToken cancellationToken)
    {
        var beforeText = before?.ToJsonString() ?? "(no classic rule observed: GitHub 404 may also indicate inaccessible protection)";
        var afterText = after?.ToJsonString() ?? "(delete classic rule)";
        if (beforeText.Length + afterText.Length > MaxApprovalTextLength) return GitHubError.BranchProtectionUnsupportedPolicy;
        var result = await approvalPrompt.RequestApprovalAsync(new ApprovalPromptRequest(
            "Githubie GitHub branch protection",
            $"{operation} protection for '{repository}' ({owner}/{repo}, branch '{branch}')",
            ["Target: " + repository + " (" + owner + "/" + repo + ", branch " + branch + ")",
             "Operation: " + operation, "Before: " + beforeText, "After: " + afterText,
             "GitHub does not provide a documented atomic precondition against changes made by other clients."]),
            ApprovalTimeout, cancellationToken);
        return result.Outcome switch
        {
            ApprovalOutcome.Approved => null,
            ApprovalOutcome.Denied => GitHubError.BranchProtectionApprovalDenied,
            ApprovalOutcome.TimedOut => GitHubError.BranchProtectionApprovalTimedOut,
            _ => GitHubError.BranchProtectionApprovalUnavailable,
        };
    }

    private static GitHubResult<T> SanitizedFailure<T, TSource>(GitHubResult<TSource> result) =>
        GitHubResult<T>.Failure(result.Error ?? GitHubError.ApiError, httpStatus: result.HttpStatus);
}
