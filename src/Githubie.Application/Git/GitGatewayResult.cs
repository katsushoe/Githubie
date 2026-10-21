namespace Githubie.Application.Git;

/// <summary>
/// Repository状態のスナップショットを表します。
/// </summary>
public sealed record GitRepositoryStatus(
    string Repository,
    string LocalBranch,
    string LocalHead,
    string RemoteDevelopHead,
    string RemoteMainHead,
    int Ahead,
    int Behind,
    bool WorkingTreeClean,
    IReadOnlyList<GitWorkingTreeChange> WorkingTreeChanges);

/// <summary>作業Treeで検出した変更の状態コードと相対Pathです。内容は含みません。</summary>
public sealed record GitWorkingTreeChange(string Status, string Path);

/// <summary>登録Repositoryのworking tree差分です。</summary>
public sealed record GitRepositoryDiff(string Diff);

/// <summary>作成したLocal Commitです。</summary>
public sealed record GitRepositoryCommit(string CommitSha);

/// <summary>Repository stashの一覧要素です。</summary>
public sealed record GitStashEntry(string Id, string Subject);

/// <summary>Branch切替の前後に確認した名前です。</summary>
public sealed record GitBranchSwitchInfo(string PreviousBranch, string CurrentBranch);

/// <summary>作成したローカルBranchと作成後に確認したHEAD状態です。</summary>
public sealed record GitLocalBranchCreateInfo(string Name, string Source, string CurrentBranch, string HeadSha);

public sealed record GitHistoryRewriteRef(string Ref, string NewLocalSha, string ExpectedRemoteSha);

public sealed record GitHistoryRewriteRefResult(string Ref, string OldSha, string NewSha, bool Success, string? RejectionReason);

public sealed record GitHistoryRewriteResult(
    bool DryRun,
    string Approval,
    IReadOnlyList<GitHistoryRewriteRefResult> Refs);

/// <summary>
/// Git Gateway操作の結果を表します。
/// </summary>
public sealed record GitGatewayResult<T>(bool IsSuccess, T? Value, GitGatewayError? Error)
{
    public string? CorrelationId { get; init; }

    public string? Diagnostic { get; init; }

    public int? ExitCode { get; init; }

    public static GitGatewayResult<T> Success(T value) => new(true, value, null);

    public static GitGatewayResult<T> Failure(
        GitGatewayError error,
        string? diagnostic = null,
        int? exitCode = null) =>
        new(false, default, error)
        {
            Diagnostic = diagnostic,
            ExitCode = exitCode,
        };
}

/// <summary>
/// Git Gatewayが返すエラーコードです。
/// </summary>
public enum GitGatewayError
{
    RepositoryNotFound,
    RepositoryNotAllowed,

    LocalRootNotFound,
    GitMetadataNotFound,
    ReparsePointDetected,

    RemoteMismatch,
    RemoteHttpsRequired,
    RemoteNotFound,
    RemoteAmbiguous,

    GitNotFound,
    GitFailed,
    GitTimedOut,
    GitCancelled,
    NetworkError,
    RemoteUnavailable,
    AuthenticationFailed,

    WorkingTreeDirty,
    UnresolvedConflicts,
    NothingToCommit,
    InvalidSelectedCommitPatch,
    StagedChangesPresent,
    ExpectedHeadMismatch,
    InvalidStashPath,
    InvalidStashReference,
    InvalidStashMessage,
    NothingToStash,
    AuthorIdentityMissing,
    BranchNotAllowed,
    BranchNotFound,
    BranchSourceNotFound,
    LocalBranchAlreadyExists,
    ProtectedBranch,

    NothingToPush,
    NonFastForward,
    InvalidRef,
    DuplicateRef,
    LeaseConflict,
    AtomicNotSupported,
    BranchProtectionDenied,
    TokenPermissionDenied,
    WorkflowPermissionDenied,
    ApprovalDenied,
    ApprovalTimedOut,
    ApprovalUnavailable,
    PermissionDenied,
}
