using System.IO.Pipes;

namespace Githubie.Application.Interactive;

/// <summary>Token画面の接続を時間制限なしで待ち、画面Processの終了で待機を打ち切ります。</summary>
public static class TokenPromptConnection
{
    /// <summary>接続できた場合はtrue、接続前に画面Processが終了した場合はfalseを返します。</summary>
    public static async Task<bool> WaitForConnectionOrExitAsync(
        NamedPipeServerStream pipe,
        Task dialogExited,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(pipe);
        ArgumentNullException.ThrowIfNull(dialogExited);

        using var stop = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        var connection = pipe.WaitForConnectionAsync(stop.Token);
        await Task.WhenAny(connection, dialogExited).ConfigureAwait(false);
        if (connection.IsCompletedSuccessfully) return true;

        cancellationToken.ThrowIfCancellationRequested();
        if (!connection.IsCompleted)
        {
            await stop.CancelAsync().ConfigureAwait(false);
            try
            {
                await connection.ConfigureAwait(false);
                return true;
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                return false;
            }
        }

        await connection.ConfigureAwait(false);
        return true;
    }
}
