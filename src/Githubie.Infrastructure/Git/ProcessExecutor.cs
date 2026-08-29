using System.ComponentModel;
using System.Diagnostics;
using Githubie.Application.Git;

namespace Githubie.Infrastructure.Git;

/// <summary>
/// <see cref="IProcessExecutor"/>の実プロセス実装です。
/// </summary>
public sealed class ProcessExecutor : IProcessExecutor
{
    public async Task<GitCommandResult> ExecuteAsync(
        string workingDirectory,
        string fileName,
        IReadOnlyList<string> arguments,
        IReadOnlyDictionary<string, string>? environmentOverrides,
        TimeSpan timeout,
        CancellationToken cancellationToken)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = fileName,
            WorkingDirectory = workingDirectory,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true,
        };

        foreach (var argument in arguments)
        {
            startInfo.ArgumentList.Add(argument);
        }

        if (environmentOverrides is not null)
        {
            startInfo.EnvironmentVariables.Clear();
            foreach (var (key, value) in environmentOverrides)
            {
                startInfo.EnvironmentVariables[key] = value;
            }
        }

        using var process = new Process { StartInfo = startInfo };
        using var timeoutCts = new CancellationTokenSource(timeout);
        using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeoutCts.Token);

        try
        {
            process.Start();
        }
        catch (Win32Exception ex) when (ex.NativeErrorCode is 2 or 3)
        {
            return GitCommandResult.Failed(GitCommandFailure.NotFound);
        }

        var standardOutputTask = process.StandardOutput.ReadToEndAsync(linkedCts.Token);
        var standardErrorTask = process.StandardError.ReadToEndAsync(linkedCts.Token);

        try
        {
            await process.WaitForExitAsync(linkedCts.Token);
        }
        catch (OperationCanceledException)
        {
            TryKill(process);

            return timeoutCts.IsCancellationRequested
                ? GitCommandResult.Failed(GitCommandFailure.TimedOut)
                : GitCommandResult.Failed(GitCommandFailure.Cancelled);
        }

        var standardOutput = (await standardOutputTask).TrimEnd('\r', '\n');
        var standardError = (await standardErrorTask).TrimEnd('\r', '\n');

        return process.ExitCode == 0
            ? GitCommandResult.Success(standardOutput)
            : GitCommandResult.Failed(GitCommandFailure.Failed, standardOutput, standardError, process.ExitCode);
    }

    private static void TryKill(Process process)
    {
        try
        {
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
            }
        }
        catch (InvalidOperationException)
        {
        }
    }
}
