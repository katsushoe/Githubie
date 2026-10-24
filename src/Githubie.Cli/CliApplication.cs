using System.Net.Http.Json;
using System.Reflection;
using System.Text.Json;
using Githubie.Application.Configuration;
using Githubie.Infrastructure.Configuration;

namespace Githubie.Cli;

/// <summary>
/// `githubie.exe`のコマンドディスパッチ本体です。テスト容易性のため、
/// 引数配列とTextWriterを受け取る純粋関数として実装します。
/// </summary>
public static class CliApplication
{
    private static readonly TimeSpan McpQueryTimeout = TimeSpan.FromSeconds(5);
    // tools/callは承認とToken入力を含み得るため時間制限を設けず、呼び出し側のキャンセルで中断する。
    private static readonly TimeSpan McpCallTimeout = Timeout.InfiniteTimeSpan;

    public static async Task<int> RunAsync(string[] args, TextWriter output, TextWriter error, CancellationToken cancellationToken)
    {
        var (configPath, remaining) = ExtractConfigOption(args);
        var layout = GithubiePathLayout.FromBinDirectory(AppContext.BaseDirectory);
        var effectiveConfigPath = configPath ?? Path.Combine(layout.ConfigDirectory, "githubie.json");
        if (remaining.Length > 0 && remaining[0] is "repo" or "issue" or "auth" or "doctor" or "start" or "stop" or "restart" or "status")
        {
            if (configPath is not null)
            {
                error.WriteLine("[NG] Business commands use the current user host; --config cannot select an offline database.");
                return 1;
            }
            return await UserHostCli.RunAsync(remaining, output, error, cancellationToken);
        }
        if (remaining.Length > 0 && remaining[0] is "host" or "autostart" or "profile" or "setup-user" or "configure" or "unconfigure" or "logs")
            return await UserHostCli.RunAsync(remaining, output, error, cancellationToken);
        if (remaining is ["mcp", "stdio"] || (configPath is null &&
            File.Exists(Path.Combine(Path.GetDirectoryName(GithubiePathLayout.ForCurrentUser(AppContext.BaseDirectory).ConfigDirectory)!, "profile.json")) &&
            remaining.Length > 0 && remaining[0] is "mcp" or "config"))
            return await UserHostCli.RunAsync(remaining, output, error, cancellationToken);
        return remaining switch
        {
            [] or ["help"] => PrintHelp(output),
            ["version"] => PrintVersion(output),
            ["config", "check"] => await ConfigCheckAsync(effectiveConfigPath, false, output, cancellationToken),
            ["config", "check", "--moyai"] => await ConfigCheckAsync(effectiveConfigPath, true, output, cancellationToken),
            ["config", "show"] => await ConfigShowAsync(effectiveConfigPath, output, cancellationToken),
            ["mcp", "auth", "status", "codex"] => await CodexMcpAuthStatusCommand.RunAsync(output, cancellationToken),
            ["mcp", "status"] => await McpStatusAsync(effectiveConfigPath, output, error, cancellationToken),
            ["mcp", "tools"] => await McpToolsAsync(effectiveConfigPath, output, error, cancellationToken),
            ["mcp", "test"] => await McpTestAsync(effectiveConfigPath, output, error, cancellationToken),
            ["mcp", "call", var tool] => await McpCallAsync(effectiveConfigPath, tool, "{}", output, error, cancellationToken),
            ["mcp", "call", var tool, var json] => await McpCallAsync(effectiveConfigPath, tool, json, output, error, cancellationToken),
            ["mcp", "call", var tool, "--file", var path] => await McpCallFileAsync(effectiveConfigPath, tool, path, output, error, cancellationToken),
            ["service", "status"] => await new WindowsServiceManager(new ScServiceCommandExecutor(), output).StatusAsync(cancellationToken),
            ["service", "uninstall"] => await new WindowsServiceManager(new ScServiceCommandExecutor(), output).UninstallAsync(cancellationToken),
            _ => PrintUnknown(remaining, error),
        };
    }

    private static (string? ConfigPath, string[] Remaining) ExtractConfigOption(string[] args)
    {
        var list = new List<string>(args);
        var index = list.IndexOf("--config");
        if (index < 0 || index + 1 >= list.Count)
        {
            return (null, list.ToArray());
        }

        var configPath = list[index + 1];
        list.RemoveRange(index, 2);
        return (configPath, list.ToArray());
    }

