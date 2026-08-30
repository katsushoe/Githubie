using FluentAssertions;
using Githubie.Application.Configuration;
using Githubie.Application.Git;
using Githubie.Application.GitHub;
using Githubie.Application.Repositories;
using NSubstitute;
using Xunit;

namespace Githubie.Server.Tests;

public sealed class GithubieMcpProjectDiscoveryTests
{
    [Fact]
    public void ListProjects_ReturnsRegisteredIdsInStableOrder()
    {
        var tools = CreateTools(CreateAllowlist("zulu", "Alpha"), out _);

        var result = tools.ListProjects();

        result.Ok.Should().BeTrue();
        result.Operation.Should().Be("list_projects");
        result.Data.Should().Equal("Alpha", "zulu");
    }

    [Fact]
    public async Task PushAsync_UnregisteredProject_ReturnsRegisteredCandidates()
    {
        var tools = CreateTools(CreateAllowlist("zulu", "Alpha"), out var gitGateway);
        gitGateway.PushAsync("missing", Arg.Any<CancellationToken>())
            .Returns(GitGatewayResult<Unit>.Failure(GitGatewayError.RepositoryNotFound));

        var result = await tools.PushAsync("missing", TestContext.Current.CancellationToken);

        result.Error!.Code.Should().Be("repository_not_found");
        result.Error.Candidates.Should().Equal("Alpha", "zulu");
    }

    private static GithubieMcpTools CreateTools(RepositoryAllowlist allowlist, out IGitGateway gitGateway)
    {
        gitGateway = Substitute.For<IGitGateway>();
        return new GithubieMcpTools(
            gitGateway,
            Substitute.For<IGitHubRepositoryGateway>(),
            Substitute.For<IRepositoryRegistrationService>(),
            Substitute.For<IRepositoryManagementService>(),
            allowlist);
    }

    private static RepositoryAllowlist CreateAllowlist(params string[] repositoryIds) =>
        new(repositoryIds.ToDictionary(
            id => id,
            _ => new RepositoryOptions(
                "owner", "repo", "C:\\repo", "origin", "develop", "main",
                ["develop"], ["develop"], ["main"], "main", "^v", "merge", true),
            StringComparer.OrdinalIgnoreCase));
}
