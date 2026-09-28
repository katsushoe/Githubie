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

    [Fact]
    public void Parse_DirectUnrestrictedWithMoyai_EnablesUnrestrictedDirectConnection()
    {
        var parsed = GithubieServerArguments.Parse(["C:\\Githubie\\config\\githubie.json", "--moyai", "--direct-unrestricted"]);

        parsed.MoyaiIntegration.Should().BeTrue();
        parsed.DirectUnrestricted.Should().BeTrue();
        parsed.HostArguments.Should().BeEmpty();
    }

    [Fact]
    public void Parse_DirectUnrestrictedWithoutMoyai_Rejects()
    {
        var parse = () => GithubieServerArguments.Parse(["--direct-unrestricted"]);

        parse.Should().Throw<ArgumentException>();
    }

    [Theory]
    [InlineData(false, false, "standalone", "unrestricted")]
    [InlineData(true, false, "moyai", "read_only")]
    [InlineData(true, true, "moyai", "unrestricted")]
    public void IntegrationMode_ReportsContractValues(
        bool moyai, bool directUnrestricted, string integrationMode, string directConnection)
    {
        var options = new Githubie.Application.Configuration.GithubieOptions(
            45460, "/mcp", new Dictionary<string, Githubie.Application.Configuration.RepositoryOptions>())
        {
            ProviderAuthentication = moyai
                ? new("moyai:test", "C:\\trust.json", "C:\\replay.db", new Dictionary<string, Guid>())
                : null,
            DirectUnrestricted = directUnrestricted,
        };

        GithubieIntegrationMode.IntegrationMode(options).Should().Be(integrationMode);
        GithubieIntegrationMode.DirectConnection(options).Should().Be(directConnection);
    }
}
