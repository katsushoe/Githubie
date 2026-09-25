using System.Diagnostics;
using System.IO.Pipes;
using FluentAssertions;
using Githubie.Application.Interactive;
using Githubie.Cli;
using Xunit;

namespace Githubie.Cli.Tests;

public sealed class TokenPromptClientTests
{
    private static Process? StartCompletedProcess() => StartCommand("/c exit 0");

    // Token画面の代わりに、Clientが終了させるまで生存するProcessを使う。
    private static Process? StartRunningProcess() => StartCommand("/c ping -n 600 127.0.0.1 >nul");

    private static Process? StartCommand(string arguments) => Process.Start(new ProcessStartInfo
    {
        FileName = Environment.GetEnvironmentVariable("ComSpec")!,
        Arguments = arguments,
        UseShellExecute = false,
        CreateNoWindow = true,
    });

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task RequestAsync_PipeResponse_ReturnsTokenOrCancelWithoutTokenArguments(bool accepted)
    {
        Task peer = Task.CompletedTask;
        var client = new TokenPromptClient(Environment.ProcessPath!, start =>
        {
            start.ArgumentList.Should().HaveCount(2);
            start.ArgumentList[0].Should().Be("--token");
            start.ArgumentList.Should().NotContain(value => value.Contains("test-secret"));
            peer = ExchangeAsync(start.ArgumentList[1], accepted);
            return StartRunningProcess();
        });

        var token = await client.RequestAsync(new("sample", "https://github.com/owner/repo"),
            TestContext.Current.CancellationToken);
        await peer;

        if (accepted) new string(token!).Should().Be("test-secret");
        else token.Should().BeNull();
        if (token is not null) Array.Clear(token);
    }

    private static async Task ExchangeAsync(string pipeName, bool accepted, TimeSpan? responseDelay = null)
    {
        await using var pipe = new NamedPipeClientStream(".", pipeName, PipeDirection.InOut, PipeOptions.Asynchronous);
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        await pipe.ConnectAsync(timeout.Token);
        if (responseDelay is { } delay) await Task.Delay(delay, timeout.Token);
        var request = await ApprovalPipeProtocol.ReadFrameAsync<TokenPromptRequest>(pipe, timeout.Token);
        request!.ProjectName.Should().Be("sample");
        request.RepositoryUrl.Should().Be("https://github.com/owner/repo");
        await ApprovalPipeProtocol.WriteFrameAsync(pipe,
            new TokenPromptResponse(accepted, accepted ? "test-secret" : ""), timeout.Token);
    }

    [Fact]
    public async Task RequestAsync_DialogExitsBeforeConnecting_IsDialogFailure()
    {
        var client = new TokenPromptClient(Environment.ProcessPath!, _ => StartCompletedProcess());
        var action = () => client.RequestAsync(new("sample", "https://github.com/owner/repo"), TestContext.Current.CancellationToken);
        await action.Should().ThrowAsync<IOException>();
    }

    [Fact]
    public async Task RequestAsync_SlowResponse_WaitsWithoutTimeout()
    {
        Task peer = Task.CompletedTask;
        var client = new TokenPromptClient(Environment.ProcessPath!, start =>
        {
            peer = ExchangeAsync(start.ArgumentList[1], accepted: true, TimeSpan.FromSeconds(2));
            return StartRunningProcess();
        });

        var token = await client.RequestAsync(new("sample", "https://github.com/owner/repo"),
            TestContext.Current.CancellationToken);
        await peer;

        new string(token!).Should().Be("test-secret");
        Array.Clear(token!);
    }

    [Fact]
    public async Task RequestAsync_CallerCancels_StopsWaiting()
    {
        using var cancel = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        var client = new TokenPromptClient(Environment.ProcessPath!, _ => StartRunningProcess());
        var request = client.RequestAsync(new("sample", "https://github.com/owner/repo"), cancel.Token);
        cancel.CancelAfter(TimeSpan.FromMilliseconds(200));

        var action = () => request;
        await action.Should().ThrowAsync<OperationCanceledException>();
    }

    [Fact]
    public async Task RequestAsync_StartReturnsNull_IsFailure()
    {
        var client = new TokenPromptClient(Environment.ProcessPath!, _ => null);
        var action = () => client.RequestAsync(new("sample", "https://github.com/owner/repo"), TestContext.Current.CancellationToken);
        await action.Should().ThrowAsync<IOException>();
    }
}
