using System.Diagnostics;
using System.Net.Http.Json;
using System.Reflection;
using Githubie.Application.Configuration;
using Githubie.Application.Credentials;
using Githubie.Application.Git;
using Githubie.Application.GitHub;
using Githubie.Infrastructure.Configuration;
using Githubie.Infrastructure.Credentials;
using Githubie.Server;

namespace Githubie.Cli;

/// <summary>
/// `githubie.exe`のコマンドディスパッチ本体です。テスト容易性のため、
/// 引数配列とTextWriterを受け取る純粋関数として実装します。
/// </summary>
public static class CliApplication
{
    public static async Task<int> RunAsync(string[] args, TextWriter output, TextWriter error, CancellationToken cancellationToken)
    {
        var (configPath, remaining) = ExtractConfigOption(args);
        var binDirectory = AppContext.BaseDirectory;
        var layout = GithubiePathLayout.FromBinDirectory(binDirectory);
        var effectiveConfigPath = configPath ?? Path.Combine(layout.ConfigDirectory, "githubie.json");

        return remaining switch
        {
            [] or ["help"] => PrintHelp(output),
            ["version"] => PrintVersion(output),
            ["logs"] => PrintLogsPath(output, layout),

            ["config", "check"] => await ConfigCheckAsync(effectiveConfigPath, output, cancellationToken),
            ["config", "show"] => await ConfigShowAsync(effectiveConfigPath, output, cancellationToken),

            ["repo", "list"] => await RepoListAsync(effectiveConfigPath, binDirectory, output, error, cancellationToken),
            ["repo", "status", var repo] => await RepoStatusAsync(effectiveConfigPath, binDirectory, repo, output, error, cancellationToken),
            ["repo", "rename", var oldRepo, var newRepo] => await RepoRenameAsync(
                effectiveConfigPath, binDirectory, oldRepo, newRepo, output, error, cancellationToken),

            ["auth", "test", var repo] => await AuthTestAsync(effectiveConfigPath, binDirectory, repo, output, error, cancellationToken),
            ["auth", "set", var repo] => AuthSet(layout, repo, output, error),
            ["auth", "delete", var repo] => AuthDelete(layout, repo, output, error),

            ["mcp", "status"] => await McpStatusAsync(effectiveConfigPath, output, error, cancellationToken),
            ["mcp", "tools"] => await McpToolsAsync(effectiveConfigPath, output, error, cancellationToken),
            ["mcp", "test"] => await McpTestAsync(effectiveConfigPath, output, error, cancellationToken),

            ["doctor"] => await DoctorAsync(effectiveConfigPath, binDirectory, output, cancellationToken),

            ["start"] => await new WindowsServiceManager(new ScServiceCommandExecutor(), output).StartAsync(cancellationToken),
            ["stop"] => await new WindowsServiceManager(new ScServiceCommandExecutor(), output).StopAsync(cancellationToken),
            ["restart"] => await new WindowsServiceManager(new ScServiceCommandExecutor(), output).RestartAsync(cancellationToken),
            ["status"] => await new WindowsServiceManager(new ScServiceCommandExecutor(), output).StatusAsync(cancellationToken),

            ["service", "install"] => await new WindowsServiceManager(new ScServiceCommandExecutor(), output)
                .InstallAsync(Path.Combine(binDirectory, "Githubie.Server.exe"), effectiveConfigPath, cancellationToken),
            ["service", "uninstall"] => await new WindowsServiceManager(new ScServiceCommandExecutor(), output).UninstallAsync(cancellationToken),
            ["service", "status"] => await new WindowsServiceManager(new ScServiceCommandExecutor(), output).StatusAsync(cancellationToken),

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

              help
              version
              logs

              config check
              config show

              repo list
              repo status <repository>
              repo rename <old-repository> <new-repository>

              auth test <repository>
              auth set <repository>
              auth delete <repository>

              mcp status
              mcp tools
              mcp test

              doctor

              start | stop | restart | status
              service install | uninstall | status

              --config <path>   githubie.json の場所を指定します（省略時は既定位置）
            """);
        return 0;
    }

    private static int PrintVersion(TextWriter output)
    {
        var version = Assembly.GetExecutingAssembly().GetName().Version?.ToString() ?? "unknown";
        output.WriteLine(version);
        return 0;
    }

    private static int PrintLogsPath(TextWriter output, GithubiePathLayout layout)
    {
        output.WriteLine(layout.LogsDirectory);
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

    private static async Task<int> ConfigCheckAsync(string configPath, TextWriter output, CancellationToken cancellationToken)
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

    private static async Task<int> RepoListAsync(string configPath, string binDirectory, TextWriter output, TextWriter error, CancellationToken cancellationToken)
    {
        var composition = await GithubieCompositionRoot.BuildAsync(configPath, binDirectory, cancellationToken);
        if (!composition.IsSuccess)
        {
            foreach (var e in composition.Errors)
            {
                error.WriteLine(e);
            }

            return 1;
        }

        var options = composition.Services!.GetService(typeof(GithubieOptions)) as GithubieOptions;
        foreach (var repositoryId in options?.Repositories.Keys ?? [])
        {
            output.WriteLine(repositoryId);
        }

        return 0;
    }

    private static async Task<int> RepoStatusAsync(string configPath, string binDirectory, string repository, TextWriter output, TextWriter error, CancellationToken cancellationToken)
    {
        var composition = await GithubieCompositionRoot.BuildAsync(configPath, binDirectory, cancellationToken);
        if (!composition.IsSuccess)
        {
            foreach (var e in composition.Errors)
            {
                error.WriteLine(e);
            }

            return 1;
        }

        var gateway = (IGitGateway)composition.Services!.GetService(typeof(IGitGateway))!;
        var result = await gateway.GetStatusAsync(repository, cancellationToken);
        if (!result.IsSuccess)
        {
            error.WriteLine($"[NG] {result.Error}");
            return 1;
        }

        var status = result.Value!;
        output.WriteLine($"repository: {status.Repository}");
        output.WriteLine($"local_branch: {status.LocalBranch}");
        output.WriteLine($"local_head: {status.LocalHead}");
        output.WriteLine($"ahead: {status.Ahead} behind: {status.Behind}");
        output.WriteLine($"working_tree_clean: {status.WorkingTreeClean}");
        foreach (var change in status.WorkingTreeChanges)
            output.WriteLine($"working_tree_change: {change.Status} {change.Path}");
        return 0;
    }

    private static async Task<int> RepoRenameAsync(
        string configPath, string binDirectory, string oldRepository, string newRepository,
        TextWriter output, TextWriter error, CancellationToken cancellationToken)
    {
        var composition = await GithubieCompositionRoot.BuildAsync(configPath, binDirectory, cancellationToken);
        if (!composition.IsSuccess)
        {
            foreach (var item in composition.Errors) error.WriteLine(item);
            return 1;
        }
        var service = (Application.Repositories.IRepositoryManagementService)composition.Services!
            .GetService(typeof(Application.Repositories.IRepositoryManagementService))!;
        var result = await service.RenameAsync(oldRepository, newRepository, cancellationToken);
        output.WriteLine(result.IsSuccess ? $"[OK] renamed: {oldRepository} -> {newRepository}" : $"[NG] {result.Error}");
        return result.IsSuccess ? 0 : 1;
    }

    private static async Task<int> AuthTestAsync(string configPath, string binDirectory, string repository, TextWriter output, TextWriter error, CancellationToken cancellationToken)
    {
        var composition = await GithubieCompositionRoot.BuildAsync(configPath, binDirectory, cancellationToken);
        if (!composition.IsSuccess)
        {
            foreach (var e in composition.Errors)
            {
                error.WriteLine(e);
            }

            return 1;
        }

        var gateway = (IGitHubRepositoryGateway)composition.Services!.GetService(typeof(IGitHubRepositoryGateway))!;
        var result = await gateway.ListBranchesAsync(repository, cancellationToken);
        output.WriteLine(result.IsSuccess ? "[OK] GitHub API authentication succeeded" : $"[NG] {result.Error}");
        return result.IsSuccess ? 0 : 1;
    }

    private static int AuthSet(GithubiePathLayout layout, string repository, TextWriter output, TextWriter error)
    {
        if (!OperatingSystem.IsWindows())
        {
            error.WriteLine("[NG] auth set requires Windows.");
            return 1;
        }

        output.Write("Personal Access Token: ");
        var token = ReadMaskedLine();
        output.WriteLine($"({token.Length} characters captured)");

        var store = new DpapiFileTokenStore(layout.SecretsDirectory);
        var result = store.Save(repository, token);
        output.WriteLine(result.IsSuccess ? "[OK] token saved" : $"[NG] {result.Error}");
        return result.IsSuccess ? 0 : 1;
    }

    private static int AuthDelete(GithubiePathLayout layout, string repository, TextWriter output, TextWriter error)
    {
        if (!OperatingSystem.IsWindows())
        {
            error.WriteLine("[NG] auth delete requires Windows.");
            return 1;
        }

        var store = new DpapiFileTokenStore(layout.SecretsDirectory);
        var result = store.Delete(repository);
        output.WriteLine(result.IsSuccess ? "[OK] token deleted" : $"[NG] {result.Error}");
        return result.IsSuccess ? 0 : 1;
    }

    private static ReadOnlySpan<char> ReadMaskedLine()
    {
        var chars = new List<char>();
        while (true)
        {
            var key = Console.ReadKey(intercept: true);
            if (key.Key == ConsoleKey.Enter)
            {
                break;
            }

            if (key.Key == ConsoleKey.Backspace)
            {
                if (chars.Count > 0)
                {
                    chars.RemoveAt(chars.Count - 1);
                    Console.Write("\b \b");
                }

                continue;
            }

            chars.Add(key.KeyChar);
            Console.Write('*');
        }

        Console.WriteLine();

        // 貼り付け時に混入しやすい先頭/末尾の空白・改行を除去する。
        return new string(chars.ToArray()).Trim().ToCharArray();
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

    private static async Task<string?> SendMcpRequestAsync(GithubieOptions options, string method, CancellationToken cancellationToken)
    {
        using var httpClient = new HttpClient { Timeout = TimeSpan.FromSeconds(5) };
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
            : new { jsonrpc = "2.0", id = 1, method };

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

    private static async Task<int> DoctorAsync(string configPath, string binDirectory, TextWriter output, CancellationToken cancellationToken)
    {
        var failures = 0;

        var options = await TryLoadOptionsAsync(configPath, output, cancellationToken);
        if (options is null)
        {
            output.WriteLine("[NG] Configuration");
            return 1;
        }

        output.WriteLine("[OK] Configuration");

        var gitFound = await CheckGitAsync(cancellationToken);
        output.WriteLine(gitFound ? "[OK] Git" : "[NG] Git");
        if (!gitFound)
        {
            failures++;
        }

        var composition = await GithubieCompositionRoot.BuildAsync(configPath, binDirectory, cancellationToken);
        if (!composition.IsSuccess)
        {
            output.WriteLine("[NG] Service composition");
            foreach (var e in composition.Errors)
            {
                output.WriteLine($"     {e}");
            }

            return failures + 1;
        }

        output.WriteLine("[OK] Service composition");

        var tokenStore = (IApiTokenStore)composition.Services!.GetService(typeof(IApiTokenStore))!;
        var gitGateway = (IGitGateway)composition.Services!.GetService(typeof(IGitGateway))!;

        foreach (var repositoryId in options.Repositories.Keys)
        {
            var tokenResult = tokenStore.Read(repositoryId);
            output.WriteLine(tokenResult.IsSuccess ? $"[OK] Token: {repositoryId}" : $"[NG] Token: {repositoryId} ({tokenResult.Error})");
            if (!tokenResult.IsSuccess)
            {
                failures++;
            }

            var statusResult = await gitGateway.GetStatusAsync(repositoryId, cancellationToken);
            output.WriteLine(statusResult.IsSuccess ? $"[OK] Repository: {repositoryId}" : $"[NG] Repository: {repositoryId} ({statusResult.Error})");
            if (!statusResult.IsSuccess)
            {
                failures++;
            }
        }

        return failures == 0 ? 0 : 1;
    }

    private static async Task<bool> CheckGitAsync(CancellationToken cancellationToken)
    {
        try
        {
            using var process = Process.Start(new ProcessStartInfo("git", "--version")
            {
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
            });

            if (process is null)
            {
                return false;
            }

            await process.WaitForExitAsync(cancellationToken);
            return process.ExitCode == 0;
        }
        catch (System.ComponentModel.Win32Exception)
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
