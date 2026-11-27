using System.ComponentModel;
using System.Diagnostics;
using System.IO.Pipes;
using System.Runtime.InteropServices;
using Githubie.Application.Interactive;
using Microsoft.Win32.SafeHandles;

namespace Githubie.Infrastructure.Interactive;

/// <summary>現在のユーザー/セッションからだけ画面を起動します。他の対話セッションへ切り替えません。</summary>
public sealed class CurrentUserInteractivePrompt(string executablePath, Func<ProcessStartInfo, Process?>? startProcess = null)
    : IInteractiveApprovalPrompt, IInteractiveTokenPrompt
{
    public async Task<ApprovalPromptOutcome> RequestApprovalAsync(ApprovalPromptRequest request, TimeSpan timeout,
        CancellationToken cancellationToken)
    {
        using var limit = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        limit.CancelAfter(timeout);
        try
        {
            var response = await ExchangeAsync<ApprovalPromptRequest, ApprovalPromptResponse>(request, false, limit.Token);
            return response is null ? ApprovalPromptOutcome.Failure(ApprovalOutcome.ProtocolError)
                : response.Approved ? ApprovalPromptOutcome.Approved() : ApprovalPromptOutcome.Denied();
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        { return ApprovalPromptOutcome.Failure(ApprovalOutcome.TimedOut); }
        catch (InvalidDataException) { return ApprovalPromptOutcome.Failure(ApprovalOutcome.ProtocolError); }
        catch (Exception ex) when (ex is IOException or Win32Exception or UnauthorizedAccessException or InvalidOperationException)
        { return ApprovalPromptOutcome.Failure(ApprovalOutcome.LaunchFailed); }
    }

    public async Task<InteractiveTokenPromptResult> RequestTokenAsync(TokenPromptRequest request, CancellationToken cancellationToken)
    {
        try
        {
            var response = await ExchangeAsync<TokenPromptRequest, TokenPromptResponse>(request, true, cancellationToken);
            if (response is null) return InteractiveTokenPromptResult.Failure(InteractiveTokenPromptOutcome.ProtocolError);
            if (response.Accepted && !string.IsNullOrWhiteSpace(response.Token) && !response.Skipped)
                return InteractiveTokenPromptResult.Accepted(response.Token.Trim().ToCharArray());
            return InteractiveTokenPromptResult.Failure(request.AllowSkip && response.Skipped && !response.Accepted
                ? InteractiveTokenPromptOutcome.Skipped : InteractiveTokenPromptOutcome.Cancelled);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        { return InteractiveTokenPromptResult.Failure(InteractiveTokenPromptOutcome.LaunchFailed); }
        catch (InvalidDataException) { return InteractiveTokenPromptResult.Failure(InteractiveTokenPromptOutcome.ProtocolError); }
        catch (Exception ex) when (ex is IOException or Win32Exception or UnauthorizedAccessException or InvalidOperationException)
        { return InteractiveTokenPromptResult.Failure(InteractiveTokenPromptOutcome.LaunchFailed); }
    }

    private async Task<TResponse?> ExchangeAsync<TRequest, TResponse>(TRequest request, bool token, CancellationToken cancellationToken)
        where TResponse : class
    {
        if (!Environment.UserInteractive || Process.GetCurrentProcess().SessionId == 0)
            throw new IOException("No current interactive user session is available.");
        if (!File.Exists(executablePath)) throw new FileNotFoundException("The Githubie dialog executable is unavailable.");
        var name = "githubie-user-dialog-" + Guid.NewGuid().ToString("N");
        await using var pipe = new NamedPipeServerStream(name, PipeDirection.InOut, 1, PipeTransmissionMode.Byte,
            PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
        var start = new ProcessStartInfo(executablePath) { UseShellExecute = false, CreateNoWindow = true };
        if (token) start.ArgumentList.Add("--token");
        start.ArgumentList.Add(name);
        using var process = (startProcess ?? Process.Start)(start) ?? throw new IOException("The Githubie dialog could not start.");
        try
        {
            using var connectionLimit = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            connectionLimit.CancelAfter(TimeSpan.FromSeconds(15));
            if (!await TokenPromptConnection.WaitForConnectionOrExitAsync(pipe, process.WaitForExitAsync(connectionLimit.Token), connectionLimit.Token))
                throw new IOException("The Githubie dialog closed before connecting.");
            if (!OperatingSystem.IsWindows() || !GetNamedPipeClientProcessId(pipe.SafePipeHandle, out var clientProcessId)
                || clientProcessId != process.Id) throw new InvalidDataException("Unexpected dialog process.");
            await ApprovalPipeProtocol.WriteFrameAsync(pipe, request!, cancellationToken);
            return await ApprovalPipeProtocol.ReadFrameAsync<TResponse>(pipe, cancellationToken);
        }
        finally
        {
            try { if (!process.HasExited) process.Kill(entireProcessTree: true); }
            catch (Exception ex) when (ex is Win32Exception or InvalidOperationException) { }
        }
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetNamedPipeClientProcessId(SafePipeHandle pipe, out uint clientProcessId);
}
