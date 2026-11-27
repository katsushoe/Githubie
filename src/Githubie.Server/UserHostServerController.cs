namespace Githubie.Server;

internal enum UserHostServerState { Stopped, Starting, Running, Stopping }

/// <summary>トレイを残したままHTTPサーバーを開始・停止します。</summary>
internal sealed class UserHostServerController(
    Func<CancellationToken, Action, Task<int>> run, CancellationToken lifetime)
{
    private CancellationTokenSource? _cancellation;
    private Task<int> _runTask = Task.FromResult(0);
    private bool _stopRequested;

    internal UserHostServerState State { get; private set; }
    internal bool CanStart => State == UserHostServerState.Stopped && !lifetime.IsCancellationRequested;
    internal bool CanStop => State is UserHostServerState.Starting or UserHostServerState.Running;
    internal event Action<UserHostServerState>? StateChanged;
    internal event Action<int, bool, Exception?>? Completed;

    internal Task<int> StartAsync()
    {
        if (!CanStart) return _runTask;
        _stopRequested = false;
        _cancellation = CancellationTokenSource.CreateLinkedTokenSource(lifetime);
        SetState(UserHostServerState.Starting);
        _runTask = RunAsync(_cancellation);
        return _runTask;
    }

    internal Task StopAsync()
    {
        if (!CanStop) return _runTask;
        _stopRequested = true;
        SetState(UserHostServerState.Stopping);
        _cancellation!.Cancel();
        return _runTask;
    }

    private async Task<int> RunAsync(CancellationTokenSource cancellation)
    {
        var result = 1;
        Exception? failure = null;
        try
        {
            result = await run(cancellation.Token, () =>
            {
                if (_cancellation == cancellation && !cancellation.IsCancellationRequested
                    && State == UserHostServerState.Starting)
                    SetState(UserHostServerState.Running);
            });
        }
        catch (OperationCanceledException) when (cancellation.IsCancellationRequested) { result = 0; }
        catch (Exception exception) { failure = exception; }
        finally
        {
            cancellation.Dispose();
            _cancellation = null;
            var stopped = _stopRequested;
            SetState(UserHostServerState.Stopped);
            Completed?.Invoke(result, stopped, failure);
        }
        return result;
    }

    private void SetState(UserHostServerState state)
    {
        State = state;
        StateChanged?.Invoke(state);
    }
}
