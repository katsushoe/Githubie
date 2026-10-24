using System.Text.Json;
using FluentAssertions;
using Xunit;

namespace Githubie.Cli.Tests;

public sealed class UserHostCliTests
{
    [Fact]
    public async Task ConsoleTokenInput_IsRejectedWithoutOpeningProfileOrSavingAnything()
    {
        using var output = new StringWriter();
        using var error = new StringWriter();
        var result = await CliApplication.RunAsync(["auth", "set", "sample", "--console"], output, error, TestContext.Current.CancellationToken);
        result.Should().Be(1);
        error.ToString().Should().Contain("CONSOLE_INPUT_UNSUPPORTED").And.Contain("token UI");
        output.ToString().Should().BeEmpty();
    }

    [Fact]
    public void Rename_UsesMcpOldAndNewRepositoryParameterNames()
    {
        var (method, parameters) = UserHostCli.ParseMcpCommand(["repo", "rename", "old", "new"]);
        method.Should().Be("tools/call");
        var json = JsonSerializer.SerializeToElement(parameters);
        json.GetProperty("arguments").GetProperty("old_repository").GetString().Should().Be("old");
        json.GetProperty("arguments").GetProperty("new_repository").GetString().Should().Be("new");
    }

    [Theory]
    [InlineData("[]")]
    [InlineData("null")]
    [InlineData("12")]
    public void Call_RejectsNonObjectArgumentsBeforeConnecting(string arguments)
    {
        var parse = () => UserHostCli.ParseMcpCommand(["mcp", "call", "tool", arguments]);
        parse.Should().Throw<ArgumentException>();
    }

    [Theory]
    [InlineData("{\"error\":{\"code\":-1}}", false)]
    [InlineData("{\"result\":{\"isError\":true}}", false)]
    [InlineData("{\"result\":{\"structuredContent\":{\"ok\":false}}}", false)]
    [InlineData("{\"result\":{\"structuredContent\":{\"ok\":true}}}", true)]
    public void ToolFailure_IsNotReportedAsSuccess(string json, bool expected) => UserHostCli.IsSuccess(json).Should().Be(expected);
}
