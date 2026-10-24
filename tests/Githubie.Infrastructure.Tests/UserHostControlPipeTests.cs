using System.Diagnostics;
using System.Security.Principal;
using FluentAssertions;
using Githubie.Infrastructure.Configuration;
using Xunit;

namespace Githubie.Infrastructure.Tests;

[System.Runtime.Versioning.SupportedOSPlatform("windows")]
public sealed class UserHostControlPipeTests
{
    [Fact]
    public async Task WaitingDialog_DoesNotBlockStatusOrStop()
    {
        using var process = Process.GetCurrentProcess();
        using var user = WindowsIdentity.GetCurrent();
        var connection = new UserHostConnection(Guid.NewGuid(), user.User!.Value, process.SessionId, process.Id,
            process.StartTime.ToUniversalTime().Ticks, "http://127.0.0.1:45460/mcp", Guid.NewGuid().ToString("N"));
        using var lifetime = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        lifetime.CancelAfter(TimeSpan.FromSeconds(15));
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var server = UserHostControlPipe.RunAsync(connection, async (request, token) =>
        {
            if (request.Command == "wait") { entered.TrySetResult(); await Task.Delay(Timeout.InfiniteTimeSpan, token); }
            return new UserHostControlResponse(true, "ready");
        }, lifetime.Cancel, lifetime.Token);
        var waiting = UserHostControlPipe.SendAsync(connection, "wait", null, TestContext.Current.CancellationToken);
        try
        {
            await entered.Task.WaitAsync(lifetime.Token);
            var status = await UserHostControlPipe.SendAsync(connection, "status", null, TestContext.Current.CancellationToken);
            status.Success.Should().BeTrue();
            waiting.IsCompleted.Should().BeFalse();
            var stop = await UserHostControlPipe.SendAsync(connection, "stop", null, TestContext.Current.CancellationToken);
            stop.Success.Should().BeTrue();
        }
        finally
        {
            lifetime.Cancel();
            try { await server; } catch (OperationCanceledException) { }
            try { await waiting; } catch (IOException) { }
        }
    }
}
