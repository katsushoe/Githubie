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
    bool WorkingTreeClean);

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

    WorkingTreeDirty,
    BranchNotAllowed,
    ProtectedBranch,

    NothingToPush,
    NonFastForward,
}
