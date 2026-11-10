using Githubie.Application.Git;

namespace Githubie.Application.Publishing;

/// <summary>Only an explicitly named local branch and immutable commit may be published.</summary>
public sealed record PrPublishInput(string SourceBranch, string DestinationBranch, string ExpectedSourceSha,
    string Title, string? Description, bool Draft, string OperationId);

/// <summary>Publication and PR effects remain visible even when the operation fails.</summary>
public sealed record PrPublicationInfo(string OperationId, string SourceBranch, string DestinationBranch,
    string SourceSha, string PublicationState = "not_attempted", string PullRequestState = "not_attempted",
    int? PullRequestNumber = null, string? PullRequestUrl = null, string? PullRequestHeadSha = null,
    string? ObservedRemoteSha = null, DateTimeOffset? ObservedAt = null, string Stage = "validation")
{
    public bool RequiresReconciliation => PublicationState == "unknown" || PullRequestState == "unknown";
}

public sealed record PrPublicationResult(PrPublicationInfo Data, string? ErrorCode = null, string Outcome = "failed")
{
    public bool IsSuccess => ErrorCode is null;
}

public sealed record PrPublicationRecord(string RequestHash, string Binding, PrPublicationResult Result);

public interface IPrPublicationService
{
    Task<PrPublicationResult> PublishAsync(string repository, PrPublishInput input, CancellationToken cancellationToken);
    Task<PrPublicationResult> GetStatusAsync(string repository, string operationId, CancellationToken cancellationToken);
}

public interface IPrPublicationDiagnostics
{
    void Failure(string stage, string exceptionType);
}

public interface IPrPublicationStore
{
    Task InitializeAsync(CancellationToken cancellationToken);
    Task<IAsyncDisposable?> TryAcquireAsync(string repository, CancellationToken cancellationToken);
    Task<PrPublicationRecord?> GetAsync(string repository, string operationId, CancellationToken cancellationToken);
    Task<bool> HasUnresolvedAsync(string repository, string sourceBranch, CancellationToken cancellationToken);
    Task PutAsync(string repository, string operationId, PrPublicationRecord record, CancellationToken cancellationToken);
}

/// <summary>Creation-only transport. There is no API accepting arbitrary refspecs or expected old SHAs.</summary>
public interface IPrPublicationGit
{
    Task<GitCommandResult> GetPushUrlsAsync(string root, string remote, CancellationToken cancellationToken);
    Task<GitCommandResult> CreateBranchAtCommitAsync(string root, string repository, string remote,
        string branch, string sha, CancellationToken cancellationToken);
}
