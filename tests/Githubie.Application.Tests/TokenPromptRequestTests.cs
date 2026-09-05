using FluentAssertions;
using Githubie.Application.Interactive;
using Xunit;

namespace Githubie.Application.Tests;

public sealed class TokenPromptRequestTests
{
    [Fact]
    public void Constructor_WithDisplayContext_PreservesBothValues()
    {
        var request = new TokenPromptRequest("Moyai", "https://github.com/katsushoe/Githubie");

        request.ProjectName.Should().Be("Moyai");
        request.RepositoryUrl.Should().Be("https://github.com/katsushoe/Githubie");
    }

    [Theory]
    [InlineData("", "https://github.com/katsushoe/Githubie")]
    [InlineData("Moyai", "")]
    [InlineData(" ", "https://github.com/katsushoe/Githubie")]
    [InlineData("Moyai", " ")]
    public void Constructor_WithoutDisplayContext_RejectsRequest(string projectName, string repositoryUrl)
    {
        var act = () => new TokenPromptRequest(projectName, repositoryUrl);

        act.Should().Throw<ArgumentException>();
    }
}
