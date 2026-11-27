using System.Text.Json;
using FluentAssertions;
using Xunit;

namespace Githubie.Cli.Tests;

public sealed class BranchProtectionCliContractTests
{
    [Fact]
    public void GenericMcpCall_PreservesBranchProtectionArguments()
    {
        const string arguments = """{"repository":"sample","branch":"main","edit":{"enforce_admins":true},"create_if_missing":false}""";
        var (method, parameters) = UserHostCli.ParseMcpCommand(["mcp", "call", "github_branch_protection_update", arguments]);
        method.Should().Be("tools/call");
        var json = JsonSerializer.SerializeToElement(parameters);
        json.GetProperty("name").GetString().Should().Be("github_branch_protection_update");
        json.GetProperty("arguments").GetProperty("edit").GetProperty("enforce_admins").GetBoolean().Should().BeTrue();
        json.GetProperty("arguments").GetProperty("create_if_missing").GetBoolean().Should().BeFalse();
    }

    [Fact]
    public void GenericMcpCall_UnknownResultIsFailure() =>
        UserHostCli.IsSuccess("""{"result":{"structuredContent":{"ok":false,"error":{"code":"branch_protection_result_unknown","outcome":"unknown","retryable":false}}}}""").Should().BeFalse();
}