    private static int PrintHelp(TextWriter output)
    {
        output.WriteLine("""
            githubie <command>
              help | version | logs
              profile initialize|status
              host start|stop|restart|status|endpoint
              start|stop|restart|status|doctor
              autostart on|off|status
              configure codex|claude [--replace-legacy]
              unconfigure codex|claude
              config check [--moyai] | config show
              repo list | repo status <repository>
              repo description get <repository>
              repo description update <repository> <description>
              repo rename <old-repository> <new-repository>
              issue list <repository> [--state open|closed]
              issue get <repository> <number>
              auth set <repository>
                Opens the current user host's dedicated token GUI.
                The GUI waits until the token is entered or cancelled.
                CANCELLED, DIALOG_FAILED, SAVE_FAILED leave an existing token unchanged.
              auth delete <repository> | auth test <repository>
              mcp stdio | mcp status | mcp tools | mcp test
              mcp auth status codex
              mcp call <tool> [<JSON-object> | --file <arguments-file>]
                Branch/tag creation source (required; no default).
              service status | service uninstall
                Legacy Service diagnostics/removal only. Use host commands for 2.0.
            """);
        return 0;
    }

    private static int PrintVersion(TextWriter output)
    {
        var version = Assembly.GetExecutingAssembly().GetName().Version?.ToString() ?? "unknown";
        output.WriteLine(version);
        return 0;
    }

    private static async Task<GithubieOptions?> TryLoadOptionsAsync(string configPath, TextWriter output, CancellationToken cancellationToken)
    {
        if (!File.Exists(configPath))
        {
            output.WriteLine($"[NG] config file not found: {configPath}");
            return null;
        }

        await using var stream = File.OpenRead(configPath);
        var result = await new JsonGithubieOptionsLoader().LoadAsync(stream, cancellationToken);
        if (!result.IsSuccess)
        {
            foreach (var e in result.Errors)
            {
                output.WriteLine($"[NG] {e.Path}: {e.Message} ({e.Code})");
            }

            return null;
        }

        return result.Options;
    }

    private static async Task<int> ConfigCheckAsync(
        string configPath, bool moyaiIntegration, TextWriter output, CancellationToken cancellationToken)
    {
        var options = await TryLoadOptionsAsync(configPath, output, cancellationToken);
        if (options is null)
        {
            return 1;
        }

        var errors = 0;
        foreach (var (repositoryId, repository) in options.Repositories)
        {
            var fullPath = Path.GetFullPath(repository.LocalRoot);
            var localRootExists = Directory.Exists(fullPath);
            var gitExists = localRootExists && Directory.Exists(Path.Combine(fullPath, ".git"));

            output.WriteLine(localRootExists ? $"[OK] {repositoryId}: local_root exists" : $"[NG] {repositoryId}: local_root not found ({fullPath})");
            output.WriteLine(gitExists ? $"[OK] {repositoryId}: .git exists" : $"[NG] {repositoryId}: .git not found");

            if (!localRootExists || !gitExists)
            {
                errors++;
            }
        }

        // 既定は単体動作モードの検査。--moyai指定時だけMoyai連携に必要な設定も検査する。
        if (moyaiIntegration)
        {
            var moyaiErrors = JsonGithubieOptionsLoader.ValidateMoyaiIntegration(options);
            foreach (var moyaiError in moyaiErrors)
            {
                output.WriteLine($"[NG] {moyaiError.Path}: {moyaiError.Message}");
            }

            if (moyaiErrors.Count == 0) output.WriteLine("[OK] Moyai integration settings");
            errors += moyaiErrors.Count;
            foreach (var repositoryId in JsonGithubieOptionsLoader.FindUnmappedRepositories(options))
            {
                output.WriteLine($"[WARN] {repositoryId}: no Moyai Project UUID in provider_authentication.projects; Moyai requests are rejected, direct calls follow direct_connection");
            }
        }

        output.WriteLine(errors == 0 ? "[OK] config check passed" : $"[NG] config check found {errors} issue(s)");
        return errors == 0 ? 0 : 1;
    }

    private static async Task<int> ConfigShowAsync(string configPath, TextWriter output, CancellationToken cancellationToken)
    {
        var options = await TryLoadOptionsAsync(configPath, output, cancellationToken);
        if (options is null)
        {
            return 1;
        }

        output.WriteLine($"mcp_port: {options.McpPort}");
        output.WriteLine($"mcp_path: {options.McpPath}");
        foreach (var (repositoryId, repository) in options.Repositories)
        {
            output.WriteLine($"- {repositoryId}: {repository.GitHubOwner}/{repository.GitHubRepo} ({repository.LocalRoot})");
        }

        return 0;
    }

    private static async Task<int> McpStatusAsync(string configPath, TextWriter output, TextWriter error, CancellationToken cancellationToken)
    {
        var options = await TryLoadOptionsAsync(configPath, output, cancellationToken);
        if (options is null)
        {
            return 1;
        }

        var response = await SendMcpRequestAsync(options, "initialize", cancellationToken);
        if (response is null)
        {
            error.WriteLine("[NG] MCP endpoint unreachable");
            return 1;
        }

        output.WriteLine("[OK] MCP endpoint responded");
        output.WriteLine(response);
        return 0;
    }

