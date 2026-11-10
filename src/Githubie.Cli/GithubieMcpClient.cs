using System.Collections.Generic;

namespace Githubie.Cli;

/// <summary>既存CLIへ委譲する、サーバーを起動しないMCPクライアントです。</summary>
public static class GithubieMcpClient
{
    /// <summary>コマンドを既存CLIに転送し、その出力と終了コードを返します。</summary>
    public static Task<int> RunAsync(string[] args, TextWriter output, TextWriter error, CancellationToken cancellationToken)
        => RunCoreAsync(args, output, error, cancellationToken, CliApplication.RunAsync);

    internal static Task<int> RunIsolatedAsync(string[] args, TextWriter output, TextWriter error, CancellationToken cancellationToken)
        => RunCoreAsync(args, output, error, cancellationToken, CliApplication.RunIsolatedAsync);

    private static Task<int> RunCoreAsync(string[] args, TextWriter output, TextWriter error, CancellationToken cancellationToken,
        Func<string[], TextWriter, TextWriter, CancellationToken, Task<int>> dispatch)
    {
        ArgumentNullException.ThrowIfNull(args);
        var arguments = new List<string>(args);
        var commandIndex = 0;
        // 設定パスは既存CLIへそのまま渡し、単一設定ファイルの検証を共有します。
        while (commandIndex < arguments.Count && arguments[commandIndex] == "--config")
        {
            if (commandIndex + 1 >= arguments.Count) return dispatch(args, output, error, cancellationToken);
            commandIndex += 2;
        }
        if (commandIndex == arguments.Count || arguments[commandIndex] is "--help" or "help")
        {
            output.WriteLine("""
                githubiemcp [--config <install-root>/config/githubie.json] <command>
                  status | tools | test | call <tool> [<JSON> | --file <path>] | stdio
                  repo | issue | pr | auth | config   Forward existing githubie CLI commands.
                  --version                    Show the installed client version.
                Connects to the running Githubie MCP service; it does not start the server.
                Repository registration and token entry use the existing interactive UI.
                --config may name only this installation's config/githubie.json.
                """);
            return Task.FromResult(0);
        }
        if (arguments[commandIndex] is "--version" or "version")
            return dispatch(["version"], output, error, cancellationToken);
        if (arguments[commandIndex] is "start" or "stop" or "restart" or "host" or "service" or "setup-user" or "profile" or "autostart" or "configure" or "unconfigure")
        {
            error.WriteLine("[NG] githubiemcp is a client and cannot start or manage the server.");
            return Task.FromResult(1);
        }
        if (arguments[commandIndex] is "status" or "tools" or "test" or "call" or "stdio")
            arguments.Insert(commandIndex, "mcp");
        return dispatch(arguments.ToArray(), output, error, cancellationToken);
    }
}
