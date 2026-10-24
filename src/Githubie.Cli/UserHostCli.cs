using System.Diagnostics;
using System.Security.Cryptography;
using System.Security.Principal;
using System.Text.Json;
using Githubie.Application.Configuration;
using Githubie.Infrastructure.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Githubie.Cli;

/// <summary>ユーザーホスト専用の管理/HTTPクライアントです。Repository DBやtoken storeは開きません。</summary>
internal static class UserHostCli
{
    public static async Task<int> RunAsync(string[] args, TextWriter output, TextWriter error, CancellationToken cancellationToken)
    {
        try
        {
            var layout = GithubiePathLayout.ForCurrentUser(AppContext.BaseDirectory);
            var profiles = new UserProfileStore(layout);
            var executable = Path.Combine(layout.BinDirectory, "Githubie.Server.exe");
            var startup = new UserAutostart(executable);
            if (args is ["auth", "set", _, "--console"])
            { error.WriteLine("[NG] CONSOLE_INPUT_UNSUPPORTED: use auth set <repository> to open the host's token UI."); return 1; }
            if (args.Length == 2 && args[0] is "configure" or "unconfigure")
                return await ClientRegistrationCli.RunAsync(args[1], args[0] == "configure", false, output, error, cancellationToken);
            if (args is ["configure", var legacyClient, "--replace-legacy"])
                return await ClientRegistrationCli.RunAsync(legacyClient, true, true, output, error, cancellationToken);
            if (args is ["setup-user", var expectedSid])
            {
                using var identity = WindowsIdentity.GetCurrent();
                if (identity.User?.Value != expectedSid || Process.GetCurrentProcess().SessionId == 0
                    || new WindowsPrincipal(identity).IsInRole(WindowsBuiltInRole.Administrator))
                    throw new UnauthorizedAccessException("User setup requires the intended non-elevated user context.");
                var profile = await profiles.InitializeAsync(cancellationToken);
                var enabled = startup.Initialize();
                output.WriteLine(JsonSerializer.Serialize(new { owner_sid = profile.OwnerSid, session_id = Process.GetCurrentProcess().SessionId,
                    autostart_enabled = enabled, verified = startup.IsEnabled() == enabled }));
                return 0;
            }
            if (args is ["profile", "initialize"])
            {
                var profile = await profiles.InitializeAsync(cancellationToken);
                output.WriteLine($"User profile initialized: {profile.ProfileId}. Legacy service data was not imported.");
                return 0;
            }
            var profileIdentity = profiles.ReadIdentity();
            if (args is ["mcp", "auth", "status", "codex"])
                return await CodexMcpAuthStatusCommand.RunAsync(output, cancellationToken);
            if (args is ["config", "show"])
            {
                output.WriteLine(await File.ReadAllTextAsync(Path.Combine(layout.ConfigDirectory, "githubie.json"), cancellationToken));
                return 0;
            }
            if (args is ["config", "check"] or ["config", "check", "--moyai"])
            {
                await using var file = File.OpenRead(Path.Combine(layout.ConfigDirectory, "githubie.json"));
                var loaded = await new JsonGithubieOptionsLoader().LoadAsync(file, cancellationToken);
                output.WriteLine(loaded.IsSuccess ? "configuration_valid" : "configuration_invalid");
                return loaded.IsSuccess ? 0 : 1;
            }
            if (args is ["profile", "status"])
            {
                output.WriteLine(JsonSerializer.Serialize(new { profile_id = profileIdentity.ProfileId, owner_sid = profileIdentity.OwnerSid,
                    root = Path.GetDirectoryName(layout.ConfigDirectory) }));
                return 0;
            }
            if (args is ["logs"]) { output.WriteLine(layout.LogsDirectory); return 0; }
            if (args is ["autostart", var setting])
            {
                var enabled = setting switch { "on" => startup.SetEnabled(true), "off" => startup.SetEnabled(false),
                    "status" => startup.IsEnabled(), _ => throw new ArgumentException("Use autostart on|off|status.") };
                output.WriteLine(enabled ? "enabled" : "disabled");
                return 0;
            }
            if (args is ["host", "start"] or ["start"])
            {
                try
                {
                    var running = new UserHostConnectionStore(layout.DataDirectory).Read(profileIdentity);
                    var response = await UserHostControlPipe.SendAsync(running, "status", null, cancellationToken);
                    if (response.Success) { output.WriteLine("already_running"); return 0; }
                }
                catch (Exception exception) when (exception is IOException or ArgumentException or CryptographicException) { }
                using var process = StartHost(executable);
                var ready = await WaitForHostAsync(layout, profiles, process, cancellationToken);
                output.WriteLine(ready ? "ready" : "startup_failed");
                return ready ? 0 : 1;
            }
            var connection = new UserHostConnectionStore(layout.DataDirectory).Read(profileIdentity);
            if (args is ["host", "endpoint"]) { output.WriteLine(connection.Endpoint); return 0; }
            using var transportServices = new ServiceCollection()
                .AddHttpClient("UserHost", client => client.Timeout = Timeout.InfiniteTimeSpan)
                .ConfigurePrimaryHttpMessageHandler(() => new HttpClientHandler { AllowAutoRedirect = false, UseProxy = false })
                .Services.BuildServiceProvider();
            var transport = new UserHostMcpClient(transportServices.GetRequiredService<IHttpClientFactory>());
            if (args is ["mcp", "stdio"]) return await UserHostStdioBridge.RunAsync(connection, transport, Console.In, output, cancellationToken);
            var control = args switch
            {
                ["host", "status"] or ["status"] or ["doctor"] => ("status", (string?)null),
                ["host", "stop"] or ["stop"] or ["host", "restart"] or ["restart"] => ("stop", (string?)null),
                ["auth", "set", var repository] => ("auth_set", repository),
                ["auth", "delete", var repository] => ("auth_delete", repository),
                ["auth", "test", var repository] => ("auth_test", repository),
                _ => ((string?)null, (string?)null),
            };
            if (control.Item1 is not null)
            {
                var response = await UserHostControlPipe.SendAsync(connection, control.Item1, control.Item2, cancellationToken);
                if (response.Success && (args is ["restart"] or ["host", "restart"]))
                {
                    using var previous = Process.GetProcessById(connection.ProcessId);
                    using var stopTimeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                    stopTimeout.CancelAfter(TimeSpan.FromSeconds(10));
                    await previous.WaitForExitAsync(stopTimeout.Token);
                    using var process = StartHost(executable);
                    response = new UserHostControlResponse(await WaitForHostAsync(layout, profiles, process, cancellationToken), "restarted");
                }
                (response.Success ? output : error).WriteLine(response.Code);
                return response.Success ? 0 : 1;
            }
            var (method, parameters) = ParseMcpCommand(args);
            var responseJson = await transport.SendAsync(connection, JsonSerializer.Serialize(new { jsonrpc = "2.0", id = 1, method, @params = parameters }), cancellationToken);
            if (responseJson is null) throw new IOException("The host did not return a response.");
            output.WriteLine(responseJson);
            return IsSuccess(responseJson) ? 0 : 1;
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        { error.WriteLine("[NG] The user host did not respond in time."); return 1; }
        catch (Exception exception) when (exception is IOException or InvalidDataException or UnauthorizedAccessException or ArgumentException or CryptographicException
            or InvalidOperationException or System.ComponentModel.Win32Exception or HttpRequestException or JsonException)
        {
            error.WriteLine($"[NG] User host command failed ({exception.GetType().Name}). Check profile status and host logs.");
            return 1;
        }
    }

    private static Process StartHost(string executable)
    {
        var start = new ProcessStartInfo(executable) { UseShellExecute = false, CreateNoWindow = true };
        start.ArgumentList.Add("--user-host");
        return Process.Start(start) ?? throw new IOException("The user host could not start.");
    }

    private static async Task<bool> WaitForHostAsync(GithubiePathLayout layout, UserProfileStore profiles, Process process, CancellationToken cancellationToken)
    {
        var deadline = DateTime.UtcNow.AddSeconds(15);
        while (!process.HasExited && DateTime.UtcNow < deadline)
        {
            try
            {
                var connection = new UserHostConnectionStore(layout.DataDirectory).Read(profiles.ReadIdentity());
                if (connection.ProcessId == process.Id && (await UserHostControlPipe.SendAsync(connection, "status", null, cancellationToken)).Success) return true;
            }
            catch (Exception exception) when (exception is IOException or ArgumentException or CryptographicException) { }
            await Task.Delay(200, cancellationToken);
        }
        return false;
    }

    internal static (string Method, object Parameters) ParseMcpCommand(string[] args) => args switch
    {
        ["repo", "list"] => Call("list_projects", new { }),
        ["repo", "status", var repository] => Call("github_repository_status", new { repository }),
        ["repo", "description", "get", var repository] => Call("github_repository_description_get", new { repository }),
        ["repo", "description", "update", var repository, var description] => Call("github_repository_description_update", new { repository, description }),
        ["repo", "rename", var old_repository, var new_repository] => Call("github_repository_rename", new { old_repository, new_repository }),
        ["issue", "list", var repository] => Call("github_issue_list", new { repository, state = (string?)null }),
        ["issue", "list", var repository, "--state", var state] when state is "open" or "closed" => Call("github_issue_list", new { repository, state }),
        ["issue", "get", var repository, var number] when int.TryParse(number, out var issue_number) => Call("github_issue_get", new { repository, issue_number }),
        ["mcp", "status"] or ["mcp", "test"] => ("initialize", new { protocolVersion = "2025-06-18", capabilities = new { }, clientInfo = new { name = "githubie-cli", version = "2.0" } }),
        ["mcp", "tools"] => ("tools/list", new { }),
        ["mcp", "call", var tool] => Call(tool, new { }),
        ["mcp", "call", var tool, var json] => Call(tool, ParseArguments(json)),
        ["mcp", "call", var tool, "--file", var path] => Call(tool, ParseArguments(File.ReadAllText(path))),
        _ => throw new ArgumentException("Unsupported user-host command. Use host, autostart, repo, issue or mcp call; token input uses auth set without --console."),
    };

    private static JsonElement ParseArguments(string json)
    {
        using var document = JsonDocument.Parse(json);
        if (document.RootElement.ValueKind != JsonValueKind.Object) throw new ArgumentException("MCP arguments must be an object.");
        return document.RootElement.Clone();
    }
    private static (string, object) Call(string name, object arguments) => ("tools/call", new { name, arguments });

    internal static bool IsSuccess(string json)
    {
        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;
        return !root.TryGetProperty("error", out _) && root.TryGetProperty("result", out var result)
            && !(result.TryGetProperty("isError", out var isError) && isError.ValueKind == JsonValueKind.True)
            && !(result.TryGetProperty("structuredContent", out var content) && content.TryGetProperty("ok", out var ok) && ok.ValueKind == JsonValueKind.False);
    }

}
