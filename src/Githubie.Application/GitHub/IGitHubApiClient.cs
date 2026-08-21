namespace Githubie.Application.GitHub;

/// <summary>
/// GitHub REST APIへの直接アクセスを行うInfrastructure境界のポートです。
/// `owner`/`repo`はGithubie内部で解決済みの値のみが渡されます。`repositoryId`は認証Token解決の鍵として使います。
/// </summary>
public interface IGitHubApiClient
{
    Task<GitHubResult<GitHubRepositoryInfo>> GetRepositoryAsync(string repositoryId, string owner, string repo, CancellationToken cancellationToken);

    Task<GitHubResult<IReadOnlyList<GitHubBranchInfo>>> ListBranchesAsync(string repositoryId, string owner, string repo, CancellationToken cancellationToken);

    Task<GitHubResult<GitHubBranchInfo>> GetBranchAsync(string repositoryId, string owner, string repo, string branch, CancellationToken cancellationToken);

    Task<GitHubResult<IReadOnlyList<GitHubPullRequestInfo>>> ListPullRequestsAsync(
        string repositoryId, string owner, string repo, GitHubPullRequestState? state, string? source, string? destination, CancellationToken cancellationToken);

    Task<GitHubResult<GitHubPullRequestInfo>> GetPullRequestAsync(string repositoryId, string owner, string repo, int number, CancellationToken cancellationToken);

    Task<GitHubResult<GitHubPullRequestDiff>> GetPullRequestDiffAsync(string repositoryId, string owner, string repo, int number, CancellationToken cancellationToken);

    Task<GitHubResult<GitHubPullRequestInfo>> CreatePullRequestAsync(
        string repositoryId, string owner, string repo, string source, string destination, GitHubPullRequestCreate request, CancellationToken cancellationToken);

    Task<GitHubResult<GitHubPullRequestInfo>> MergePullRequestAsync(
        string repositoryId, string owner, string repo, GitHubPullRequestMerge request, CancellationToken cancellationToken);

    Task<GitHubResult<GitHubPullRequestInfo>> UpdatePullRequestStateAsync(
        string repositoryId, string owner, string repo, int number, GitHubPullRequestState state, CancellationToken cancellationToken);

    Task<GitHubResult<IReadOnlyList<GitHubPullRequestComment>>> ListPullRequestCommentsAsync(
        string repositoryId, string owner, string repo, int number, CancellationToken cancellationToken);

    Task<GitHubResult<GitHubPullRequestComment>> CreatePullRequestCommentAsync(
        string repositoryId, string owner, string repo, int number, string body, CancellationToken cancellationToken);

    Task<GitHubResult<IReadOnlyList<GitHubTagInfo>>> ListTagsAsync(string repositoryId, string owner, string repo, CancellationToken cancellationToken);

    Task<GitHubResult<GitHubTagInfo>> GetTagAsync(string repositoryId, string owner, string repo, string tag, CancellationToken cancellationToken);

    Task<GitHubResult<GitHubTagInfo>> CreateTagAsync(string repositoryId, string owner, string repo, GitHubTagCreate request, CancellationToken cancellationToken);

    Task<GitHubResult<GitHubReleaseInfo>> CreateReleaseAsync(
        string repositoryId,
        string owner,
        string repo,
        string localRoot,
        GitHubReleaseCreate request,
        CancellationToken cancellationToken);
}
