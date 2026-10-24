namespace Githubie.Application.Repositories;

/// <summary>ホスト内の登録/設定/資格変更を共有し、入力画面の対象が途中で変更されることを防ぎます。</summary>
public sealed class RepositoryMutationLock
{
    internal SemaphoreSlim Gate { get; } = new(1, 1);

    public async Task<IDisposable> AcquireAsync(CancellationToken cancellationToken)
    {
        await Gate.WaitAsync(cancellationToken);
        return new Lease(Gate);
    }

    private sealed class Lease(SemaphoreSlim gate) : IDisposable
    {
        private SemaphoreSlim? _gate = gate;
        public void Dispose() => Interlocked.Exchange(ref _gate, null)?.Release();
    }
}
