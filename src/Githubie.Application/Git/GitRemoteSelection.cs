namespace Githubie.Application.Git;

/// <summary>
/// Tool引数`remote`（Moyaiの`gitRemoteName`）を、Gatewayの公開シグネチャを変えずに
/// 同じ非同期フロー内のGit操作へ引き渡します。
/// </summary>
public static class GitRemoteSelection
{
    private static readonly AsyncLocal<string?> CurrentRemote = new();

    /// <summary>現在の非同期フローで指定されたリモート名です。未指定なら<see langword="null"/>です。</summary>
    public static string? Current => CurrentRemote.Value;

    /// <summary>指定したリモート名を、戻り値を破棄するまで有効にします。空白は未指定とみなします。</summary>
    public static IDisposable Begin(string? remote)
    {
        var previous = CurrentRemote.Value;
        CurrentRemote.Value = string.IsNullOrWhiteSpace(remote) ? null : remote.Trim();
        return new Scope(previous);
    }

    private sealed class Scope(string? previous) : IDisposable
    {
        private bool _disposed;

        public void Dispose()
        {
            if (_disposed) return;
            CurrentRemote.Value = previous;
            _disposed = true;
        }
    }
}
