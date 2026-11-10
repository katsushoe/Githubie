using FluentAssertions;
using Githubie.Application.Configuration;
using Xunit;

namespace Githubie.Cli.Tests;

public sealed class CanonicalConfigurationTests
{
    [Fact]
    public void OmittedConfiguration_UsesExecutingInstallation() =>
        GithubieConfigurationPath.Resolve(null).Should().Be(Path.GetFullPath(
            Path.Combine(AppContext.BaseDirectory, "..", "config", "githubie.json")));

    [Theory]
    [InlineData(@"D:\Tools\Githubie\bin", @"D:\Tools\Githubie\config\githubie.json")]
    [InlineData(@"E:\Apps With Spaces\Githubie\bin\", @"E:\Apps With Spaces\Githubie\config\githubie.json")]
    [InlineData(@"C:\Program Files\Githubie\bin", @"C:\Program Files\Githubie\config\githubie.json")]
    public void CustomInstallation_UsesOnlyItsOwnConfiguration(string binDirectory, string expected)
    {
        GithubieConfigurationPath.Resolve(null, binDirectory).Should().Be(expected);
        GithubieConfigurationPath.Resolve(expected, binDirectory).Should().Be(expected);
        GithubieConfigurationPath.Resolve(expected.ToLowerInvariant(), binDirectory).Should().Be(expected);
        var alternate = () => GithubieConfigurationPath.Resolve(@"C:\Githubie\config\githubie.json", binDirectory);
        alternate.Should().Throw<ArgumentException>();
    }

    [Theory]
    [InlineData("githubie.json")]
    [InlineData(@"C:\Users\example\AppData\Local\Akatsukisoft\Githubie\config\githubie.json")]
    [InlineData(@"D:\Githubie\config\githubie.json")]
    public async Task AlternateConfiguration_IsRejectedBeforeReadingOrConnecting(string path)
    {
        using var output = new StringWriter();
        using var error = new StringWriter();
        var result = await CliApplication.RunAsync(["--config", path, "config", "check"], output, error,
            TestContext.Current.CancellationToken);
        result.Should().Be(1);
        output.ToString().Should().BeEmpty();
        error.ToString().Should().Contain(GithubieConfigurationPath.CanonicalPath);
    }
}
