using System.ComponentModel;
using System.IO.Pipes;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Security.AccessControl;
using System.Security.Principal;
using Githubie.Application.Interactive;
using Microsoft.Extensions.Logging;
using Microsoft.Win32.SafeHandles;

namespace Githubie.Infrastructure.Interactive;

/// <summary>対話Desktopへ承認Dialogを表示するWindows実装です。</summary>
[SupportedOSPlatform("windows")]
public sealed class WindowsInteractiveApprovalPrompt(string executablePath, ILogger<WindowsInteractiveApprovalPrompt> logger)
    : IInteractiveApprovalPrompt, IInteractiveTokenPrompt
{
    public async Task<ApprovalPromptOutcome> RequestApprovalAsync(
        ApprovalPromptRequest request, TimeSpan timeout, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (!TryGetSession(out var token, out var sid, out var sessionId) || token is null || sid is null)
        {
            logger.LogError("Approval prompt session lookup failed: no active interactive user session was found.");
            return ApprovalPromptOutcome.Failure(ApprovalOutcome.NoInteractiveSession);
        }

        using (token)
        {
            var pipeSession = CreatePipe(sid);
            await using (var pipe = pipeSession.Stream)
            {
                if (!TryLaunch(token, executablePath, pipeSession.Name, out var launchError))
                {
                    logger.LogError("Approval prompt process launch failed for session {SessionId}: {Error}", sessionId, launchError);
                    return ApprovalPromptOutcome.Failure(ApprovalOutcome.LaunchFailed);
                }

                logger.LogInformation("Approval prompt process launched in interactive session {SessionId}.", sessionId);
                using var timeoutSource = new CancellationTokenSource(timeout);
                using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeoutSource.Token);
                try
                {
                    await pipe.WaitForConnectionAsync(linked.Token);
                    await ApprovalPipeProtocol.WriteFrameAsync(pipe, request, linked.Token);
                    var response = await ApprovalPipeProtocol.ReadFrameAsync<ApprovalPromptResponse>(pipe, linked.Token);
                    if (response is null)
                    {
                        logger.LogError("Approval prompt returned an empty response in session {SessionId}.", sessionId);
                        return ApprovalPromptOutcome.Failure(ApprovalOutcome.ProtocolError);
                    }

                    return response.Approved ? ApprovalPromptOutcome.Approved() : ApprovalPromptOutcome.Denied();
                }
                catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
                {
                    logger.LogWarning("Approval prompt timed out in session {SessionId}.", sessionId);
                    return ApprovalPromptOutcome.Failure(ApprovalOutcome.TimedOut);
                }
                catch (IOException ex)
                {
                    logger.LogError(ex, "Approval prompt pipe protocol failed in session {SessionId}.", sessionId);
                    return ApprovalPromptOutcome.Failure(ApprovalOutcome.ProtocolError);
                }
            }
        }
    }

    public async Task<InteractiveTokenPromptResult> RequestTokenAsync(
        TokenPromptRequest request,
        TimeSpan timeout, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (!TryGetSession(out var token, out var sid, out var sessionId) || token is null || sid is null)
        {
            logger.LogError("Token prompt session lookup failed: no active interactive user session was found.");
            return InteractiveTokenPromptResult.Failure(InteractiveTokenPromptOutcome.NoInteractiveSession);
        }

        using (token)
        {
            var pipeSession = CreatePipe(sid);
            await using (var pipe = pipeSession.Stream)
            {
                if (!TryLaunch(token, executablePath, pipeSession.Name, true, out var launchError))
                {
                    logger.LogError("Token prompt process launch failed for session {SessionId}: {Error}", sessionId, launchError);
                    return InteractiveTokenPromptResult.Failure(InteractiveTokenPromptOutcome.LaunchFailed);
                }

                logger.LogInformation("Token prompt process launched in interactive session {SessionId}.", sessionId);
                using var timeoutSource = new CancellationTokenSource(timeout);
                using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeoutSource.Token);
                try
                {
                    await pipe.WaitForConnectionAsync(linked.Token);
                    await ApprovalPipeProtocol.WriteFrameAsync(pipe, request, linked.Token);
                    var response = await ApprovalPipeProtocol.ReadFrameAsync<TokenPromptResponse>(pipe, linked.Token);
                    if (response is null)
                    {
                        logger.LogError("Token prompt returned an empty response in session {SessionId}.", sessionId);
                        return InteractiveTokenPromptResult.Failure(InteractiveTokenPromptOutcome.ProtocolError);
                    }

                    return response.Accepted && !string.IsNullOrWhiteSpace(response.Token)
                        ? InteractiveTokenPromptResult.Accepted(response.Token.Trim().ToCharArray())
                        : InteractiveTokenPromptResult.Failure(InteractiveTokenPromptOutcome.Skipped);
                }
                catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
                {
                    logger.LogWarning("Token prompt timed out in session {SessionId}.", sessionId);
                    return InteractiveTokenPromptResult.Failure(InteractiveTokenPromptOutcome.TimedOut);
                }
                catch (IOException ex)
                {
                    logger.LogError(ex, "Token prompt pipe protocol failed in session {SessionId}.", sessionId);
                    return InteractiveTokenPromptResult.Failure(InteractiveTokenPromptOutcome.ProtocolError);
                }
            }
        }
    }

    private static (string Name, NamedPipeServerStream Stream) CreatePipe(SecurityIdentifier sid)
    {
        var security = new PipeSecurity();
        security.AddAccessRule(new PipeAccessRule(sid, PipeAccessRights.ReadWrite, AccessControlType.Allow));
        security.AddAccessRule(new PipeAccessRule(new SecurityIdentifier(WellKnownSidType.LocalSystemSid, null), PipeAccessRights.FullControl, AccessControlType.Allow));
        var name = $"Githubie-Approval-{Guid.NewGuid():N}";
        return (name, NamedPipeServerStreamAcl.Create(name, PipeDirection.InOut, 1, PipeTransmissionMode.Byte,
            PipeOptions.Asynchronous, 4096, 4096, security));
    }

    private static bool TryGetSession(out SafeAccessTokenHandle? token, out SecurityIdentifier? sid, out uint sessionId)
    {
        token = null;
        sid = null;
        sessionId = uint.MaxValue;

        foreach (var candidate in EnumerateActiveSessionIds())
        {
            if (!NativeMethods.WTSQueryUserToken(candidate, out var handle) || handle.IsInvalid)
            {
                handle.Dispose();
                continue;
            }

            try
            {
                using var identity = new WindowsIdentity(handle.DangerousGetHandle());
                if (identity.User is null)
                {
                    handle.Dispose();
                    continue;
                }

                token = handle;
                sid = identity.User;
                sessionId = candidate;
                return true;
            }
            catch (UnauthorizedAccessException)
            {
                handle.Dispose();
            }
        }

        return false;
    }

    private static IReadOnlyList<uint> EnumerateActiveSessionIds()
    {
        if (!NativeMethods.WTSEnumerateSessions(
                IntPtr.Zero, 0, 1, out var sessionInfo, out var count))
        {
            return ConsoleSessionFallback();
        }

        try
        {
            var sessions = new List<uint>();
            var itemSize = Marshal.SizeOf<NativeMethods.WtsSessionInfo>();
            for (var index = 0; index < count; index++)
            {
                var item = Marshal.PtrToStructure<NativeMethods.WtsSessionInfo>(
                    IntPtr.Add(sessionInfo, index * itemSize));
                if (item.State == NativeMethods.WtsConnectState.Active)
                    sessions.Add(item.SessionId);
            }

            return sessions.Count > 0 ? sessions : ConsoleSessionFallback();
        }
        finally
        {
            NativeMethods.WTSFreeMemory(sessionInfo);
        }
    }

    private static IReadOnlyList<uint> ConsoleSessionFallback()
    {
        var sessionId = NativeMethods.WTSGetActiveConsoleSessionId();
        return sessionId == uint.MaxValue ? [] : [sessionId];
    }

    private static bool TryLaunch(
        SafeAccessTokenHandle token, string path, string pipeName, out string error) =>
        TryLaunch(token, path, pipeName, false, out error);

    private static bool TryLaunch(
        SafeAccessTokenHandle token, string path, string pipeName, bool tokenPrompt, out string error)
    {
        error = string.Empty;
        if (!File.Exists(path)) { error = $"executable not found: {path}"; return false; }
        if (!NativeMethods.CreateEnvironmentBlock(out var environment, token, false))
        {
            error = new Win32Exception(Marshal.GetLastWin32Error()).Message;
            return false;
        }

        try
        {
            var startup = new NativeMethods.StartupInfo { Size = Marshal.SizeOf<NativeMethods.StartupInfo>(), Desktop = @"winsta0\default" };
            var commandLine = tokenPrompt
                ? $"\"{path}\" --token \"{pipeName}\""
                : $"\"{path}\" \"{pipeName}\"";
            if (!NativeMethods.CreateProcessAsUser(token, null, commandLine, IntPtr.Zero, IntPtr.Zero, false,
                    NativeMethods.CreateUnicodeEnvironment, environment, Path.GetDirectoryName(path), ref startup, out var process))
            {
                error = new Win32Exception(Marshal.GetLastWin32Error()).Message;
                return false;
            }
            NativeMethods.CloseHandle(process.Thread);
            NativeMethods.CloseHandle(process.Process);
            return true;
        }
        finally { NativeMethods.DestroyEnvironmentBlock(environment); }
    }

    private static class NativeMethods
    {
        internal const uint CreateUnicodeEnvironment = 0x00000400;

        internal enum WtsConnectState
        {
            Active,
            Connected,
            ConnectQuery,
            Shadow,
            Disconnected,
            Idle,
            Listen,
            Reset,
            Down,
            Init,
        }

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        internal struct WtsSessionInfo
        {
            internal uint SessionId;
            internal IntPtr WinStationName;
            internal WtsConnectState State;
        }

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        internal struct StartupInfo
        {
            internal int Size; internal string? Reserved; internal string? Desktop; internal string? Title;
            internal uint X; internal uint Y; internal uint XSize; internal uint YSize; internal uint XCountChars;
            internal uint YCountChars; internal uint FillAttribute; internal uint Flags; internal short ShowWindow;
            internal short Reserved2; internal IntPtr ReservedData; internal IntPtr StandardInput;
            internal IntPtr StandardOutput; internal IntPtr StandardError;
        }

        [StructLayout(LayoutKind.Sequential)]
        internal struct ProcessInformation
        {
            internal IntPtr Process; internal IntPtr Thread; internal uint ProcessId; internal uint ThreadId;
        }

        [DllImport("kernel32.dll")] internal static extern uint WTSGetActiveConsoleSessionId();
        [DllImport("wtsapi32.dll", EntryPoint = "WTSEnumerateSessionsW", CharSet = CharSet.Unicode, SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool WTSEnumerateSessions(
            IntPtr server, int reserved, int version, out IntPtr sessionInfo, out int count);
        [DllImport("wtsapi32.dll")]
        internal static extern void WTSFreeMemory(IntPtr memory);
        [DllImport("wtsapi32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool WTSQueryUserToken(uint sessionId, out SafeAccessTokenHandle token);
        [DllImport("userenv.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool CreateEnvironmentBlock(out IntPtr environment, SafeAccessTokenHandle token, bool inherit);
        [DllImport("userenv.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool DestroyEnvironmentBlock(IntPtr environment);
        [DllImport("advapi32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool CreateProcessAsUser(SafeAccessTokenHandle token, string? applicationName, string commandLine,
            IntPtr processAttributes, IntPtr threadAttributes, bool inheritHandles, uint creationFlags, IntPtr environment,
            string? currentDirectory, ref StartupInfo startupInfo, out ProcessInformation processInformation);
        [DllImport("kernel32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool CloseHandle(IntPtr handle);
    }
}
