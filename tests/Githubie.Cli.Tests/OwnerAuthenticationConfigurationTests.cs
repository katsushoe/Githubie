using FluentAssertions;
using Xunit;

namespace Githubie.Cli.Tests;

public sealed class OwnerAuthenticationConfigurationTests
{
    [Theory]
    [InlineData("windows_owner", 0)]
    [InlineData("legacy", 1)]
    public async Task ConfigCheck_OwnerModeDoesNotRequireIssuerEvenWhenLegacyMoyaiFlagIsStored(string mode, int expected)
    {
        var path = Path.GetTempFileName();
        try
        {
            await File.WriteAllTextAsync(path, "{\"mcp_port\":45460,\"mcp_path\":\"/mcp\",\"repositories\":{},\"user_host\":{\"moyai_integration\":true,\"authentication_mode\":\"" + mode + "\"}}", TestContext.Current.CancellationToken);
            using var output = new StringWriter();
            using var error = new StringWriter();
            var result = await CliApplication.RunIsolatedAsync(["--config", path, "config", "check", "--moyai"], output, error,
                TestContext.Current.CancellationToken);
            result.Should().Be(expected);
            error.ToString().Should().BeEmpty();
        }
        finally { File.Delete(path); }
    }
}
