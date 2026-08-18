using FluentAssertions;
using Githubie.Cli;
using Xunit;

namespace Githubie.Cli.Tests;

public sealed class WindowsServiceManagementTests
{
    private sealed class RecordingServiceCommandExecutor : IServiceCommandExecutor
    {
        public IReadOnlyList<string>? CapturedArguments { get; private set; }

        public int ExitCode { get; set; }

        public Task<(int ExitCode, string Output)> RunAsync(IReadOnlyList<string> arguments, CancellationToken cancellationToken)
        {
            CapturedArguments = arguments;
            return Task.FromResult((ExitCode, "sc output"));
        }
    }

    [Fact]
    public async Task InstallAsync_BuildsScCreateWithQuotedArgumentsAndAutoStart()
    {
        var executor = new RecordingServiceCommandExecutor();
        using var output = new StringWriter();
        var manager = new WindowsServiceManager(executor, output);

        await manager.InstallAsync("C:\\Githubie\\bin\\Githubie.Server.exe", "C:\\Githubie\\config\\githubie.json", CancellationToken.None);

        executor.CapturedArguments.Should().Equal(
            "create", "Githubie",
            "binPath=", "\"C:\\Githubie\\bin\\Githubie.Server.exe\" \"C:\\Githubie\\config\\githubie.json\"",
            "start=", "auto",
            "DisplayName=", "Githubie MCP Server");
    }

    [Fact]
    public async Task UninstallAsync_BuildsScDeleteWithFixedServiceName()
    {
        var executor = new RecordingServiceCommandExecutor();
        using var output = new StringWriter();
        var manager = new WindowsServiceManager(executor, output);

        await manager.UninstallAsync(CancellationToken.None);

        executor.CapturedArguments.Should().Equal("delete", "Githubie");
    }

    [Theory]
    [InlineData("start")]
    [InlineData("stop")]
    [InlineData("query")]
    public async Task SimpleVerbs_UseFixedServiceName(string verb)
    {
        var executor = new RecordingServiceCommandExecutor();
        using var output = new StringWriter();
        var manager = new WindowsServiceManager(executor, output);

        Func<Task> action = verb switch
        {
            "start" => () => manager.StartAsync(CancellationToken.None),
            "stop" => () => manager.StopAsync(CancellationToken.None),
            _ => () => manager.StatusAsync(CancellationToken.None),
        };
        await action();

        executor.CapturedArguments.Should().Equal(verb, "Githubie");
    }

    [Fact]
    public async Task InstallAsync_ReturnsExecutorExitCode()
    {
        var executor = new RecordingServiceCommandExecutor { ExitCode = 5 };
        using var output = new StringWriter();
        var manager = new WindowsServiceManager(executor, output);

        var exitCode = await manager.InstallAsync("server.exe", "config.json", CancellationToken.None);

        exitCode.Should().Be(5);
    }
}
