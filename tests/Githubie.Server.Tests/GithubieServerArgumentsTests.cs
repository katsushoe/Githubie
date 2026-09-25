using FluentAssertions;
using Xunit;

namespace Githubie.Server.Tests;

public sealed class GithubieServerArgumentsTests
{
    [Fact]
    public void Parse_NoOption_IsStandaloneMode()
    {
        var parsed = GithubieServerArguments.Parse(["C:\\Githubie\\config\\githubie.json"]);

        parsed.ConfigPath.Should().Be("C:\\Githubie\\config\\githubie.json");
        parsed.MoyaiIntegration.Should().BeFalse();
        parsed.HostArguments.Should().BeEmpty();
    }

    [Theory]
    [InlineData("--moyai", "C:\\Githubie\\config\\githubie.json")]
    [InlineData("C:\\Githubie\\config\\githubie.json", "--moyai")]
    public void Parse_MoyaiOption_EnablesIntegrationAndIsNotPassedToHost(string first, string second)
    {
        var parsed = GithubieServerArguments.Parse([first, second]);

        parsed.ConfigPath.Should().Be("C:\\Githubie\\config\\githubie.json");
        parsed.MoyaiIntegration.Should().BeTrue();
        parsed.HostArguments.Should().BeEmpty();
    }

    [Fact]
    public void Parse_NoArguments_UsesDefaultConfigAndStandaloneMode()
    {
        var parsed = GithubieServerArguments.Parse([]);

        parsed.ConfigPath.Should().BeNull();
        parsed.MoyaiIntegration.Should().BeFalse();
    }
}
