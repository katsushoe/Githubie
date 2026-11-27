using System.ComponentModel;
using System.Diagnostics;
using System.Text.Json;

namespace Githubie.Cli;

// Client-side diagnostics run as the invoking user, not as the Windows service.
internal static class CodexMcpAuthStatusCommand
{
    internal static async Task<int> RunAsync(TextWriter output, CancellationToken cancellationToken)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(10));
        using var process = new Process
        {
            StartInfo = new ProcessStartInfo
            {
                FileName = OperatingSystem.IsWindows() ? "cmd.exe" : "codex",
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true
            }
        };
        if (OperatingSystem.IsWindows())
        {
            // Fixed command only: no shell interpolation of user input.
            foreach (var argument in new[] { "/d", "/s", "/c", "codex mcp list --json" })
                process.StartInfo.ArgumentList.Add(argument);
        }
        else
        {
            foreach (var argument in new[] { "mcp", "list", "--json" })
                process.StartInfo.ArgumentList.Add(argument);
        }

        try
        {
            if (!process.Start())
                return WriteFailure(output, "codex_start_failed");

            // Drain both pipes concurrently; never forward raw configuration or stderr.
            var stdout = process.StandardOutput.ReadToEndAsync(timeout.Token);
            var stderr = process.StandardError.ReadToEndAsync(timeout.Token);
            await Task.WhenAll(stdout, stderr, process.WaitForExitAsync(timeout.Token));
            if (process.ExitCode != 0)
                return WriteFailure(output, "codex_command_failed");

            return WriteStatus(stdout.Result, output);
        }
        catch (OperationCanceledException)
        {
            TryKill(process);
            cancellationToken.ThrowIfCancellationRequested();
            return WriteFailure(output, "codex_timeout");
        }
        catch (Exception ex) when (ex is Win32Exception or IOException or InvalidOperationException)
        {
            TryKill(process);
            return WriteFailure(output, "codex_unavailable");
        }
    }

    internal static int WriteStatus(string json, TextWriter output)
    {
        try
        {
            using var document = JsonDocument.Parse(json);
            if (document.RootElement.ValueKind != JsonValueKind.Array)
                return WriteFailure(output, "codex_response_invalid");

            foreach (var server in document.RootElement.EnumerateArray())
            {
                if (server.ValueKind != JsonValueKind.Object
                    || !server.TryGetProperty("name", out var name)
                    || name.ValueKind != JsonValueKind.String
                    || name.GetString() != "githubie")
                    continue;

                if (!server.TryGetProperty("enabled", out var enabled)
                    || enabled.ValueKind is not (JsonValueKind.True or JsonValueKind.False)
                    || !server.TryGetProperty("auth_status", out var auth)
                    || auth.ValueKind != JsonValueKind.String
                    || string.IsNullOrWhiteSpace(auth.GetString()))
                    return WriteFailure(output, "codex_response_invalid");

                output.WriteLine(JsonSerializer.Serialize(new
                {
                    ok = true,
                    client = "codex",
                    server = "githubie",
                    enabled = enabled.GetBoolean(),
                    auth_status = auth.GetString(),
                    source = "codex_mcp_list",
                    connection_checked = false,
                    note = "Reports the invoking user's Codex auth field. Unknown is inconclusive; this is not a server connectivity or token validity check."
                }));
                return 0;
            }

            return WriteFailure(output, "githubie_not_configured", 1);
        }
        catch (JsonException)
        {
            return WriteFailure(output, "codex_response_invalid");
        }
    }

    private static int WriteFailure(TextWriter output, string code, int exitCode = 2)
    {
        output.WriteLine(JsonSerializer.Serialize(new { ok = false, client = "codex", server = "githubie", error = code }));
        return exitCode;
    }

    private static void TryKill(Process process)
    {
        try
        {
            if (!process.HasExited) process.Kill(entireProcessTree: true);
        }
        catch (Exception ex) when (ex is InvalidOperationException or Win32Exception or NotSupportedException)
        {
            // The process may already have exited.
        }
    }
}
