namespace Githubie.Application.Git;

/// <summary>
/// `repositoryRoot`を起点とした低レベルGitコマンド実行のポートです。
/// 許可されたコマンド以外は公開しません（`status` / `rev-parse` / `remote get-url` / `fetch` / `pull --ff-only` / `push`）。
/// </summary>
public interface IGitCommandClient
{
    Task<GitCommandResult> GetCurrentBranchAsync(string repositoryRoot, CancellationToken cancellationToken);

    Task<GitCommandResult> GetHeadAsync(string repositoryRoot, CancellationToken cancellationToken);

    Task<GitCommandResult> GetRemoteHeadAsync(string repositoryRoot, string remote, string branch, CancellationToken cancellationToken);

    Task<GitCommandResult> GetAheadBehindAsync(string repositoryRoot, string remote, string branch, CancellationToken cancellationToken);

    Task<GitCommandResult> GetStatusAsync(string repositoryRoot, CancellationToken cancellationToken);

    Task<GitCommandResult> GetDiffAsync(string repositoryRoot, CancellationToken cancellationToken);

    Task<GitCommandResult> AddAllAsync(string repositoryRoot, CancellationToken cancellationToken);

    Task<GitCommandResult> CommitAsync(string repositoryRoot, string message, CancellationToken cancellationToken);

    Task<GitCommandResult> GetRemoteUrlAsync(string repositoryRoot, string remote, CancellationToken cancellationToken);

    Task<GitCommandResult> FetchAsync(string repositoryRoot, string repositoryId, string remote, CancellationToken cancellationToken);

    Task<GitCommandResult> PullFastForwardOnlyAsync(string repositoryRoot, string repositoryId, string remote, string branch, CancellationToken cancellationToken);

    Task<GitCommandResult> PushAsync(string repositoryRoot, string repositoryId, string remote, string branch, CancellationToken cancellationToken);

    Task<GitCommandResult> PushTagAsync(string repositoryRoot, string repositoryId, string remote, string tag, CancellationToken cancellationToken);

    Task<GitCommandResult> FetchTagAsync(string repositoryRoot, string repositoryId, string remote, string tag, CancellationToken cancellationToken);

    Task<GitCommandResult> GetLocalRefAsync(string repositoryRoot, string reference, CancellationToken cancellationToken);

    Task<GitCommandResult> GetRemoteRefAsync(string repositoryRoot, string repositoryId, string remote, string reference, CancellationToken cancellationToken);

    Task<GitCommandResult> PushHistoryRewriteAsync(
        string repositoryRoot,
        string repositoryId,
        string remote,
        IReadOnlyList<GitHistoryRewriteRef> refs,
        CancellationToken cancellationToken);
}
