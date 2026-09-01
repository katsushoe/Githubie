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
    public async Task GetRepositoryAsync_ReturnsDescription()
    {
        var client = CreateClient(_ => Json(HttpStatusCode.OK,
            """{"default_branch":"main","description":"sample description"}"""));

        var result = await client.GetRepositoryAsync("repo-id", "owner", "repo", TestContext.Current.CancellationToken);

        result.IsSuccess.Should().BeTrue();
        result.Value!.Description.Should().Be("sample description");
    }

    [Fact]
    public async Task UpdateRepositoryDescriptionAsync_PatchesOnlyDescription()
    {
        HttpMethod? method = null;
        string? path = null;
        string? capturedBody = null;
        var client = CreateCapturingClient((request, body) =>
        {
            method = request.Method;
            path = request.RequestUri!.PathAndQuery;
            capturedBody = body;
            return Json(HttpStatusCode.OK, """{"default_branch":"main","description":"説明"}""");
        });

        var result = await client.UpdateRepositoryDescriptionAsync(
            "repo-id", "owner", "repo", "説明", TestContext.Current.CancellationToken);

        result.IsSuccess.Should().BeTrue();
        method.Should().Be(HttpMethod.Patch);
        path.Should().Be("/repos/owner/repo");
        using var json = System.Text.Json.JsonDocument.Parse(capturedBody!);
        json.RootElement.EnumerateObject().Should().ContainSingle();
        json.RootElement.GetProperty("description").GetString().Should().Be("説明");
    }

    [Theory]
    [InlineData(HttpStatusCode.Forbidden, GitHubError.TokenScopeMissing)]
    [InlineData(HttpStatusCode.NotFound, GitHubError.RepositoryNotFound)]
    [InlineData(HttpStatusCode.UnprocessableEntity, GitHubError.RepositoryDescriptionInvalid)]
    public async Task UpdateRepositoryDescriptionAsync_ClassifiesErrors(HttpStatusCode status, GitHubError expected)
    {
        var client = CreateClient(_ => new HttpResponseMessage(status));

        var result = await client.UpdateRepositoryDescriptionAsync(
            "repo-id", "owner", "repo", string.Empty, TestContext.Current.CancellationToken);

        result.Error.Should().Be(expected);
    }

    [Fact]
    public async Task DispatchWorkflowAsync_PostsRefAndInputsToFixedActionsEndpoint()
    {
        HttpMethod? method = null;
        string? path = null;
        string? capturedBody = null;
        var client = CreateCapturingClient((request, body) =>
        {
            method = request.Method;
            path = request.RequestUri!.PathAndQuery;
            capturedBody = body;
            return new HttpResponseMessage(HttpStatusCode.NoContent);
        });

        var result = await client.DispatchWorkflowAsync("repo-id", "owner", "repo",
            new("release.yml", "main", new Dictionary<string, string> { ["version"] = "1.2.3" }),
            TestContext.Current.CancellationToken);

        result.IsSuccess.Should().BeTrue();
        method.Should().Be(HttpMethod.Post);
        path.Should().Be("/repos/owner/repo/actions/workflows/release.yml/dispatches");
        using var json = System.Text.Json.JsonDocument.Parse(capturedBody!);
        json.RootElement.GetProperty("ref").GetString().Should().Be("main");
        json.RootElement.GetProperty("inputs").GetProperty("version").GetString().Should().Be("1.2.3");
    }

    [Fact]
    public async Task ListWorkflowRunsAsync_ReturnsMetadataWithoutLogs()
    {
        var client = CreateClient(_ => Json(HttpStatusCode.OK, """
            {"workflow_runs":[{"id":42,"name":"Release","head_branch":"main","head_sha":"abc","event":"workflow_dispatch","status":"completed","conclusion":"success","actor":{"login":"user"},"created_at":"2026-08-25T00:00:00Z","updated_at":"2026-08-25T00:01:00Z","html_url":"https://github.com/o/r/actions/runs/42"}]}
            """));

        var result = await client.ListWorkflowRunsAsync(
            "repo-id", "owner", "repo", "release.yml", "main", "workflow_dispatch", "completed", 10,
            TestContext.Current.CancellationToken);

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().ContainSingle().Which.Id.Should().Be(42);
        result.Value![0].Conclusion.Should().Be("success");
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
    public async Task ListIssuesAsync_ExcludesPullRequestsAndMapsMetadata()
    {
        var client = CreateClient(request =>
        {
            request.RequestUri!.PathAndQuery.Should().Be("/repos/owner/repo/issues?state=open&per_page=100");
            return Json(HttpStatusCode.OK, """
                [
                  {"number":7,"title":"Issue","body":"details","state":"open","user":{"login":"author"},"labels":[{"name":"bug"}],"assignees":[{"login":"owner"}],"comments":2,"locked":false,"created_at":"2026-01-01T00:00:00Z","updated_at":"2026-01-02T00:00:00Z","html_url":"https://example.com/issues/7"},
                  {"number":8,"title":"PR","state":"open","user":{"login":"author"},"labels":[],"assignees":[],"comments":0,"locked":false,"created_at":"2026-01-01T00:00:00Z","updated_at":"2026-01-01T00:00:00Z","html_url":"https://example.com/pull/8","pull_request":{}}
                ]
                """);
        });

        var result = await client.ListIssuesAsync(
            "repo-id", "owner", "repo", GitHubIssueState.Open, TestContext.Current.CancellationToken);

        var issue = result.Value.Should().ContainSingle().Subject;
        issue.Number.Should().Be(7);
        issue.Labels.Should().Equal("bug");
        issue.Assignees.Should().Equal("owner");
        issue.Comments.Should().Be(2);
    }

    [Fact]
    public async Task GetIssueAsync_WhenNumberIsPullRequest_ReturnsIssueNotFound()
    {
        var client = CreateClient(_ => Json(HttpStatusCode.OK, """
            {"number":8,"title":"PR","state":"open","user":{"login":"author"},"labels":[],"assignees":[],"comments":0,"locked":false,"created_at":"2026-01-01T00:00:00Z","updated_at":"2026-01-01T00:00:00Z","html_url":"https://example.com/pull/8","pull_request":{}}
            """));

        var result = await client.GetIssueAsync(
            "repo-id", "owner", "repo", 8, TestContext.Current.CancellationToken);

        result.IsSuccess.Should().BeFalse();
        result.Error.Should().Be(GitHubError.IssueNotFound);
    }

    [Fact]
    public async Task GetIssueAsync_MapsNotFoundToIssueNotFound()
    {
        var client = CreateClient(_ => new HttpResponseMessage(HttpStatusCode.NotFound));

        var result = await client.GetIssueAsync(
            "repo-id", "owner", "repo", 9999, TestContext.Current.CancellationToken);

        result.Error.Should().Be(GitHubError.IssueNotFound);
    }

    [Theory]
    [InlineData(null, "unknown", GitHubMergeabilityStatus.CalculatingRetryable, 2)]
    [InlineData(true, "clean", GitHubMergeabilityStatus.Mergeable, null)]
    [InlineData(false, "dirty", GitHubMergeabilityStatus.Conflicting, null)]
    [InlineData(false, "blocked", GitHubMergeabilityStatus.Blocked, null)]
    [InlineData(true, "blocked", GitHubMergeabilityStatus.Blocked, null)]
    [InlineData(false, "mystery", GitHubMergeabilityStatus.UnknownRetryable, 2)]
    public async Task GetPullRequestAsync_ClassifiesMergeability(
        bool? mergeable, string mergeableState, string expectedStatus, int? expectedRetryAfter)
    {
        var mergeableJson = mergeable is null ? "null" : mergeable.Value.ToString().ToLowerInvariant();
        var client = CreateClient(_ => Json(HttpStatusCode.OK, $$"""
            {"number":1,"title":"t","state":"open","merged":false,"head":{"ref":"develop"},"base":{"ref":"main"},"user":{"login":"u"},"mergeable":{{mergeableJson}},"mergeable_state":"{{mergeableState}}","html_url":"https://example.com","created_at":"2026-01-01T00:00:00Z","updated_at":"2026-01-01T00:00:00Z"}
            """));

        var result = await client.GetPullRequestAsync(
            "repo-id", "owner", "repo", 1, TestContext.Current.CancellationToken);

        result.Value!.MergeabilityStatus.Should().Be(expectedStatus);
        result.Value.RetryAfterSeconds.Should().Be(expectedRetryAfter);
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
    public async Task UpdatePullRequestStateAsync_UsesPatchWithClosedState()
    {
        HttpMethod? method = null;
        string? capturedBody = null;
        var client = CreateCapturingClient((request, body) =>
        {
            method = request.Method;
            capturedBody = body;
            return Json(HttpStatusCode.OK,
                """{"number":1,"title":"t","state":"closed","merged":false,"head":{"ref":"develop"},"base":{"ref":"main"},"user":{"login":"u"},"html_url":"https://example.com","created_at":"2026-01-01T00:00:00Z","updated_at":"2026-01-01T00:00:00Z"}""");
        });

        var result = await client.UpdatePullRequestStateAsync(
            "repo-id", "owner", "repo", 1, GitHubPullRequestState.Closed, TestContext.Current.CancellationToken);

        result.Value!.State.Should().Be(GitHubPullRequestState.Closed);
        method.Should().Be(HttpMethod.Patch);
        capturedBody.Should().Contain("\"state\":\"closed\"");
    }

    [Fact]
    public async Task UpdatePullRequestStateAsync_MergedState_IsRejectedBeforeRequest()
    {
        var client = CreateClient(_ => throw new InvalidOperationException("Request must not be sent."));

        var result = await client.UpdatePullRequestStateAsync(
            "repo-id", "owner", "repo", 1, GitHubPullRequestState.Merged, TestContext.Current.CancellationToken);

        result.Error.Should().Be(GitHubError.PullRequestStateNotAllowed);
    }

    [Fact]
    public async Task CreatePullRequestCommentAsync_UsesIssueCommentEndpointAndMapsResponse()
    {
        string? path = null;
        string? capturedBody = null;
        var client = CreateCapturingClient((request, body) =>
        {
            path = request.RequestUri!.AbsolutePath;
            capturedBody = body;
            return Json(HttpStatusCode.Created,
                """{"id":42,"body":"hello","user":{"login":"u"},"created_at":"2026-01-01T00:00:00Z","updated_at":"2026-01-01T00:00:00Z","html_url":"https://example.com/comment"}""");
        });

        var result = await client.CreatePullRequestCommentAsync(
            "repo-id", "owner", "repo", 7, "hello", TestContext.Current.CancellationToken);

        result.Value!.Id.Should().Be(42);
        path.Should().Be("/repos/owner/repo/issues/7/comments");
        capturedBody.Should().Contain("\"body\":\"hello\"");
    }

    [Fact]
    public async Task ListPullRequestCommentsAsync_ReturnsConversationComments()
    {
        var client = CreateClient(_ => Json(HttpStatusCode.OK,
            """[{"id":42,"body":"hello","user":{"login":"u"},"created_at":"2026-01-01T00:00:00Z","updated_at":"2026-01-01T00:00:00Z","html_url":"https://example.com/comment"}]"""));

        var result = await client.ListPullRequestCommentsAsync(
            "repo-id", "owner", "repo", 7, TestContext.Current.CancellationToken);

        result.Value.Should().ContainSingle().Which.Author.Should().Be("u");
    }

    [Theory]
    [InlineData(GitHubPullRequestReviewAction.Approve, "APPROVE", "APPROVED")]
    [InlineData(GitHubPullRequestReviewAction.RequestChanges, "REQUEST_CHANGES", "CHANGES_REQUESTED")]
    public async Task CreatePullRequestReviewAsync_UsesReviewEndpointAndMapsResponse(
        GitHubPullRequestReviewAction action, string expectedEvent, string responseState)
    {
        string? path = null;
        string? capturedBody = null;
        var client = CreateCapturingClient((request, body) =>
        {
            path = request.RequestUri!.AbsolutePath;
            capturedBody = body;
            return Json(HttpStatusCode.OK,
                $$"""{"id":91,"body":"review","user":{"login":"reviewer"},"state":"{{responseState}}","submitted_at":"2026-01-01T00:00:00Z","commit_id":"abc","html_url":"https://example.com/review/91"}""");
        });

        var result = await client.CreatePullRequestReviewAsync(
            "repo-id", "owner", "repo", 7, action, "review", TestContext.Current.CancellationToken);

        result.Value!.State.Should().Be(responseState);
        path.Should().Be("/repos/owner/repo/pulls/7/reviews");
        capturedBody.Should().Contain($"\"event\":\"{expectedEvent}\"");
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
                if (request.Method == HttpMethod.Get)
                    return new HttpResponseMessage(HttpStatusCode.NotFound);
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
            requests.Select(item => item.Method).Should().Equal(HttpMethod.Get, HttpMethod.Post, HttpMethod.Post, HttpMethod.Patch);
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

    [Fact]
    public async Task DeleteTagAsync_UsesTagRefDeleteEndpoint()
    {
        HttpMethod? method = null;
        string? path = null;
        var client = CreateClient(request =>
        {
            method = request.Method;
            path = request.RequestUri!.AbsolutePath;
            return new HttpResponseMessage(HttpStatusCode.NoContent);
        });

        var result = await client.DeleteTagAsync(
            "repo-id", "owner", "repo", "v1.0.0", TestContext.Current.CancellationToken);

        result.Value.Should().BeTrue();
        method.Should().Be(HttpMethod.Delete);
        path.Should().Be("/repos/owner/repo/git/refs/tags/v1.0.0");
    }

    [Fact]
    public async Task ListReleasesAsync_ReturnsAssets()
    {
        var client = CreateClient(_ => Json(HttpStatusCode.OK,
            """[{"id":10,"tag_name":"v1","name":"R","draft":false,"prerelease":false,"html_url":"https://example.com/r","upload_url":"https://uploads.github.com/repos/owner/repo/releases/10/assets{?name,label}","assets":[{"id":20,"name":"a.zip","size":7,"browser_download_url":"https://example.com/a"}]}]"""));

        var result = await client.ListReleasesAsync("repo-id", "owner", "repo", TestContext.Current.CancellationToken);

        result.Value.Should().ContainSingle().Which.Assets.Should().ContainSingle().Which.Id.Should().Be(20);
    }

    [Fact]
    public async Task UploadReleaseAssetsAsync_ExistingNameWithoutReplace_ReturnsConflict()
    {
        var root = Path.Combine(Path.GetTempPath(), $"githubie-release-{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);
        var asset = Path.Combine(root, "a.zip");
        await File.WriteAllTextAsync(asset, "payload", TestContext.Current.CancellationToken);
        try
        {
            var client = CreateClient(_ => Json(HttpStatusCode.OK,
                """{"id":10,"tag_name":"v1","name":"R","draft":true,"prerelease":false,"html_url":"https://example.com/r","upload_url":"https://uploads.github.com/repos/owner/repo/releases/10/assets{?name,label}","assets":[{"id":20,"name":"a.zip","size":7,"browser_download_url":"https://example.com/a"}]}"""));

            var result = await client.UploadReleaseAssetsAsync(
                "repo-id", "owner", "repo", root, new GitHubReleaseAssetUpload(10, [asset], false),
                TestContext.Current.CancellationToken);

            result.Error.Should().Be(GitHubError.ReleaseAssetAlreadyExists);
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }

    [Fact]
    public async Task UploadReleaseAssetsAsync_ExistingNameWithReplace_DeletesThenUploads()
    {
        var root = Path.Combine(Path.GetTempPath(), $"githubie-release-{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);
        var asset = Path.Combine(root, "a.zip");
        await File.WriteAllTextAsync(asset, "payload", TestContext.Current.CancellationToken);
        var methods = new List<HttpMethod>();
        try
        {
            var client = CreateClient(request =>
            {
                methods.Add(request.Method);
                if (request.Method == HttpMethod.Delete) return new HttpResponseMessage(HttpStatusCode.NoContent);
                if (request.Method == HttpMethod.Post) return Json(HttpStatusCode.Created,
                    """{"id":21,"name":"a.zip","size":7,"browser_download_url":"https://example.com/new"}""");
                return Json(HttpStatusCode.OK,
                    """{"id":10,"tag_name":"v1","name":"R","draft":true,"prerelease":false,"html_url":"https://example.com/r","upload_url":"https://uploads.github.com/repos/owner/repo/releases/10/assets{?name,label}","assets":[{"id":20,"name":"a.zip","size":7,"browser_download_url":"https://example.com/a"}]}""");
            });

            var result = await client.UploadReleaseAssetsAsync(
                "repo-id", "owner", "repo", root, new GitHubReleaseAssetUpload(10, [asset], true),
                TestContext.Current.CancellationToken);

            result.IsSuccess.Should().BeTrue();
            methods.Should().Equal(HttpMethod.Get, HttpMethod.Delete, HttpMethod.Post, HttpMethod.Get);
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }

    [Fact]
    public async Task CreateReleaseAsync_MatchingDraftRetry_SkipsExistingAsset()
    {
        var root = Path.Combine(Path.GetTempPath(), $"githubie-release-{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);
        var asset = Path.Combine(root, "a.zip");
        await File.WriteAllTextAsync(asset, "payload", TestContext.Current.CancellationToken);
        var requestCount = 0;
        try
        {
            var client = CreateClient(_ =>
            {
                requestCount++;
                return Json(HttpStatusCode.OK,
                    """{"id":10,"tag_name":"v1","name":"R","draft":true,"prerelease":false,"html_url":"https://example.com/r","upload_url":"https://uploads.github.com/repos/owner/repo/releases/10/assets{?name,label}","assets":[{"id":20,"name":"a.zip","size":7,"browser_download_url":"https://example.com/a"}]}""");
            });

            var result = await client.CreateReleaseAsync(
                "repo-id", "owner", "repo", root,
                new GitHubReleaseCreate("v1", "R", null, true, false, [asset]), TestContext.Current.CancellationToken);

            result.IsSuccess.Should().BeTrue();
            requestCount.Should().Be(1);
            result.Value!.Assets.Should().ContainSingle();
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }

    [Theory]
    [InlineData("Install-App.ps1")]
    [InlineData("SHA256SUMS.txt")]
    public async Task CreateReleaseAsync_DistributionScriptAndChecksumList_AreAllowed(string fileName)
    {
        var root = Path.Combine(Path.GetTempPath(), $"githubie-release-{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);
        var asset = Path.Combine(root, fileName);
        await File.WriteAllTextAsync(asset, "payload", TestContext.Current.CancellationToken);
        try
        {
            var client = CreateClient(request => request.Method == HttpMethod.Get
                ? new HttpResponseMessage(HttpStatusCode.NotFound)
                : request.RequestUri!.Host == "uploads.github.com"
                    ? Json(HttpStatusCode.Created, $$"""{"id":20,"name":"{{fileName}}","size":7,"browser_download_url":"https://example.com/a"}""")
                    : Json(HttpStatusCode.Created, ReleaseJson(draft: true)));

            var result = await client.CreateReleaseAsync(
                "repo-id", "owner", "repo", root,
                new GitHubReleaseCreate("v1", "R", null, true, false, [asset]), TestContext.Current.CancellationToken);

            result.IsSuccess.Should().BeTrue();
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }

    private static HttpResponseMessage Json(HttpStatusCode status, string json) => new(status)
    {
        Content = new StringContent(json),
    };

    private static string ReleaseJson(bool draft) => $$"""
        {"id":10,"tag_name":"v1.2.0.0","name":"Githubie 1.2.0.0","draft":{{draft.ToString().ToLowerInvariant()}},"prerelease":false,"html_url":"https://github.com/owner/repo/releases/tag/v1.2.0.0","upload_url":"https://uploads.github.com/repos/owner/repo/releases/10/assets{?name,label}"}
        """;
}
