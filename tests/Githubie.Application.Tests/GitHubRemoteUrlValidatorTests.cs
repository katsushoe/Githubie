using FluentAssertions;
using Githubie.Application.Repositories;
using Xunit;

namespace Githubie.Application.Tests;

public sealed class GitHubRemoteUrlValidatorTests
{
    [Theory]
    [InlineData("https://github.com/example-org/example-repo.git", "example-org", "example-repo", true)]
    [InlineData("https://github.com/example-org/example-repo", "example-org", "example-repo", true)]
    [InlineData("git@github.com:example-org/example-repo.git", "example-org", "example-repo", true)]
    [InlineData("https://github.com/other-org/example-repo.git", "example-org", "example-repo", false)]
    [InlineData("https://gitlab.com/example-org/example-repo.git", "example-org", "example-repo", false)]
    [InlineData("https://github.com/example-org/other-repo.git", "example-org", "example-repo", false)]
    public void IsExpectedRemote_MatchesOnlyConfiguredOwnerAndRepo(string remoteUrl, string owner, string repo, bool expected)
    {
        GitHubRemoteUrlValidator.IsExpectedRemote(remoteUrl, owner, repo).Should().Be(expected);
    }

    [Fact]
    public void TryParse_ReturnsNullForUnrecognizedFormat()
    {
        GitHubRemoteUrlValidator.TryParse("not-a-url").Should().BeNull();
    }
}
