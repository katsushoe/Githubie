using System.Diagnostics;
using System.IO.Pipes;
using FluentAssertions;
using Githubie.Application.Interactive;
using Githubie.Infrastructure.Interactive;
using Xunit;

namespace Githubie.Infrastructure.Tests;

[System.Runtime.Versioning.SupportedOSPlatform("windows")]
public sealed class CurrentUserInteractivePromptTests
{
    private static string PowerShell => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "WindowsPowerShell", "v1.0", "powershell.exe");

    [Theory]
    [InlineData("token", InteractiveTokenPromptOutcome.Accepted)]
    [InlineData("cancel", InteractiveTokenPromptOutcome.Cancelled)]
    [InlineData("skip", InteractiveTokenPromptOutcome.Skipped)]
    [InlineData("invalid", InteractiveTokenPromptOutcome.ProtocolError)]
    public async Task OwnedChild_ResponseDistinguishesTokenCancelSkipAndProtocolFailure(string mode, InteractiveTokenPromptOutcome expected)
    {
        var prompt = new CurrentUserInteractivePrompt(PowerShell, start => StartFixture(start.ArgumentList.Last(), mode));
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(30));
        var result = await prompt.RequestTokenAsync(new TokenPromptRequest("sample", "https://github.com/owner/repo") { AllowSkip = true }, timeout.Token);
        result.Outcome.Should().Be(expected);
        if (result.Token is not null) { new string(result.Token).Should().Be("fixture-token"); Array.Clear(result.Token); }
    }

    [Fact]
    public async Task Approval_FromOwnedChildIsAccepted()
    {
        var prompt = new CurrentUserInteractivePrompt(PowerShell, start => StartFixture(start.ArgumentList.Last(), "approval"));
        var result = await prompt.RequestApprovalAsync(new("Test consent", "Fixture only", []), TimeSpan.FromSeconds(30), TestContext.Current.CancellationToken);
        result.Outcome.Should().Be(ApprovalOutcome.Approved);
    }

    [Fact]
    public async Task AnotherSameUserProcess_CannotImpersonateLaunchedDialog()
    {
        var pipeName = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
        var prompt = new CurrentUserInteractivePrompt(PowerShell, start =>
        {
            var sleeper = new ProcessStartInfo(PowerShell) { UseShellExecute = false, CreateNoWindow = true };
            sleeper.ArgumentList.Add("-NoProfile");
            sleeper.ArgumentList.Add("-NonInteractive");
            sleeper.ArgumentList.Add("-Command");
            sleeper.ArgumentList.Add("Start-Sleep -Seconds 30");
            var process = Process.Start(sleeper);
            pipeName.TrySetResult(start.ArgumentList.Last());
            return process;
        });
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(15));
        var result = prompt.RequestApprovalAsync(new("Test consent", "Fixture only", []), TimeSpan.FromSeconds(15), timeout.Token);
        await using var impostor = new NamedPipeClientStream(".", await pipeName.Task.WaitAsync(timeout.Token), PipeDirection.InOut,
            PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
        await impostor.ConnectAsync(timeout.Token);
        (await result).Outcome.Should().Be(ApprovalOutcome.ProtocolError);
    }

    private static Process? StartFixture(string pipeName, string mode)
    {
        var start = new ProcessStartInfo(PowerShell) { UseShellExecute = false, CreateNoWindow = true };
        foreach (var argument in new[] { "-NoProfile", "-NonInteractive", "-File", Path.Combine(AppContext.BaseDirectory, "Fixtures", "PipePromptFixture.ps1"),
            "-PipeName", pipeName, "-Mode", mode }) start.ArgumentList.Add(argument);
        return Process.Start(start);
    }
}
