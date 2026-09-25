using System.IO.Pipes;
using FluentAssertions;
using Githubie.Application.Interactive;
using Xunit;

namespace Githubie.Application.Tests;

public sealed class TokenPromptConnectionTests
{
    private static NamedPipeServerStream CreatePipe(out string name)
    {
        name = $"githubie-test-{Guid.NewGuid():N}";
        return new NamedPipeServerStream(name, PipeDirection.InOut, 1, PipeTransmissionMode.Byte,
            PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
    }

    [Fact]
    public async Task WaitForConnectionOrExitAsync_ClientConnectsLater_ReturnsTrueWithoutTimeout()
    {
        await using var pipe = CreatePipe(out var name);
        var neverExits = new TaskCompletionSource().Task;
        var wait = TokenPromptConnection.WaitForConnectionOrExitAsync(pipe, neverExits, TestContext.Current.CancellationToken);

        await Task.Delay(TimeSpan.FromSeconds(1), TestContext.Current.CancellationToken);
        wait.IsCompleted.Should().BeFalse();
        await using var client = new NamedPipeClientStream(".", name, PipeDirection.InOut, PipeOptions.Asynchronous);
        await client.ConnectAsync(TestContext.Current.CancellationToken);

        (await wait).Should().BeTrue();
    }

    [Fact]
    public async Task WaitForConnectionOrExitAsync_DialogExitsBeforeConnecting_ReturnsFalse()
    {
        await using var pipe = CreatePipe(out _);
        var exited = new TaskCompletionSource();
        var wait = TokenPromptConnection.WaitForConnectionOrExitAsync(pipe, exited.Task, TestContext.Current.CancellationToken);

        exited.SetResult();

        (await wait).Should().BeFalse();
    }

    [Fact]
    public async Task WaitForConnectionOrExitAsync_CallerCancels_Throws()
    {
        await using var pipe = CreatePipe(out _);
        using var cancel = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        var wait = TokenPromptConnection.WaitForConnectionOrExitAsync(pipe, new TaskCompletionSource().Task, cancel.Token);

        await cancel.CancelAsync();

        var action = () => wait;
        await action.Should().ThrowAsync<OperationCanceledException>();
    }
}
