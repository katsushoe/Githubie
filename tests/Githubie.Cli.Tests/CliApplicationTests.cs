using FluentAssertions;
using Githubie.Cli;
using Xunit;

namespace Githubie.Cli.Tests;

public sealed class CliApplicationTests
{
    [Fact]
    public async Task RunAsync_Help_DescribesConsoleAuthOption()
    {
        using var output = new StringWriter();
        using var error = new StringWriter();

        var exitCode = await CliApplication.RunAsync(["help"], output, error, TestContext.Current.CancellationToken);

        exitCode.Should().Be(0);
        output.ToString().Should().Contain("auth set <repository> [--console]");
    }

    [Fact]
    public async Task RunAsync_NoArgs_PrintsHelpAndReturnsZero()
    {
        using var output = new StringWriter();
        using var error = new StringWriter();

        var exitCode = await CliApplication.RunAsync([], output, error, TestContext.Current.CancellationToken);

        exitCode.Should().Be(0);
        output.ToString().Should().Contain("githubie <command>");
    }

    [Fact]
    public async Task RunAsync_Version_PrintsAssemblyVersion()
    {
        using var output = new StringWriter();
        using var error = new StringWriter();

        var exitCode = await CliApplication.RunAsync(["version"], output, error, TestContext.Current.CancellationToken);

        exitCode.Should().Be(0);
        output.ToString().Trim().Should().MatchRegex(@"^\d+\.\d+\.\d+\.\d+$");
    }

    [Fact]
    public async Task RunAsync_UnknownCommand_ReturnsNonZeroAndWritesToError()
    {
        using var output = new StringWriter();
        using var error = new StringWriter();

        var exitCode = await CliApplication.RunAsync(["not-a-command"], output, error, TestContext.Current.CancellationToken);

        exitCode.Should().Be(1);
        error.ToString().Should().Contain("unknown command");
    }

    [Fact]
    public async Task RunAsync_McpCall_InvalidJson_ReturnsNonZeroWithoutNetworkCall()
    {
        using var output = new StringWriter();
        using var error = new StringWriter();

        var exitCode = await CliApplication.RunAsync(
            ["mcp", "call", "github_pr_list", "not-json"], output, error,
            TestContext.Current.CancellationToken);

        exitCode.Should().Be(1);
        error.ToString().Should().Contain("invalid arguments JSON");
    }

    [Fact]
    public async Task RunAsync_McpCall_ArrayArguments_ReturnsNonZero()
    {
        using var output = new StringWriter();
        using var error = new StringWriter();

        var exitCode = await CliApplication.RunAsync(
            ["mcp", "call", "github_pr_list", "[]"], output, error,
            TestContext.Current.CancellationToken);

        exitCode.Should().Be(1);
        error.ToString().Should().Contain("must be a JSON object");
    }

    [Fact]
    public async Task RunAsync_McpCall_MissingArgumentsFile_ReturnsNonZero()
    {
        using var output = new StringWriter();
        using var error = new StringWriter();
        var path = Path.Combine(Path.GetTempPath(), $"githubie-missing-{Guid.NewGuid():N}.json");

        var exitCode = await CliApplication.RunAsync(
            ["mcp", "call", "github_push", "--file", path], output, error,
            TestContext.Current.CancellationToken);

        exitCode.Should().Be(1);
        error.ToString().Should().Contain("arguments file not found");
    }

    [Fact]
    public async Task RunAsync_ConfigCheck_MissingFile_ReturnsNonZero()
    {
        using var output = new StringWriter();
        using var error = new StringWriter();
        var missingPath = Path.Combine(Path.GetTempPath(), $"githubie-missing-{Guid.NewGuid():N}.json");

        var exitCode = await CliApplication.RunAsync(["--config", missingPath, "config", "check"], output, error, TestContext.Current.CancellationToken);

        exitCode.Should().Be(1);
        output.ToString().Should().Contain("config file not found");
    }

    [Fact]
    public async Task RunAsync_ConfigShow_ValidFile_PrintsRepositories()
    {
        var configPath = Path.Combine(Path.GetTempPath(), $"githubie-test-{Guid.NewGuid():N}.json");
        await File.WriteAllTextAsync(configPath, """
            {
              "mcp_port": 45460,
              "mcp_path": "/mcp",
              "repositories": {
                "sample": {
                  "github_owner": "example-org",
                  "github_repo": "example-repo",
                  "local_root": "C:/does-not-matter",
                  "remote": "origin",
                  "develop_branch": "develop",
                  "main_branch": "main",
                  "direct_push_branches": ["develop"],
                  "pull_branches": ["develop", "main"],
                  "protected_branches": ["main"],
                  "tag_target_branch": "main",
                  "tag_pattern": "^v[0-9]+\\.[0-9]+\\.[0-9]+.*$",
                  "merge_method": "merge",
                  "require_clean_working_tree": true
                }
              }
            }
            """, TestContext.Current.CancellationToken);

        try
        {
            using var output = new StringWriter();
            using var error = new StringWriter();

            var exitCode = await CliApplication.RunAsync(["--config", configPath, "config", "show"], output, error, TestContext.Current.CancellationToken);

            exitCode.Should().Be(0);
            output.ToString().Should().Contain("example-org/example-repo");
        }
        finally
        {
            File.Delete(configPath);
        }
    }
}
