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

        public ApiTokenStoreResult Rename(string oldRepositoryId, string newRepositoryId) => ApiTokenStoreResult.Success();
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
    public async Task CreatePullRequestAsync_UsesGitHubSnakeCaseRequestPropertyNames()
    {
        string? capturedBody = null;
        var client = CreateCapturingClient((request, body) =>
        {
            capturedBody = body;
            return new HttpResponseMessage(HttpStatusCode.Created)
            {
                Content = new StringContent(
                    """{"number":1,"title":"Release","state":"open","merged":false,"head":{"ref":"develop"},"base":{"ref":"main"},"user":{"login":"u"},"html_url":"https://example.com","created_at":"2026-01-01T00:00:00Z","updated_at":"2026-01-01T00:00:00Z"}""")
            };
        });

        await client.CreatePullRequestAsync(
            "repo-id",
            "owner",
            "repo",
            "develop",
            "main",
            new GitHubPullRequestCreate("Release", "Description", false),
            TestContext.Current.CancellationToken);

        capturedBody.Should().NotBeNull();
        capturedBody.Should().Contain("\"title\"");
        capturedBody.Should().Contain("\"body\"");
        capturedBody.Should().Contain("\"head\"");
        capturedBody.Should().Contain("\"base\"");
        capturedBody.Should().Contain("\"draft\"");
        capturedBody.Should().NotContain("\"Title\"");
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

    [Fact]
    public async Task GetTagAsync_MapsNotFoundToTagNotFound_NotTagInvalid()
    {
        // branch_not_foundと同種の回帰防止: 存在しないtagの404が誤ってtag_invalid
        // (パターン不一致の意味)へマップされていた。
        var client = CreateClient(_ => new HttpResponseMessage(HttpStatusCode.NotFound));

        var result = await client.GetTagAsync("repo-id", "owner", "repo", "missing-tag", TestContext.Current.CancellationToken);

        result.IsSuccess.Should().BeFalse();
        result.Error.Should().Be(GitHubError.TagNotFound);
    }

    [Fact]
    public async Task CreateReleaseAsync_CreatesDraftUploadsAssetsThenPublishes()
    {
        var root = Path.Combine(Path.GetTempPath(), $"githubie-release-{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);
        var asset = Path.Combine(root, "Githubie-1.2.0.0-win-x64.zip");
        await File.WriteAllTextAsync(asset, "payload", TestContext.Current.CancellationToken);
        var requests = new List<(HttpMethod Method, string Url)>();
        try
        {
            var client = CreateCapturingClient((request, _) =>
            {
                requests.Add((request.Method, request.RequestUri!.ToString()));
                if (request.Method == HttpMethod.Post && request.RequestUri.Host == "uploads.github.com")
                    return Json(HttpStatusCode.Created, """{"name":"Githubie-1.2.0.0-win-x64.zip","size":7,"browser_download_url":"https://github.com/o/r/releases/download/v1.2.0.0/a.zip"}""");
                if (request.Method == HttpMethod.Patch)
                    return Json(HttpStatusCode.OK, ReleaseJson(draft: false));
                return Json(HttpStatusCode.Created, ReleaseJson(draft: true));
            });

            var result = await client.CreateReleaseAsync(
                "repo-id", "owner", "repo", root,
                new GitHubReleaseCreate("v1.2.0.0", "Githubie 1.2.0.0", "notes", false, false, [asset]),
                TestContext.Current.CancellationToken);

            result.IsSuccess.Should().BeTrue();
            result.Value!.Draft.Should().BeFalse();
            result.Value.Assets.Should().ContainSingle().Which.Name.Should().Be("Githubie-1.2.0.0-win-x64.zip");
            requests.Select(item => item.Method).Should().Equal(HttpMethod.Post, HttpMethod.Post, HttpMethod.Patch);
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }

    [Fact]
    public async Task CreateReleaseAsync_AssetOutsideRepositoryRoot_IsRejectedBeforeApiCall()
    {
        var client = CreateClient(_ => throw new InvalidOperationException("API must not be called"));

        var result = await client.CreateReleaseAsync(
            "repo-id", "owner", "repo", Path.GetTempPath(),
            new GitHubReleaseCreate("v1.2.0.0", "Release", null, false, false, ["C:\\outside\\asset.exe"]),
            TestContext.Current.CancellationToken);

        result.Error.Should().Be(GitHubError.ReleaseAssetInvalid);
    }

    private static HttpResponseMessage Json(HttpStatusCode status, string json) => new(status)
    {
        Content = new StringContent(json),
    };

    private static string ReleaseJson(bool draft) => $$"""
        {"id":10,"tag_name":"v1.2.0.0","name":"Githubie 1.2.0.0","draft":{{draft.ToString().ToLowerInvariant()}},"prerelease":false,"html_url":"https://github.com/owner/repo/releases/tag/v1.2.0.0","upload_url":"https://uploads.github.com/repos/owner/repo/releases/10/assets{?name,label}"}
        """;
}
