using System.Diagnostics;

namespace Githubie.Cli;

/// <summary>
/// `sc.exe`をサブプロセス実行してWindows Serviceを管理します。
/// .NETのWindows Service管理APIやWMIではなく、固定コマンド文法のscコマンドを使います。
/// </summary>
public interface IServiceCommandExecutor
{
    Task<(int ExitCode, string Output)> RunAsync(IReadOnlyList<string> arguments, CancellationToken cancellationToken);
}

public sealed class ScServiceCommandExecutor : IServiceCommandExecutor
{
    private static readonly string ScPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.System), "sc.exe");

    public async Task<(int ExitCode, string Output)> RunAsync(IReadOnlyList<string> arguments, CancellationToken cancellationToken)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = ScPath,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true,
        };

        foreach (var argument in arguments)
        {
            startInfo.ArgumentList.Add(argument);
        }

        using var process = new Process { StartInfo = startInfo };
        process.Start();

        var output = await process.StandardOutput.ReadToEndAsync(cancellationToken);
        var error = await process.StandardError.ReadToEndAsync(cancellationToken);
        await process.WaitForExitAsync(cancellationToken);

        return (process.ExitCode, output + error);
    }
}

/// <summary>
/// Githubie Windows Serviceの install/uninstall/start/stop/restart/status を扱います。
/// </summary>
public sealed class WindowsServiceManager(IServiceCommandExecutor executor, TextWriter output)
{
    public const string ServiceName = "Githubie";

    public async Task<int> InstallAsync(string serverExecutablePath, string configPath, CancellationToken cancellationToken)
    {
        var binPath = $"\"{serverExecutablePath}\" \"{configPath}\"";
        var (exitCode, result) = await executor.RunAsync(
            ["create", ServiceName, "binPath=", binPath, "start=", "auto", "DisplayName=", "Githubie MCP Server"], cancellationToken);

        output.WriteLine(result.Trim());
        return exitCode;
    }

    public async Task<int> UninstallAsync(CancellationToken cancellationToken)
    {
        var (exitCode, result) = await executor.RunAsync(["delete", ServiceName], cancellationToken);
        output.WriteLine(result.Trim());
        return exitCode;
    }

    public Task<int> StartAsync(CancellationToken cancellationToken) => RunSimpleAsync("start", cancellationToken);

    public Task<int> StopAsync(CancellationToken cancellationToken) => RunSimpleAsync("stop", cancellationToken);

    public async Task<int> RestartAsync(CancellationToken cancellationToken)
    {
        await StopAsync(cancellationToken);
        return await StartAsync(cancellationToken);
    }

    public Task<int> StatusAsync(CancellationToken cancellationToken) => RunSimpleAsync("query", cancellationToken);

    private async Task<int> RunSimpleAsync(string verb, CancellationToken cancellationToken)
    {
        var (exitCode, result) = await executor.RunAsync([verb, ServiceName], cancellationToken);
        output.WriteLine(result.Trim());
        return exitCode;
    }
}
