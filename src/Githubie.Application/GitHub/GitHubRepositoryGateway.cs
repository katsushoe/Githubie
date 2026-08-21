using Githubie.Application.Repositories;

namespace Githubie.Application.GitHub;

/// <summary>
/// Repository Allowlistの解決とRepository Policyの適用を行ったうえで<see cref="IGitHubApiClient"/>を呼び出します。
/// PRのsource/destinationおよびTagの対象branchはAgentに自由指定させず、設定から決定します。
/// </summary>
public sealed class GitHubRepositoryGateway(RepositoryAllowlist allowlist, IGitHubApiClient apiClient) : IGitHubRepositoryGateway
{
    private readonly RepositoryAllowlist _allowlist = allowlist;
    private readonly IGitHubApiClient _apiClient = apiClient;

    public async Task<GitHubResult<IReadOnlyList<GitHubBranchInfo>>> ListBranchesAsync(string repository, CancellationToken cancellationToken)
    {
        var resolved = Resolve(repository);
        if (resolved.Error is not null)
        {
            return GitHubResult<IReadOnlyList<GitHubBranchInfo>>.Failure(resolved.Error.Value);
        }

        return await _apiClient.ListBranchesAsync(repository, resolved.Options!.GitHubOwner, resolved.Options.GitHubRepo, cancellationToken);
    }

    public async Task<GitHubResult<GitHubBranchInfo>> GetBranchAsync(string repository, string branch, CancellationToken cancellationToken)
    {
        var resolved = Resolve(repository);
        if (resolved.Error is not null)
        {
            return GitHubResult<GitHubBranchInfo>.Failure(resolved.Error.Value);
        }

        return await _apiClient.GetBranchAsync(repository, resolved.Options!.GitHubOwner, resolved.Options.GitHubRepo, branch, cancellationToken);
    }

    public async Task<GitHubResult<IReadOnlyList<GitHubPullRequestInfo>>> ListPullRequestsAsync(
        string repository, GitHubPullRequestState? state, string? source, string? destination, CancellationToken cancellationToken)
    {
        var resolved = Resolve(repository);
        if (resolved.Error is not null)
        {
            return GitHubResult<IReadOnlyList<GitHubPullRequestInfo>>.Failure(resolved.Error.Value);
        }

        return await _apiClient.ListPullRequestsAsync(repository, resolved.Options!.GitHubOwner, resolved.Options.GitHubRepo, state, source, destination, cancellationToken);
    }

    public async Task<GitHubResult<GitHubPullRequestInfo>> GetPullRequestAsync(string repository, int number, CancellationToken cancellationToken)
    {
        var resolved = Resolve(repository);
        if (resolved.Error is not null)
        {
            return GitHubResult<GitHubPullRequestInfo>.Failure(resolved.Error.Value);
        }

        return await _apiClient.GetPullRequestAsync(repository, resolved.Options!.GitHubOwner, resolved.Options.GitHubRepo, number, cancellationToken);
    }

    public async Task<GitHubResult<GitHubPullRequestDiff>> GetPullRequestDiffAsync(string repository, int number, CancellationToken cancellationToken)
    {
        var resolved = Resolve(repository);
        if (resolved.Error is not null)
        {
            return GitHubResult<GitHubPullRequestDiff>.Failure(resolved.Error.Value);
        }

        return await _apiClient.GetPullRequestDiffAsync(repository, resolved.Options!.GitHubOwner, resolved.Options.GitHubRepo, number, cancellationToken);
    }

    public async Task<GitHubResult<GitHubPullRequestInfo>> CreatePullRequestAsync(
        string repository, GitHubPullRequestCreate request, CancellationToken cancellationToken)
    {
        var resolved = Resolve(repository);
        if (resolved.Error is not null)
        {
            return GitHubResult<GitHubPullRequestInfo>.Failure(resolved.Error.Value);
        }

        var options = resolved.Options!;
        var policy = options.ToPolicy(repository);
        var routeResult = policy.ValidatePullRequest(options.DevelopBranch, options.MainBranch);
        if (!routeResult.IsAllowed)
        {
            return GitHubResult<GitHubPullRequestInfo>.Failure(GitHubError.PullRequestRouteNotAllowed);
        }

        return await _apiClient.CreatePullRequestAsync(
            repository, options.GitHubOwner, options.GitHubRepo, options.DevelopBranch, options.MainBranch, request, cancellationToken);
    }

