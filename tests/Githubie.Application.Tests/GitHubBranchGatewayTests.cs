using FluentAssertions;
using Githubie.Application.Configuration;
using Githubie.Application.GitHub;
using Githubie.Application.Repositories;
using NSubstitute;
using Xunit;

namespace Githubie.Application.Tests;

public sealed class GitHubBranchGatewayTests
{
    private const string Repository = "sample";
    private const string Sha = "1111111111111111111111111111111111111111";
    private readonly IGitHubApiClient _api = Substitute.For<IGitHubApiClient>();
    private readonly GitHubRepositoryGateway _gateway;

    public GitHubBranchGatewayTests()
    {
        var options = new RepositoryOptions(
            "owner", "repo", "C:\\repo", "origin", "develop", "main", ["develop"], ["develop", "main"], ["main"],
            "main", "^v", "merge", true);
        _gateway = new(new RepositoryAllowlist(new Dictionary<string, RepositoryOptions> { [Repository] = options }), _api);
    }

    [Fact]
    public async Task CreateBranchAsync_CreatesAllowedBranchFromMainHead()
    {
        _api.GetBranchAsync(Repository, "owner", "repo", "main", Arg.Any<CancellationToken>())
            .Returns(GitHubResult<GitHubBranchInfo>.Success(new("main", Sha, true)));
        _api.CreateBranchAsync(Repository, "owner", "repo", "develop", Sha, Arg.Any<CancellationToken>())
            .Returns(GitHubResult<GitHubBranchInfo>.Success(new("develop", Sha, false)));

        var result = await _gateway.CreateBranchAsync(Repository, "develop", TestContext.Current.CancellationToken);

        result.IsSuccess.Should().BeTrue();
        result.Value!.Name.Should().Be("develop");
    }

    [Theory]
    [InlineData(GitHubError.BranchAlreadyExists)]
    [InlineData(GitHubError.AuthenticationFailed)]
    [InlineData(GitHubError.PermissionDenied)]
    public async Task CreateBranchAsync_PropagatesProviderErrors(GitHubError error)
    {
        _api.GetBranchAsync(Repository, "owner", "repo", "main", Arg.Any<CancellationToken>())
            .Returns(GitHubResult<GitHubBranchInfo>.Success(new("main", Sha, true)));
        _api.CreateBranchAsync(Repository, "owner", "repo", "develop", Sha, Arg.Any<CancellationToken>())
            .Returns(GitHubResult<GitHubBranchInfo>.Failure(error));

        var result = await _gateway.CreateBranchAsync(Repository, "develop", TestContext.Current.CancellationToken);

        result.Error.Should().Be(error);
    }

    [Fact]
    public async Task DeleteBranchAsync_PropagatesNotFound()
    {
        _api.DeleteBranchAsync(Repository, "owner", "repo", "develop", Arg.Any<CancellationToken>())
            .Returns(GitHubResult<bool>.Failure(GitHubError.BranchNotFound));

        var result = await _gateway.DeleteBranchAsync(Repository, "develop", TestContext.Current.CancellationToken);

        result.Error.Should().Be(GitHubError.BranchNotFound);
    }

    [Fact]
    public async Task DeleteBranchAsync_RejectsProtectedBranchWithoutCallingApi()
    {
        var result = await _gateway.DeleteBranchAsync(Repository, "main", TestContext.Current.CancellationToken);

        result.Error.Should().Be(GitHubError.ProtectedBranch);
        await _api.DidNotReceive().DeleteBranchAsync(
            Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
    }
}
