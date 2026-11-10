using System.Text;
using FluentAssertions;
using Githubie.Infrastructure.Configuration;
using Xunit;

namespace Githubie.Infrastructure.Tests;

public sealed class UserHostSettingsStoreTests
{
    [Theory]
    [InlineData("{}", false, false, true)]
    [InlineData("{\"user_host\":{\"moyai_integration\":true,\"direct_unrestricted\":true,\"autostart_enabled\":false}}", true, true, false)]
    public void ReadsModesAndAutostartFromTheSameDocument(string json, bool moyai, bool direct, bool startup)
    {
        var path = Path.GetTempFileName();
        try
        {
            File.WriteAllText(path, json);
            var settings = new UserHostSettingsStore(path).Read();
            settings.MoyaiIntegration.Should().Be(moyai);
            settings.DirectUnrestricted.Should().Be(direct);
            settings.AutostartEnabled.Should().Be(startup);
        }
        finally { File.Delete(path); }
    }

    [Theory]
    [InlineData("{\"user_host\":null}")]
    [InlineData("{\"user_host\":{\"direct_unrestricted\":true}}")]
    [InlineData("{\"user_host\":{\"autostart_enabled\":\"false\"}}")]
    public void InvalidHostSettingsCannotBeUsed(string json)
    {
        var path = Path.GetTempFileName();
        try
        {
            File.WriteAllText(path, json);
            var read = () => new UserHostSettingsStore(path).Read();
            read.Should().Throw<Exception>();
            File.ReadAllText(path).Should().Be(json);
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public async Task MainLoaderParsesAndRoundTripsCombinedSettings()
    {
        var loader = new JsonGithubieOptionsLoader();
        using var input = new MemoryStream(Encoding.UTF8.GetBytes("{\"mcp_port\":45460,\"mcp_path\":\"/mcp\",\"repositories\":{},\"user_host\":{\"moyai_integration\":true,\"direct_unrestricted\":true,\"autostart_enabled\":false}}"));
        var result = await loader.LoadAsync(input, TestContext.Current.CancellationToken);
        result.IsSuccess.Should().BeTrue();
        using var output = new MemoryStream();
        await loader.SaveAsync(result.Options!, output, TestContext.Current.CancellationToken);
        output.Position = 0;
        var reloaded = await loader.LoadAsync(output, TestContext.Current.CancellationToken);
        reloaded.Options!.UserHost.Should().Be(result.Options!.UserHost);
    }
}
