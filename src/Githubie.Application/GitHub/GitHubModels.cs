namespace Githubie.Application.GitHub;

public sealed record GitHubRepositoryInfo(string Owner, string Repo, string DefaultBranch);

public sealed record GitHubBranchInfo(string Name, string HeadSha, bool Protected);

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
    string Url);

public sealed record GitHubPullRequestCreate(string Title, string? Description, bool Draft);

public enum GitHubMergeMethod
{
    Merge,
    Squash,
    Rebase,
}

public sealed record GitHubPullRequestMerge(int Number, GitHubMergeMethod? MergeMethod, string? CommitMessage);

public sealed record GitHubPullRequestDiff(string Diff, int FilesChanged, int Additions, int Deletions);
