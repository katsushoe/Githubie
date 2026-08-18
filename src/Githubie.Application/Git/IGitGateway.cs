namespace Githubie.Application.Git;

/// <summary>
/// Githubie内部Repository IDだけを受け取るアプリケーション層Git Gatewayです。
/// </summary>
public interface IGitGateway
{
    Task<GitGatewayResult<GitRepositoryStatus>> GetStatusAsync(string repository, CancellationToken cancellationToken);

    Task<GitGatewayResult<Unit>> FetchAsync(string repository, CancellationToken cancellationToken);

    Task<GitGatewayResult<Unit>> PullAsync(string repository, string branch, CancellationToken cancellationToken);

    Task<GitGatewayResult<Unit>> PushAsync(string repository, CancellationToken cancellationToken);
}

/// <summary>
/// 値を持たない操作結果を表すための単位型です。
/// </summary>
public readonly record struct Unit
{
    public static readonly Unit Value = default;
}
