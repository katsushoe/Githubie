using System.Diagnostics;
using System.IO.Pipes;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Security.Cryptography;
using System.Text;
using Githubie.Application.Interactive;
using Microsoft.Win32.SafeHandles;

namespace Githubie.Infrastructure.Configuration;

public sealed record UserHostControlRequest(
    [property: System.Text.Json.Serialization.JsonPropertyName("credential")] string Credential,
    [property: System.Text.Json.Serialization.JsonPropertyName("command")] string Command,
    [property: System.Text.Json.Serialization.JsonPropertyName("repository")] string? Repository)
{
    public override string ToString() => $"Githubie control: {Command}";
}
public sealed record UserHostControlResponse(
    [property: System.Text.Json.Serialization.JsonPropertyName("success")] bool Success,
    [property: System.Text.Json.Serialization.JsonPropertyName("code")] string Code);

/// <summary>SID制限に加え、OSが報告する接続元セッションと起動資格を照合します。</summary>
[SupportedOSPlatform("windows")]
public static class UserHostControlPipe
{
    private static string Name(UserHostConnection connection) => "Githubie-UserHost-" + Convert.ToHexString(SHA256.HashData(
        Encoding.UTF8.GetBytes($"{connection.OwnerSid}/{connection.ProfileId}/{connection.SessionId}")))[..32];

    public static async Task RunAsync(UserHostConnection connection, Func<UserHostControlRequest, CancellationToken, Task<UserHostControlResponse>> handler,
        Action stop, CancellationToken cancellationToken)
    {
        using var lifetime = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        using var capacity = new SemaphoreSlim(16, 16);
        var clients = new List<Task>();
        try
        {
            while (!lifetime.IsCancellationRequested)
            {
                await capacity.WaitAsync(lifetime.Token);
                NamedPipeServerStream? pipe = null;
                try
                {
                    pipe = new NamedPipeServerStream(Name(connection), PipeDirection.InOut, NamedPipeServerStream.MaxAllowedServerInstances,
                        PipeTransmissionMode.Byte, PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
                    await pipe.WaitForConnectionAsync(lifetime.Token);
                    clients.RemoveAll(task => task.IsCompletedSuccessfully);
                    clients.Add(HandleAsync(pipe));
                    pipe = null; // HandleAsync owns the connected pipe and capacity lease.
                }
                finally
                {
                    if (pipe is not null) { await pipe.DisposeAsync(); capacity.Release(); }
                }
            }
        }
        finally
        {
            lifetime.Cancel();
            await Task.WhenAll(clients);
        }

        async Task HandleAsync(NamedPipeServerStream pipe)
        {
            try
            {
                if (!GetNamedPipeClientSessionId(pipe.SafePipeHandle, out var session) || session != connection.SessionId) return;
                using var timeout = CancellationTokenSource.CreateLinkedTokenSource(lifetime.Token);
                timeout.CancelAfter(TimeSpan.FromSeconds(10));
                var request = await ApprovalPipeProtocol.ReadFrameAsync<UserHostControlRequest>(pipe, timeout.Token);
                if (request is null || request.Credential is null || !CryptographicOperations.FixedTimeEquals(
                    Encoding.UTF8.GetBytes(request.Credential), Encoding.UTF8.GetBytes(connection.Credential))) return;
                var response = await handler(request, lifetime.Token);
                await ApprovalPipeProtocol.WriteFrameAsync(pipe, response, lifetime.Token);
                if (request.Command == "stop" && response.Success) stop();
            }
            catch (OperationCanceledException) { }
            catch (IOException) { }
            finally { await pipe.DisposeAsync(); capacity.Release(); }
        }
    }

    public static async Task<UserHostControlResponse> SendAsync(UserHostConnection connection, string command, string? repository,
        CancellationToken cancellationToken)
    {
        if (connection.SessionId != Process.GetCurrentProcess().SessionId) throw new UnauthorizedAccessException("The host belongs to another session.");
        await using var pipe = new NamedPipeClientStream(".", Name(connection), PipeDirection.InOut,
            PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(5));
        await pipe.ConnectAsync(timeout.Token);
        await ApprovalPipeProtocol.WriteFrameAsync(pipe, new UserHostControlRequest(connection.Credential, command, repository), cancellationToken);
        return await ApprovalPipeProtocol.ReadFrameAsync<UserHostControlResponse>(pipe, cancellationToken)
            ?? throw new IOException("The host returned an invalid control response.");
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetNamedPipeClientSessionId(SafePipeHandle pipe, out uint sessionId);
}
