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
    public static GitGatewayResult<T> Success(T value) => new(true, value, null);

    public static GitGatewayResult<T> Failure(GitGatewayError error) => new(false, default, error);
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

    GitNotFound,
    GitFailed,
    GitTimedOut,
    GitCancelled,
    AuthenticationFailed,

    WorkingTreeDirty,
    BranchNotAllowed,
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
