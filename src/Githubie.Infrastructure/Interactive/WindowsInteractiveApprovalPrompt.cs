using System.Diagnostics;
using System.IO.Pipes;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Security.AccessControl;
using System.Security.Principal;
using Githubie.Application.Interactive;
using Microsoft.Win32.SafeHandles;

namespace Githubie.Infrastructure.Interactive;

/// <summary>対話Desktopへ承認Dialogを表示するWindows実装です。</summary>
[SupportedOSPlatform("windows")]
public sealed class WindowsInteractiveApprovalPrompt(string executablePath) : IInteractiveApprovalPrompt
{
    public async Task<ApprovalPromptOutcome> RequestApprovalAsync(
        ApprovalPromptRequest request, TimeSpan timeout, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (!TryGetSession(out var token, out var sid) || token is null || sid is null)
            return ApprovalPromptOutcome.Failure(ApprovalOutcome.NoInteractiveSession);

        using (token)
        {
            var pipeSession = CreatePipe(sid);
            await using (var pipe = pipeSession.Stream)
            {
                if (!await LaunchAsync(token, executablePath, pipeSession.Name, cancellationToken))
                    return ApprovalPromptOutcome.Failure(ApprovalOutcome.LaunchFailed);
                using var timeoutSource = new CancellationTokenSource(timeout);
                using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeoutSource.Token);
                try
                {
                    await pipe.WaitForConnectionAsync(linked.Token);
                    await ApprovalPipeProtocol.WriteFrameAsync(pipe, request, linked.Token);
                    var response = await ApprovalPipeProtocol.ReadFrameAsync<ApprovalPromptResponse>(pipe, linked.Token);
                    return response?.Approved == true ? ApprovalPromptOutcome.Approved() : ApprovalPromptOutcome.Denied();
                }
                catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
                {
                    return ApprovalPromptOutcome.Failure(ApprovalOutcome.TimedOut);
                }
                catch (IOException)
                {
                    return ApprovalPromptOutcome.Failure(ApprovalOutcome.ProtocolError);
                }
            }
        }
    }

    private static (string Name, NamedPipeServerStream Stream) CreatePipe(SecurityIdentifier sid)
    {
        var security = new PipeSecurity();
        security.AddAccessRule(new PipeAccessRule(sid, PipeAccessRights.ReadWrite, AccessControlType.Allow));
        security.AddAccessRule(new PipeAccessRule(
            new SecurityIdentifier(WellKnownSidType.LocalSystemSid, null), PipeAccessRights.FullControl, AccessControlType.Allow));
        var name = $"Githubie-Approval-{Guid.NewGuid():N}";
        var stream = NamedPipeServerStreamAcl.Create(
            name, PipeDirection.InOut, 1, PipeTransmissionMode.Byte, PipeOptions.Asynchronous, 4096, 4096, security);
        return (name, stream);
    }

    private static bool TryGetSession(out SafeAccessTokenHandle? token, out SecurityIdentifier? sid)
    {
        token = null;
        sid = null;
        var sessionId = NativeMethods.WTSGetActiveConsoleSessionId();
        if (sessionId == uint.MaxValue || !NativeMethods.WTSQueryUserToken(sessionId, out var handle) || handle.IsInvalid) return false;
        try
        {
            using var identity = new WindowsIdentity(handle.DangerousGetHandle());
            if (identity.User is null) { handle.Dispose(); return false; }
            token = handle;
            sid = identity.User;
            return true;
        }
        catch (UnauthorizedAccessException) { handle.Dispose(); return false; }
    }

    private static async Task<bool> LaunchAsync(
        SafeAccessTokenHandle token, string path, string pipeName, CancellationToken cancellationToken)
    {
        using var identity = new WindowsIdentity(token.DangerousGetHandle());
        var account = identity.Name;
        if (string.IsNullOrWhiteSpace(account)) return false;
        var taskName = $"Githubie-Approval-{Guid.NewGuid():N}";
        try
        {
            if (!await RunSchtasksAsync(["/Create", "/TN", taskName, "/TR", $"\"{path}\" \"{pipeName}\"", "/SC", "ONCE", "/ST", "23:59", "/RU", account, "/IT", "/RL", "LIMITED", "/F"], cancellationToken)) return false;
            return await RunSchtasksAsync(["/Run", "/TN", taskName], cancellationToken);
        }
        finally
        {
            _ = await RunSchtasksAsync(["/Delete", "/TN", taskName, "/F"], CancellationToken.None);
        }
    }

    private static async Task<bool> RunSchtasksAsync(IReadOnlyList<string> arguments, CancellationToken cancellationToken)
    {
        var startInfo = new ProcessStartInfo("schtasks.exe") { UseShellExecute = false, CreateNoWindow = true };
        foreach (var argument in arguments) startInfo.ArgumentList.Add(argument);
        using var process = new Process { StartInfo = startInfo };
        if (!process.Start()) return false;
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeout.Token);
        try { await process.WaitForExitAsync(linked.Token); return process.ExitCode == 0; }
        catch (OperationCanceledException) { if (!process.HasExited) process.Kill(); return false; }
    }

    private static class NativeMethods
    {
        [DllImport("kernel32.dll")] internal static extern uint WTSGetActiveConsoleSessionId();
        [DllImport("wtsapi32.dll", SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool WTSQueryUserToken(uint sessionId, out SafeAccessTokenHandle token);
    }
}