    public async Task<GitHubResult<GitHubPullRequestInfo>> MergePullRequestAsync(
        string repository, GitHubPullRequestMerge request, CancellationToken cancellationToken)
    {
        var resolved = Resolve(repository);
        if (resolved.Error is not null)
        {
            return GitHubResult<GitHubPullRequestInfo>.Failure(resolved.Error.Value);
        }

        var options = resolved.Options!;

        var current = await _apiClient.GetPullRequestAsync(repository, options.GitHubOwner, options.GitHubRepo, request.Number, cancellationToken);
        if (!current.IsSuccess)
        {
            return current;
        }

        if (current.Value!.State != GitHubPullRequestState.Open)
        {
            return GitHubResult<GitHubPullRequestInfo>.Failure(GitHubError.PullRequestNotOpen);
        }

        var policy = options.ToPolicy(repository);
        var routeResult = policy.ValidatePullRequest(current.Value.Source, current.Value.Destination);
        if (!routeResult.IsAllowed)
        {
            return GitHubResult<GitHubPullRequestInfo>.Failure(GitHubError.PullRequestRouteNotAllowed);
        }

        return await _apiClient.MergePullRequestAsync(repository, options.GitHubOwner, options.GitHubRepo, request, cancellationToken);
    }

    public Task<GitHubResult<GitHubPullRequestInfo>> ClosePullRequestAsync(
        string repository, int number, CancellationToken cancellationToken) =>
        ChangePullRequestStateAsync(repository, number, GitHubPullRequestState.Closed, cancellationToken);

    public Task<GitHubResult<GitHubPullRequestInfo>> ReopenPullRequestAsync(
        string repository, int number, CancellationToken cancellationToken) =>
        ChangePullRequestStateAsync(repository, number, GitHubPullRequestState.Open, cancellationToken);

    public async Task<GitHubResult<IReadOnlyList<GitHubPullRequestComment>>> ListPullRequestCommentsAsync(
        string repository, int number, CancellationToken cancellationToken)
    {
        var resolved = Resolve(repository);
        if (resolved.Error is not null) return GitHubResult<IReadOnlyList<GitHubPullRequestComment>>.Failure(resolved.Error.Value);
        var options = resolved.Options!;
        var pullRequest = await _apiClient.GetPullRequestAsync(
            repository, options.GitHubOwner, options.GitHubRepo, number, cancellationToken);
        if (!pullRequest.IsSuccess) return GitHubResult<IReadOnlyList<GitHubPullRequestComment>>.Failure(pullRequest.Error!.Value);
        return await _apiClient.ListPullRequestCommentsAsync(
            repository, options.GitHubOwner, options.GitHubRepo, number, cancellationToken);
    }

