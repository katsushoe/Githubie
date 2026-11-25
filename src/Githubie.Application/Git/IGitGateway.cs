namespace Githubie.Application.Git;

/// <summary>
/// Githubie内部Repository IDだけを受け取るアプリケーション層Git Gatewayです。
/// </summary>
public interface IGitGateway
{
    Task<GitGatewayResult<GitRepositoryStatus>> GetStatusAsync(string repository, CancellationToken cancellationToken);

    Task<GitGatewayResult<GitBranchSwitchInfo>> SwitchBranchAsync(string repository, string branch, CancellationToken cancellationToken);

    /// <summary>CleanなworktreeでローカルBranch/Tag/SHAからローカルBranch refだけを作成します。</summary>
    Task<GitGatewayResult<GitLocalBranchCreateInfo>> CreateLocalBranchAsync(string repository, string branch, string source, CancellationToken cancellationToken);

    Task<GitGatewayResult<GitRepositoryDiff>> GetDiffAsync(string repository, CancellationToken cancellationToken);

    Task<GitGatewayResult<GitRepositoryCommit>> CommitAsync(string repository, string message, CancellationToken cancellationToken);
    Task<GitGatewayResult<GitRepositoryCommit>> CommitSelectedAsync(string repository, string message, string expectedHead, string patch, CancellationToken cancellationToken);

    /// <summary>現在branchへ明示した取り込み元をmergeします。衝突時の既定は中止で、作業ツリーとHEADを変更しません。pushは行いません。</summary>
    Task<GitGatewayResult<GitRepositoryMergeInfo>> MergeAsync(
        string repository, string branch, string expectedHead, string sourceKind, string source, string expectedSourceSha,
        string fastForward, string? message, IReadOnlyList<GitMergeResolution>? resolutions, CancellationToken cancellationToken) =>
        throw new NotSupportedException();

    Task<GitGatewayResult<GitStashEntry>> PushStashAsync(
        string repository, string message, IReadOnlyList<string>? paths, CancellationToken cancellationToken);

    Task<GitGatewayResult<IReadOnlyList<GitStashEntry>>> ListStashesAsync(
        string repository, CancellationToken cancellationToken);

    Task<GitGatewayResult<GitStashEntry>> ApplyStashAsync(
        string repository, string stashId, CancellationToken cancellationToken);

    Task<GitGatewayResult<GitStashEntry>> DropStashAsync(
        string repository, string stashId, CancellationToken cancellationToken);

    Task<GitGatewayResult<Unit>> FetchAsync(string repository, CancellationToken cancellationToken);

    Task<GitGatewayResult<Unit>> PullAsync(string repository, string branch, CancellationToken cancellationToken);

    Task<GitGatewayResult<Unit>> PushAsync(string repository, CancellationToken cancellationToken);

    Task<GitGatewayResult<Unit>> PushTagAsync(string repository, string tag, CancellationToken cancellationToken);

    Task<GitGatewayResult<Unit>> PersistTagAsync(string repository, string tag, CancellationToken cancellationToken);

    Task<GitGatewayResult<GitHistoryRewriteResult>> RewriteHistoryAsync(
        string repository,
        IReadOnlyList<GitHistoryRewriteRef> refs,
        bool dryRun,
        CancellationToken cancellationToken);
}

/// <summary>
/// 値を持たない操作結果を表すための単位型です。
/// </summary>
public readonly record struct Unit
{
    public static readonly Unit Value = default;
}
