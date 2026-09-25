namespace Githubie.Application.Git;

/// <summary>承認待機後にProvider要求が現在も実行可能かを再確認します。</summary>
public interface IProviderExecutionGuard
{
    /// <summary>外部処理を開始する直前に現在の要求を再検証します。</summary>
    Task EnsureCurrentAsync(CancellationToken cancellationToken = default);
}