    public async Task<GitHubResult<GitHubPullRequestComment>> CreatePullRequestCommentAsync(
        string repository, int number, string body, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(body) || body.Length > 65536)
            return GitHubResult<GitHubPullRequestComment>.Failure(GitHubError.PullRequestCommentInvalid);
        var resolved = Resolve(repository);
        if (resolved.Error is not null) return GitHubResult<GitHubPullRequestComment>.Failure(resolved.Error.Value);
        var options = resolved.Options!;
        var pullRequest = await _apiClient.GetPullRequestAsync(
            repository, options.GitHubOwner, options.GitHubRepo, number, cancellationToken);
        if (!pullRequest.IsSuccess) return GitHubResult<GitHubPullRequestComment>.Failure(pullRequest.Error!.Value);
        return await _apiClient.CreatePullRequestCommentAsync(
            repository, options.GitHubOwner, options.GitHubRepo, number, body, cancellationToken);
    }

    private async Task<GitHubResult<GitHubPullRequestInfo>> ChangePullRequestStateAsync(
        string repository, int number, GitHubPullRequestState target, CancellationToken cancellationToken)
    {
        var resolved = Resolve(repository);
        if (resolved.Error is not null) return GitHubResult<GitHubPullRequestInfo>.Failure(resolved.Error.Value);
        var options = resolved.Options!;
        var current = await _apiClient.GetPullRequestAsync(
            repository, options.GitHubOwner, options.GitHubRepo, number, cancellationToken);
        if (!current.IsSuccess) return current;
        if (current.Value!.State == GitHubPullRequestState.Merged)
            return GitHubResult<GitHubPullRequestInfo>.Failure(GitHubError.PullRequestStateNotAllowed);
        if (current.Value.State == target) return current;
        return await _apiClient.UpdatePullRequestStateAsync(
            repository, options.GitHubOwner, options.GitHubRepo, number, target, cancellationToken);
    }

    public async Task<GitHubResult<IReadOnlyList<GitHubTagInfo>>> ListTagsAsync(string repository, CancellationToken cancellationToken)
    {
        var resolved = Resolve(repository);
        if (resolved.Error is not null)
        {
            return GitHubResult<IReadOnlyList<GitHubTagInfo>>.Failure(resolved.Error.Value);
        }

        return await _apiClient.ListTagsAsync(repository, resolved.Options!.GitHubOwner, resolved.Options.GitHubRepo, cancellationToken);
    }

    public async Task<GitHubResult<GitHubTagInfo>> GetTagAsync(string repository, string tag, CancellationToken cancellationToken)
    {
        var resolved = Resolve(repository);
        if (resolved.Error is not null)
        {
            return GitHubResult<GitHubTagInfo>.Failure(resolved.Error.Value);
        }

        return await _apiClient.GetTagAsync(repository, resolved.Options!.GitHubOwner, resolved.Options.GitHubRepo, tag, cancellationToken);
    }

    public async Task<GitHubResult<GitHubTagInfo>> CreateTagAsync(string repository, string tag, string? message, CancellationToken cancellationToken)
    {
        var resolved = Resolve(repository);
        if (resolved.Error is not null)
        {
            return GitHubResult<GitHubTagInfo>.Failure(resolved.Error.Value);
        }

        var options = resolved.Options!;
        var policy = options.ToPolicy(repository);
        var tagPolicyResult = policy.ValidateTag(tag, options.TagTargetBranch);
        if (!tagPolicyResult.IsAllowed)
        {
            return GitHubResult<GitHubTagInfo>.Failure(MapPolicyError(tagPolicyResult.ErrorCode!.Value));
        }

        var targetBranch = await _apiClient.GetBranchAsync(repository, options.GitHubOwner, options.GitHubRepo, options.TagTargetBranch, cancellationToken);
        if (!targetBranch.IsSuccess)
        {
            return GitHubResult<GitHubTagInfo>.Failure(targetBranch.Error!.Value);
        }

        return await _apiClient.CreateTagAsync(
            repository,
            options.GitHubOwner,
            options.GitHubRepo,
            new GitHubTagCreate(tag, targetBranch.Value!.HeadSha, message),
            cancellationToken);
    }

    public async Task<GitHubResult<GitHubReleaseInfo>> CreateReleaseAsync(
        string repository,
        GitHubReleaseCreate request,
        CancellationToken cancellationToken)
    {
        var resolved = Resolve(repository);
        if (resolved.Error is not null) return GitHubResult<GitHubReleaseInfo>.Failure(resolved.Error.Value);

        var options = resolved.Options!;
        var policy = options.ToPolicy(repository);
        var tagPolicy = policy.ValidateTag(request.Tag, options.TagTargetBranch);
        if (!tagPolicy.IsAllowed) return GitHubResult<GitHubReleaseInfo>.Failure(MapPolicyError(tagPolicy.ErrorCode!.Value));

        var tag = await _apiClient.GetTagAsync(repository, options.GitHubOwner, options.GitHubRepo, request.Tag, cancellationToken);
        if (!tag.IsSuccess) return GitHubResult<GitHubReleaseInfo>.Failure(tag.Error!.Value);
        var main = await _apiClient.GetBranchAsync(repository, options.GitHubOwner, options.GitHubRepo, options.TagTargetBranch, cancellationToken);
        if (!main.IsSuccess) return GitHubResult<GitHubReleaseInfo>.Failure(main.Error!.Value);
        if (!string.Equals(tag.Value!.TargetCommitSha, main.Value!.HeadSha, StringComparison.OrdinalIgnoreCase))
            return GitHubResult<GitHubReleaseInfo>.Failure(GitHubError.TagTargetNotAllowed);

        return await _apiClient.CreateReleaseAsync(
            repository, options.GitHubOwner, options.GitHubRepo, options.LocalRoot, request, cancellationToken);
    }

    private (Configuration.RepositoryOptions? Options, GitHubError? Error) Resolve(string repository)
    {
        if (!RepositoryId.IsValid(repository) || !_allowlist.TryGet(repository, out var options))
        {
            return (null, GitHubError.RepositoryNotFound);
        }

        return (options, null);
    }

    private static GitHubError MapPolicyError(Domain.PolicyErrorCode error) => error switch
    {
        Domain.PolicyErrorCode.TagInvalid => GitHubError.TagInvalid,
        Domain.PolicyErrorCode.TagTargetNotAllowed => GitHubError.TagTargetNotAllowed,
        _ => GitHubError.ApiError,
    };
}
