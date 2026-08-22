namespace Githubie.Application.GitHub;

/// <summary>
/// Githubie内部Repository IDだけを受け取るアプリケーション層GitHub Gatewayです。
/// Allowlist解決とRepository Policy適用は本Gatewayが担い、呼び出し側にowner/repoを意識させません。
/// </summary>
public interface IGitHubRepositoryGateway
{
    Task<GitHubResult<IReadOnlyList<GitHubBranchInfo>>> ListBranchesAsync(string repository, CancellationToken cancellationToken);

    Task<GitHubResult<GitHubBranchInfo>> GetBranchAsync(string repository, string branch, CancellationToken cancellationToken);

    Task<GitHubResult<IReadOnlyList<GitHubPullRequestInfo>>> ListPullRequestsAsync(
        string repository, GitHubPullRequestState? state, string? source, string? destination, CancellationToken cancellationToken);

    Task<GitHubResult<GitHubPullRequestInfo>> GetPullRequestAsync(string repository, int number, CancellationToken cancellationToken);

    Task<GitHubResult<GitHubPullRequestDiff>> GetPullRequestDiffAsync(string repository, int number, CancellationToken cancellationToken);

    Task<GitHubResult<GitHubPullRequestInfo>> CreatePullRequestAsync(string repository, GitHubPullRequestCreate request, CancellationToken cancellationToken);

    Task<GitHubResult<GitHubPullRequestInfo>> MergePullRequestAsync(string repository, GitHubPullRequestMerge request, CancellationToken cancellationToken);

    Task<GitHubResult<GitHubPullRequestInfo>> ClosePullRequestAsync(string repository, int number, CancellationToken cancellationToken);

    Task<GitHubResult<GitHubPullRequestInfo>> ReopenPullRequestAsync(string repository, int number, CancellationToken cancellationToken);

    Task<GitHubResult<IReadOnlyList<GitHubPullRequestComment>>> ListPullRequestCommentsAsync(
        string repository, int number, CancellationToken cancellationToken);

    Task<GitHubResult<GitHubPullRequestComment>> CreatePullRequestCommentAsync(
        string repository, int number, string body, CancellationToken cancellationToken);

    Task<GitHubResult<GitHubPullRequestReview>> ApprovePullRequestAsync(
        string repository, int number, string? body, CancellationToken cancellationToken);

    Task<GitHubResult<GitHubPullRequestReview>> RequestPullRequestChangesAsync(
        string repository, int number, string body, CancellationToken cancellationToken);

    Task<GitHubResult<IReadOnlyList<GitHubTagInfo>>> ListTagsAsync(string repository, CancellationToken cancellationToken);

    Task<GitHubResult<GitHubTagInfo>> GetTagAsync(string repository, string tag, CancellationToken cancellationToken);

    Task<GitHubResult<GitHubTagInfo>> CreateTagAsync(string repository, string tag, string? message, CancellationToken cancellationToken);

    Task<GitHubResult<GitHubReleaseInfo>> CreateReleaseAsync(
        string repository,
        GitHubReleaseCreate request,
        CancellationToken cancellationToken);
}
