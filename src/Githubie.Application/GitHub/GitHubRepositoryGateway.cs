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
