using FluentAssertions;
using Githubie.Cli;
using Xunit;

namespace Githubie.Cli.Tests;

public sealed class CliApplicationTests
{
    [Theory]
    [InlineData("initialize", 5)]
    [InlineData("tools/list", 5)]
    public void ResolveMcpTimeout_QueryMethod_ReturnsFiveSeconds(string method, int expectedSeconds)
    {
        var timeout = CliApplication.ResolveMcpTimeout(method);

        timeout.Should().Be(TimeSpan.FromSeconds(expectedSeconds));
    }

    [Fact]
    public void ResolveMcpTimeout_ToolCall_HasNoTimeLimit()
    {
        var timeout = CliApplication.ResolveMcpTimeout("tools/call");

        timeout.Should().Be(Timeout.InfiniteTimeSpan);
    }

    [Fact]
    public async Task RunAsync_Help_DescribesConsoleAuthOption()
    {
        using var output = new StringWriter();
        using var error = new StringWriter();

        var exitCode = await CliApplication.RunAsync(["help"], output, error, TestContext.Current.CancellationToken);

        exitCode.Should().Be(0);
        output.ToString().Should().Contain("auth set <repository> [--console]");
        output.ToString().Should().Contain("GUI by default; only --console uses masked terminal input.");
        output.ToString().Should().Contain("The GUI waits until the token is entered or cancelled.");
        output.ToString().Should().Contain("CANCELLED, DIALOG_FAILED, SAVE_FAILED");
        output.ToString().Should().NotContain("timeout");
        output.ToString().Should().Contain("source (required; no default)");
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
              "provider_authentication": {
                "issuer": "moyai:test",
                "trust_bundle_path": "C:\\Githubie\\config\\moyai-trust.json",
                "replay_database_path": "C:\\Githubie\\data\\moyai-replay.db",
                "projects": { "sample": "11111111-1111-1111-1111-111111111111" }
              },
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

    [Theory]
    [InlineData(false, 0)]
    [InlineData(true, 1)]
    public async Task RunAsync_ConfigCheck_RequiresMoyaiSettingsOnlyWithMoyaiOption(bool moyai, int expectedExit)
    {
        var configPath = Path.Combine(Path.GetTempPath(), $"githubie-standalone-{Guid.NewGuid():N}.json");
        await File.WriteAllTextAsync(configPath, """
            { "mcp_port": 45460, "mcp_path": "/mcp", "repositories": {} }
            """, TestContext.Current.CancellationToken);
        try
        {
            using var output = new StringWriter();
            using var error = new StringWriter();
            string[] args = moyai
                ? ["--config", configPath, "config", "check", "--moyai"]
                : ["--config", configPath, "config", "check"];

            var exitCode = await CliApplication.RunAsync(args, output, error, TestContext.Current.CancellationToken);

            exitCode.Should().Be(expectedExit);
            if (moyai) output.ToString().Should().Contain("[NG] $.provider_authentication");
            else output.ToString().Should().Contain("[OK] config check passed");
        }
        finally
        {
            File.Delete(configPath);
        }
    }
}
