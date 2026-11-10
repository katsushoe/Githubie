using System.Text.Json;
using FluentAssertions;
using Xunit;

namespace Githubie.Cli.Tests;

public sealed class PrMergeCliContractTests
{
    [Fact]
    public void GenericMcpCall_PreservesMergeArgumentsWithoutSelectingAnotherStrategy()
    {
        var sha = new string('a', 40);
        var arguments = JsonSerializer.Serialize(new { repository = "sample", pull_request_number = 1,
            merge_strategy = (string?)null, message = "  approved body  ", expected_source_commit_sha = sha });

        var (method, parameters) = UserHostCli.ParseMcpCommand(["mcp", "call", "github_pr_merge", arguments]);

        method.Should().Be("tools/call");
        var json = JsonSerializer.SerializeToElement(parameters);
        json.GetProperty("name").GetString().Should().Be("github_pr_merge");
        var forwarded = json.GetProperty("arguments");
        forwarded.GetProperty("expected_source_commit_sha").GetString().Should().Be(sha);
        forwarded.GetProperty("merge_strategy").ValueKind.Should().Be(JsonValueKind.Null);
        forwarded.GetProperty("message").GetString().Should().Be("  approved body  ");
    }

    [Fact]
    public void UnknownMergeResult_IsFailureInCli()
    {
        UserHostCli.IsSuccess("""{"result":{"structuredContent":{"ok":false,"error":{"code":"pr_merge_result_unknown","outcome":"unknown","retryable":false}}}}""")
            .Should().BeFalse();
    }
}
