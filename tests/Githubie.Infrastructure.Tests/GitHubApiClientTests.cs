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

    private sealed class CapturingStubHandler(Func<HttpRequestMessage, string?, HttpResponseMessage> respond) : HttpMessageHandler
    {
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var body = request.Content is null ? null : await request.Content.ReadAsStringAsync(cancellationToken);
            return respond(request, body);
        }
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

    private static GitHubApiClient CreateCapturingClient(Func<HttpRequestMessage, string?, HttpResponseMessage> respond)
    {
        var httpClient = new HttpClient(new CapturingStubHandler(respond)) { BaseAddress = new Uri("https://api.github.com/") };
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

    [Fact]
    public async Task MergePullRequestAsync_OmitsNullMergeMethodAndCommitMessageFromRequestBody()
    {
        // 実データ検証で発見した回帰防止: merge_strategy未指定時にJSON bodyへ
        // "merge_method": null を明示送信すると、GitHubのmerge APIが422を返して失敗していた。
        string? capturedBody = null;
        var client = CreateCapturingClient((request, body) =>
        {
            if (request.Method == HttpMethod.Put)
            {
                capturedBody = body;
            }

            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(
                    """{"number":1,"title":"t","state":"closed","merged":true,"head":{"ref":"develop"},"base":{"ref":"main"},"user":{"login":"u"},"html_url":"https://example.com","created_at":"2026-01-01T00:00:00Z","updated_at":"2026-01-01T00:00:00Z"}""")
            };
        });

        await client.MergePullRequestAsync("repo-id", "owner", "repo", new GitHubPullRequestMerge(1, null, null), TestContext.Current.CancellationToken);

        capturedBody.Should().NotBeNull();
        capturedBody.Should().NotContain("merge_method");
        capturedBody.Should().NotContain("commit_message");
    }

    [Fact]
    public async Task MergePullRequestAsync_MapsMethodNotAllowedToPullRequestNotMergeable()
    {
        var client = CreateClient(_ => new HttpResponseMessage(HttpStatusCode.MethodNotAllowed));

        var result = await client.MergePullRequestAsync("repo-id", "owner", "repo", new GitHubPullRequestMerge(1, null, null), TestContext.Current.CancellationToken);

        result.IsSuccess.Should().BeFalse();
        result.Error.Should().Be(GitHubError.PullRequestNotMergeable);
    }
}