    private static async Task<int> McpToolsAsync(string configPath, TextWriter output, TextWriter error, CancellationToken cancellationToken)
    {
        var options = await TryLoadOptionsAsync(configPath, output, cancellationToken);
        if (options is null)
        {
            return 1;
        }

        var response = await SendMcpRequestAsync(options, "tools/list", cancellationToken);
        if (response is null)
        {
            error.WriteLine("[NG] MCP endpoint unreachable");
            return 1;
        }

        output.WriteLine(response);
        return 0;
    }

    private static async Task<int> McpTestAsync(string configPath, TextWriter output, TextWriter error, CancellationToken cancellationToken)
    {
        var status = await McpStatusAsync(configPath, output, error, cancellationToken);
        return status;
    }

    private static async Task<int> McpCallFileAsync(
        string configPath, string tool, string argumentsPath, TextWriter output, TextWriter error,
        CancellationToken cancellationToken)
    {
        if (!File.Exists(argumentsPath))
        {
            error.WriteLine($"[NG] arguments file not found: {argumentsPath}");
            return 1;
        }

        var argumentsJson = await File.ReadAllTextAsync(argumentsPath, cancellationToken);
        return await McpCallAsync(configPath, tool, argumentsJson, output, error, cancellationToken);
    }

    private static async Task<int> McpCallAsync(
        string configPath, string tool, string argumentsJson, TextWriter output, TextWriter error,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(tool))
        {
            error.WriteLine("[NG] MCP tool name is required");
            return 1;
        }

        JsonElement arguments;
        try
        {
            using var document = JsonDocument.Parse(argumentsJson);
            if (document.RootElement.ValueKind != JsonValueKind.Object)
            {
                error.WriteLine("[NG] MCP tool arguments must be a JSON object");
                return 1;
            }
            arguments = document.RootElement.Clone();
        }
        catch (JsonException exception)
        {
            error.WriteLine($"[NG] invalid arguments JSON: {exception.Message}");
            return 1;
        }

        var options = await TryLoadOptionsAsync(configPath, output, cancellationToken);
        if (options is null) return 1;

        var response = await SendMcpRequestAsync(options, "tools/call", cancellationToken, new
        {
            name = tool,
            arguments,
        });
        if (response is null)
        {
            error.WriteLine("[NG] MCP endpoint unreachable");
            return 1;
        }

        var json = ExtractMcpJson(response);
        output.WriteLine(json);
        return IsMcpCallSuccess(json) ? 0 : 1;
    }

    private static async Task<string?> SendMcpRequestAsync(
        GithubieOptions options, string method, CancellationToken cancellationToken, object? parameters = null)
    {
        using var httpClient = new HttpClient { Timeout = ResolveMcpTimeout(method) };
        var uri = new Uri($"http://127.0.0.1:{options.McpPort}{options.McpPath}");

        object payload = method == "initialize"
            ? new
            {
                jsonrpc = "2.0",
                id = 1,
                method,
                @params = new
                {
                    protocolVersion = "2025-06-18",
                    capabilities = new { },
                    clientInfo = new { name = "githubie-cli", version = "0.1" },
                },
            }
            : parameters is null
                ? new { jsonrpc = "2.0", id = 1, method }
                : new { jsonrpc = "2.0", id = 1, method, @params = parameters };

        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Post, uri)
            {
                Content = JsonContent.Create(payload),
            };
            request.Headers.Accept.Add(new System.Net.Http.Headers.MediaTypeWithQualityHeaderValue("application/json"));
            request.Headers.Accept.Add(new System.Net.Http.Headers.MediaTypeWithQualityHeaderValue("text/event-stream"));

            var response = await httpClient.SendAsync(request, cancellationToken);
            return await response.Content.ReadAsStringAsync(cancellationToken);
        }
        catch (HttpRequestException)
        {
            return null;
        }
        catch (TaskCanceledException)
        {
            return null;
        }
    }

    internal static TimeSpan ResolveMcpTimeout(string method) =>
        string.Equals(method, "tools/call", StringComparison.Ordinal) ? McpCallTimeout : McpQueryTimeout;

    private static string ExtractMcpJson(string response)
    {
        foreach (var line in response.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries))
        {
            if (line.StartsWith("data:", StringComparison.Ordinal)) return line[5..].TrimStart();
        }
        return response;
    }

    private static bool IsMcpCallSuccess(string responseJson)
    {
        try
        {
            using var document = JsonDocument.Parse(responseJson);
            var root = document.RootElement;
            if (root.TryGetProperty("error", out _)) return false;
            if (!root.TryGetProperty("result", out var result)) return false;
            if (result.TryGetProperty("isError", out var isError) && isError.ValueKind == JsonValueKind.True) return false;
            if (result.TryGetProperty("structuredContent", out var structured) &&
                structured.TryGetProperty("ok", out var ok) && ok.ValueKind == JsonValueKind.False) return false;
            return true;
        }
        catch (JsonException)
        {
            return false;
        }
    }

    private static int PrintUnknown(string[] remaining, TextWriter error)
    {
        error.WriteLine($"unknown command: {string.Join(' ', remaining)}");
        return 1;
    }
}
