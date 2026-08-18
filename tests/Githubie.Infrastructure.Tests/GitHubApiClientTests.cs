using System.Net;
using FluentAssertions;
using Githubie.Application.Credentials;
using Githubie.Application.GitHub;
using Githubie.Infrastructure.GitHub;
using Xunit;

namespace Githubie.Infrastructure.Tests;

public sealed class GitHubApiClientTests
{
    private sealed class StubHandler(Func<HttpRequestMessage, HttpResponseMessage> respond) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(respond(request));
    }

    private sealed class FakeTokenStore : IApiTokenStore
    {
        public ApiTokenStoreResult Save(string repositoryId, ReadOnlySpan<char> token) => ApiTokenStoreResult.Success();

        public ApiTokenStoreReadResult Read(string repositoryId) => ApiTokenStoreReadResult.Success("dummy-token".ToCharArray());

        public ApiTokenStoreResult Delete(string repositoryId) => ApiTokenStoreResult.Success();
    }

    private static GitHubApiClient CreateClient(Func<HttpRequestMessage, HttpResponseMessage> respond)
    {
        var httpClient = new HttpClient(new StubHandler(respond)) { BaseAddress = new Uri("https://api.github.com/") };
        return new GitHubApiClient(httpClient, new FakeTokenStore());
    }

    [Fact]
    public async Task GetBranchAsync_MapsNotFoundToBranchNotFound_NotRepositoryNotFound()
    {
        // 実データ検証で発見した回帰防止: 存在しないbranchの404が誤ってrepository_not_foundへ
        // マップされていた(既定のnotFoundErrorがRepositoryNotFoundのままだったため)。
        var client = CreateClient(_ => new HttpResponseMessage(HttpStatusCode.NotFound));

        var result = await client.GetBranchAsync("repo-id", "owner", "repo", "missing-branch", TestContext.Current.CancellationToken);

        result.IsSuccess.Should().BeFalse();
        result.Error.Should().Be(GitHubError.BranchNotFound);
    }

    [Fact]
    public async Task GetPullRequestAsync_MapsNotFoundToPullRequestNotFound()
    {
        var client = CreateClient(_ => new HttpResponseMessage(HttpStatusCode.NotFound));

        var result = await client.GetPullRequestAsync("repo-id", "owner", "repo", 9999, TestContext.Current.CancellationToken);

        result.IsSuccess.Should().BeFalse();
        result.Error.Should().Be(GitHubError.PullRequestNotFound);
    }

    [Fact]
    public async Task GetBranchAsync_MapsUnauthorizedToAuthenticationFailed()
    {
        var client = CreateClient(_ => new HttpResponseMessage(HttpStatusCode.Unauthorized));

        var result = await client.GetBranchAsync("repo-id", "owner", "repo", "main", TestContext.Current.CancellationToken);

        result.IsSuccess.Should().BeFalse();
        result.Error.Should().Be(GitHubError.AuthenticationFailed);
    }

    [Fact]
    public async Task GetBranchAsync_MapsPrimaryRateLimitCorrectly()
    {
        var client = CreateClient(_ =>
        {
            var response = new HttpResponseMessage(HttpStatusCode.Forbidden);
            response.Headers.Add("x-ratelimit-remaining", "0");
            return response;
        });

        var result = await client.GetBranchAsync("repo-id", "owner", "repo", "main", TestContext.Current.CancellationToken);

        result.IsSuccess.Should().BeFalse();
        result.Error.Should().Be(GitHubError.RateLimited);
    }
}
