namespace Githubie.Application.GitHub;

public sealed record GitHubRepositoryInfo(string Owner, string Repo, string DefaultBranch, string? Description);

public sealed record GitHubWorkflowRunInfo(
    long Id,
    string Workflow,
    string Ref,
    string HeadSha,
    string Event,
    string Status,
    string? Conclusion,
    string Actor,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt,
    string Url);

public sealed record GitHubWorkflowDispatchRequest(string Workflow, string Ref, IReadOnlyDictionary<string, string> Inputs);

public sealed record GitHubWorkflowDispatchInfo(
    string Workflow, string Ref, DateTimeOffset DispatchedAt, GitHubWorkflowRunInfo Run);

public sealed record GitHubBranchInfo(string Name, string HeadSha, bool Protected);

public sealed record GitHubProviderCapabilities(
    bool ProviderCapabilities,
    bool BranchList,
    bool BranchCreate,
    bool BranchDelete,
    bool TagCreate,
    bool TagDelete,
    bool TagPush,
    bool RepositoryDiff,
    bool RepositoryCommit);

public sealed record GitHubTagInfo(string Name, string TargetCommitSha, string? Message, string? Tagger, DateTimeOffset? Date);

public sealed record GitHubTagCreate(string Tag, string TargetCommitSha, string? Message);

public enum GitHubPullRequestState
{
    Open,
    Closed,
    Merged,
}

public sealed record GitHubPullRequestInfo(
    int Number,
    string Title,
    string? Body,
    GitHubPullRequestState State,
    string Source,
    string Destination,
    string Author,
    string? MergeCommitSha,
    bool? Mergeable,
    DateTimeOffset Created,
    DateTimeOffset Updated,
    string Url,
    string MergeabilityStatus = GitHubMergeabilityStatus.UnknownRetryable,
    int? RetryAfterSeconds = null);

/// <summary>Pull Requestのマージ可能性を表す安定した外部状態名です。</summary>
public static class GitHubMergeabilityStatus
{
    public const string CalculatingRetryable = "calculating_retryable";
    public const string Mergeable = "mergeable";
    public const string Conflicting = "conflicting";
    public const string Blocked = "blocked";
    public const string UnknownRetryable = "unknown_retryable";
}

public sealed record GitHubPullRequestCreate(string Title, string? Description, bool Draft);

public enum GitHubMergeMethod
{
    Merge,
    Squash,
    Rebase,
}

public sealed record GitHubPullRequestMerge(int Number, GitHubMergeMethod? MergeMethod, string? CommitMessage);

public sealed record GitHubPullRequestComment(
    long Id,
    string Body,
    string Author,
    DateTimeOffset Created,
    DateTimeOffset Updated,
    string Url);

public enum GitHubPullRequestReviewAction
{
    Approve,
    RequestChanges,
}

public sealed record GitHubPullRequestReview(
    long Id,
    string? Body,
    string Author,
    string State,
    DateTimeOffset Submitted,
    string CommitSha,
    string Url);

public sealed record GitHubPullRequestDiff(string Diff, int FilesChanged, int Additions, int Deletions);

public sealed record GitHubReleaseCreate(
    string Tag,
    string Name,
    string? Body,
    bool Draft,
    bool Prerelease,
    IReadOnlyList<string> Assets);

public sealed record GitHubReleaseUpdate(string? Name, string? Body, bool? Draft, bool? Prerelease);

public sealed record GitHubReleaseAssetUpload(long ReleaseId, IReadOnlyList<string> Assets, bool ReplaceExisting);

public sealed record GitHubReleaseAssetInfo(string Name, long Size, string DownloadUrl, long Id = 0);

public sealed record GitHubReleaseInfo(
    long Id,
    string Tag,
    string Name,
    bool Draft,
    bool Prerelease,
    string Url,
    IReadOnlyList<GitHubReleaseAssetInfo> Assets);
