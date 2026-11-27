using System.Text.Json;
using FluentAssertions;
using Xunit;

namespace Githubie.Cli.Tests;

public sealed class CodexMcpAuthStatusCommandTests
{
    [Fact]
    public void WriteStatus_UnknownAuth_ReturnsInconclusiveStatusWithoutConfigurationSecrets()
    {
        using var output = new StringWriter();
        var exitCode = CodexMcpAuthStatusCommand.WriteStatus(
            """[{"name":"other","enabled":true,"auth_status":"oauth"},{"name":"githubie","enabled":true,"auth_status":"unknown","transport":{"bearer_token":"secret-value","url":"private-url"}}]""",
            output);
        exitCode.Should().Be(0);
        using var result = JsonDocument.Parse(output.ToString());
        result.RootElement.GetProperty("auth_status").GetString().Should().Be("unknown");
        result.RootElement.GetProperty("connection_checked").GetBoolean().Should().BeFalse();
        output.ToString().Should().NotContain("secret-value").And.NotContain("private-url");
    }

    [Theory]
    [InlineData("[]", 1, "githubie_not_configured")]
    [InlineData("not-json", 2, "codex_response_invalid")]
    [InlineData("{}", 2, "codex_response_invalid")]
    [InlineData("""[{"name":"githubie","enabled":true}]""", 2, "codex_response_invalid")]
    [InlineData("""[{"name":"githubie","enabled":"true","auth_status":"unknown"}]""", 2, "codex_response_invalid")]
    public void WriteStatus_UnavailableOrMalformedStatus_ReturnsExplicitFailure(string json, int expectedExit, string code)
    {
        using var output = new StringWriter();
        CodexMcpAuthStatusCommand.WriteStatus(json, output).Should().Be(expectedExit);
        using var result = JsonDocument.Parse(output.ToString());
        result.RootElement.GetProperty("error").GetString().Should().Be(code);
    }

    [Fact]
    public void WriteStatus_DisabledServer_PreservesDisabledState()
    {
        using var output = new StringWriter();
        CodexMcpAuthStatusCommand.WriteStatus(
            """[{"name":"githubie","enabled":false,"auth_status":"unsupported"}]""", output).Should().Be(0);
        using var result = JsonDocument.Parse(output.ToString());
        result.RootElement.GetProperty("enabled").GetBoolean().Should().BeFalse();
    }
}
