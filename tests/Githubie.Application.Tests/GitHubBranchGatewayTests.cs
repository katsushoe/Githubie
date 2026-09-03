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

    [Theory]
    [InlineData("main", Sha)]
    [InlineData("release/base", "2222222222222222222222222222222222222222")]
    [InlineData("develop", "3333333333333333333333333333333333333333")]
    public async Task CreateBranchAsync_CreatesAllowedBranchFromExplicitSource(string source, string sha)
    {
        _api.GetBranchAsync(Repository, "owner", "repo", source, Arg.Any<CancellationToken>())
            .Returns(GitHubResult<GitHubBranchInfo>.Success(new(source, sha, true)));
        _api.CreateBranchAsync(Repository, "owner", "repo", "develop", sha, Arg.Any<CancellationToken>())
            .Returns(GitHubResult<GitHubBranchInfo>.Success(new("develop", sha, false)));

        var result = await _gateway.CreateBranchAsync(Repository, "develop", source, TestContext.Current.CancellationToken);

        result.IsSuccess.Should().BeTrue();
        result.Value!.Name.Should().Be("develop");
        result.Value.HeadSha.Should().Be(sha);
        await _api.Received(1).GetBranchAsync(Repository, "owner", "repo", source, Arg.Any<CancellationToken>());
        _api.ReceivedCalls().Should().HaveCount(2);
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

        var result = await _gateway.CreateBranchAsync(Repository, "develop", "main", TestContext.Current.CancellationToken);

        result.Error.Should().Be(error);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("  ")]
    public async Task CreateBranchAsync_MissingSource_DoesNotCallApi(string? source)
    {
        var result = await _gateway.CreateBranchAsync(Repository, "develop", source!, TestContext.Current.CancellationToken);

        result.Error.Should().Be(GitHubError.BranchSourceInvalid);
        _api.ReceivedCalls().Should().BeEmpty();
    }

    [Theory]
    [InlineData(GitHubError.BranchNotFound)]
    [InlineData(GitHubError.AuthenticationFailed)]
    [InlineData(GitHubError.PermissionDenied)]
    public async Task CreateBranchAsync_SourceFailure_DoesNotCreateRef(GitHubError error)
    {
        _api.GetBranchAsync(Repository, "owner", "repo", "missing", Arg.Any<CancellationToken>())
            .Returns(GitHubResult<GitHubBranchInfo>.Failure(error));

        var result = await _gateway.CreateBranchAsync(Repository, "develop", "missing", TestContext.Current.CancellationToken);

        result.Error.Should().Be(error);
        _api.ReceivedCalls().Should().ContainSingle();
    }

    [Fact]
    public async Task CreateBranchAsync_ExplicitCommit_DoesNotResolveDefaultBranch()
    {
        _api.GetCommitShaAsync(Repository, "owner", "repo", Sha, Arg.Any<CancellationToken>())
            .Returns(GitHubResult<string>.Success(Sha));
        _api.CreateBranchAsync(Repository, "owner", "repo", "develop", Sha, Arg.Any<CancellationToken>())
            .Returns(GitHubResult<GitHubBranchInfo>.Success(new("develop", Sha, false)));

        var result = await _gateway.CreateBranchAsync(Repository, "develop", Sha, TestContext.Current.CancellationToken);

        result.Value!.HeadSha.Should().Be(Sha);
        await _api.DidNotReceive().GetBranchAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task CreateBranchAsync_MissingCommit_DoesNotCreateRef()
    {
        _api.GetCommitShaAsync(Repository, "owner", "repo", Sha, Arg.Any<CancellationToken>())
            .Returns(GitHubResult<string>.Failure(GitHubError.BranchSourceNotFound));

        var result = await _gateway.CreateBranchAsync(Repository, "develop", Sha, TestContext.Current.CancellationToken);

        result.Error.Should().Be(GitHubError.BranchSourceNotFound);
        _api.ReceivedCalls().Should().ContainSingle();
    }

    [Fact]
    public async Task CreateBranchAsync_DisallowedDestination_DoesNotResolveSource()
    {
        var result = await _gateway.CreateBranchAsync(Repository, "not-allowed", "main", TestContext.Current.CancellationToken);

        result.Error.Should().Be(GitHubError.BranchNotAllowed);
        _api.ReceivedCalls().Should().BeEmpty();
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
