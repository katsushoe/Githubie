using System.Text;
using FluentAssertions;
using Githubie.Infrastructure.Configuration;
using Xunit;

namespace Githubie.Infrastructure.Tests;

public sealed class OwnerAuthenticationConfigurationTests
{
    [Theory]
    [InlineData("legacy", true)]
    [InlineData("windows_owner", true)]
    [InlineData("Windows_Owner", false)]
    [InlineData("", false)]
    [InlineData("assertion", false)]
    public async Task Loader_RejectsUnknownAuthenticationModes(string mode, bool expected)
    {
        var json = "{\"mcp_port\":45460,\"mcp_path\":\"/mcp\",\"repositories\":{},\"user_host\":{\"authentication_mode\":" + System.Text.Json.JsonSerializer.Serialize(mode) + "}}";
        using var input = new MemoryStream(Encoding.UTF8.GetBytes(json));
        var result = await new JsonGithubieOptionsLoader().LoadAsync(input, TestContext.Current.CancellationToken);
        result.IsSuccess.Should().Be(expected);
        if (expected) result.Options!.UserHost.AuthenticationMode.Should().Be(mode);
    }
}
