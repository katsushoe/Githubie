using FluentAssertions;
using Githubie.Server;
using Xunit;

namespace Githubie.Server.Tests;

public sealed class UserHostServerControllerTests
{
    [Fact]
    public async Task StopAndStart_RunSeparateInstancesAndWaitForReadiness()
    {
        using var lifetime = new CancellationTokenSource();
        var starts = 0;
        Action? ready = null;
        var states = new List<UserHostServerState>();
        var server = new UserHostServerController(async (token, onReady) =>
        {
            starts++;
            ready = onReady;
            await Task.Delay(Timeout.Infinite, token);
            return 0;
        }, lifetime.Token);
        server.StateChanged += states.Add;

        server.CanStart.Should().BeTrue();
        server.CanStop.Should().BeFalse();
        var first = server.StartAsync();
        server.State.Should().Be(UserHostServerState.Starting);
        server.CanStart.Should().BeFalse();
        server.StartAsync().Should().BeSameAs(first);
        ready!();
        server.State.Should().Be(UserHostServerState.Running);
        server.CanStop.Should().BeTrue();

        await server.StopAsync();
        (await first).Should().Be(0);
        server.State.Should().Be(UserHostServerState.Stopped);
        server.CanStart.Should().BeTrue();
        server.CanStop.Should().BeFalse();
        var second = server.StartAsync();
        ready!();
        await server.StopAsync();
        (await second).Should().Be(0);
        starts.Should().Be(2);
        states.Should().Equal(
            UserHostServerState.Starting, UserHostServerState.Running,
            UserHostServerState.Stopping, UserHostServerState.Stopped,
            UserHostServerState.Starting, UserHostServerState.Running,
            UserHostServerState.Stopping, UserHostServerState.Stopped);
    }

    [Fact]
    public async Task StopDuringStartup_DisablesBothCommandsUntilShutdownAndIgnoresLateReady()
    {
        Action? ready = null;
        var shutdown = new TaskCompletionSource<int>(TaskCreationOptions.RunContinuationsAsynchronously);
        var server = new UserHostServerController((_, onReady) =>
        {
            ready = onReady;
            return shutdown.Task;
        }, CancellationToken.None);
        var running = server.StartAsync();
        var stopped = server.StopAsync();
        server.State.Should().Be(UserHostServerState.Stopping);
        server.CanStart.Should().BeFalse();
        server.CanStop.Should().BeFalse();
        ready!();
        server.State.Should().Be(UserHostServerState.Stopping);
        server.StartAsync().Should().BeSameAs(running);
        server.StopAsync().Should().BeSameAs(running);
        shutdown.SetResult(0);
        await stopped;
        server.State.Should().Be(UserHostServerState.Stopped);
    }

    [Fact]
    public async Task StartupFailure_IsReportedAndAllowsRetry()
    {
        var expected = new IOException("Test startup failure");
        Exception? reported = null;
        var server = new UserHostServerController((_, _) => throw expected, CancellationToken.None);
        server.Completed += (_, _, failure) => reported = failure;

        (await server.StartAsync()).Should().Be(1);
        reported.Should().BeSameAs(expected);
        server.State.Should().Be(UserHostServerState.Stopped);
        server.CanStart.Should().BeTrue();
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Completion_DistinguishesTrayStopFromExternalStop(bool trayStop)
    {
        var completion = new TaskCompletionSource<int>(TaskCreationOptions.RunContinuationsAsynchronously);
        var requested = false;
        var server = new UserHostServerController((_, ready) =>
        {
            ready();
            return completion.Task;
        }, CancellationToken.None);
        server.Completed += (_, stopped, _) => requested = stopped;
        var running = server.StartAsync();
        if (trayStop) _ = server.StopAsync();
        completion.SetResult(0);
        await running;
        requested.Should().Be(trayStop);
    }

    [Fact]
    public async Task OldReadinessCallback_DoesNotMarkNewInstanceRunning()
    {
        Action? ready = null;
        var server = new UserHostServerController(async (token, onReady) =>
        {
            ready = onReady;
            await Task.Delay(Timeout.Infinite, token);
            return 0;
        }, CancellationToken.None);
        _ = server.StartAsync();
        var previousReady = ready!;
        await server.StopAsync();
        _ = server.StartAsync();
        previousReady();
        server.State.Should().Be(UserHostServerState.Starting);
        ready!();
        server.State.Should().Be(UserHostServerState.Running);
        await server.StopAsync();
    }

    [Fact]
    public async Task ApplicationExit_CancelsServerAndPreventsRestart()
    {
        using var lifetime = new CancellationTokenSource();
        var server = new UserHostServerController(async (token, ready) =>
        {
            ready();
            await Task.Delay(Timeout.Infinite, token);
            return 0;
        }, lifetime.Token);
        var running = server.StartAsync();
        lifetime.Cancel();
        (await running).Should().Be(0);
        server.CanStart.Should().BeFalse();
        server.CanStop.Should().BeFalse();
        server.StartAsync().Should().BeSameAs(running);
    }
}
