namespace Githubie.Application.GitHub;

/// <summary>
/// Githubie内部Repository IDだけを受け取るアプリケーション層GitHub Gatewayです。
/// Allowlist解決とRepository Policy適用は本Gatewayが担い、呼び出し側にowner/repoを意識させません。
/// </summary>
public interface IGitHubRepositoryGateway
{
    Task<GitHubResult<GitHubRepositoryInfo>> GetRepositoryAsync(string repository, CancellationToken cancellationToken);

    Task<GitHubResult<GitHubRepositoryInfo>> UpdateRepositoryDescriptionAsync(
        string repository, string description, CancellationToken cancellationToken);

    Task<GitHubResult<GitHubWorkflowDispatchInfo>> DispatchWorkflowAsync(
        string repository, GitHubWorkflowDispatchRequest request, CancellationToken cancellationToken);

    Task<GitHubResult<GitHubWorkflowRunInfo>> GetWorkflowRunAsync(
        string repository, long runId, CancellationToken cancellationToken);

    Task<GitHubResult<IReadOnlyList<GitHubWorkflowRunInfo>>> ListWorkflowRunsAsync(
        string repository, string? workflow, string? branch, string? eventName, string? status,
        int limit, CancellationToken cancellationToken);

    Task<GitHubResult<IReadOnlyList<GitHubBranchInfo>>> ListBranchesAsync(string repository, CancellationToken cancellationToken);

    Task<GitHubResult<GitHubBranchInfo>> GetBranchAsync(string repository, string branch, CancellationToken cancellationToken);

    Task<GitHubResult<GitHubBranchInfo>> CreateBranchAsync(string repository, string branch, string source, CancellationToken cancellationToken);

    Task<GitHubResult<bool>> DeleteBranchAsync(string repository, string branch, CancellationToken cancellationToken);

    Task<GitHubResult<IReadOnlyList<GitHubPullRequestInfo>>> ListPullRequestsAsync(
        string repository, GitHubPullRequestState? state, string? source, string? destination, CancellationToken cancellationToken);

    Task<GitHubResult<GitHubPullRequestInfo>> GetPullRequestAsync(string repository, int number, CancellationToken cancellationToken);

    Task<GitHubResult<IReadOnlyList<GitHubIssueInfo>>> ListIssuesAsync(
        string repository, GitHubIssueState? state, CancellationToken cancellationToken);

    Task<GitHubResult<GitHubIssueInfo>> GetIssueAsync(string repository, int number, CancellationToken cancellationToken);

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

    Task<GitHubResult<GitHubTagInfo>> CreateTagAsync(string repository, string tag, string source, string? message, CancellationToken cancellationToken);

    Task<GitHubResult<bool>> DeleteTagAsync(string repository, string tag, CancellationToken cancellationToken);

    Task<GitHubResult<IReadOnlyList<GitHubReleaseInfo>>> ListReleasesAsync(string repository, CancellationToken cancellationToken);

    Task<GitHubResult<GitHubReleaseInfo>> GetReleaseAsync(string repository, string tag, CancellationToken cancellationToken);

    Task<GitHubResult<GitHubReleaseInfo>> UpdateReleaseAsync(string repository, long releaseId, GitHubReleaseUpdate request, CancellationToken cancellationToken);

    Task<GitHubResult<bool>> DeleteReleaseAsync(string repository, long releaseId, CancellationToken cancellationToken);

    Task<GitHubResult<bool>> DeleteDraftReleaseAsync(string repository, long releaseId, CancellationToken cancellationToken);

    Task<GitHubResult<GitHubReleaseInfo>> UploadReleaseAssetsAsync(string repository, GitHubReleaseAssetUpload request, CancellationToken cancellationToken);

    Task<GitHubResult<GitHubReleaseInfo>> CreateReleaseAsync(
        string repository,
        GitHubReleaseCreate request,
        CancellationToken cancellationToken);
}
