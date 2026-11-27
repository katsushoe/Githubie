using System.Net;
using System.Text.Json;
using FluentAssertions;
using Githubie.Application.GitHub;
using Xunit;

namespace Githubie.Infrastructure.Tests;

public sealed partial class GitHubApiClientTests
{
    private const string MergeSource = "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa";
    private const string MergeCommit = "bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb";
    private static string MergedPullRequestJson => $$"""
        {"number":1,"title":"t","state":"closed","merged":true,"draft":false,"head":{"ref":"develop","sha":"{{MergeSource}}"},"base":{"ref":"main"},"user":{"login":"u"},"html_url":"https://example.com/pr/1","merge_commit_sha":"{{MergeCommit}}","created_at":"2026-01-01T00:00:00Z","updated_at":"2026-01-01T00:00:00Z"}
        """;

    [Fact]
    public async Task MergeTransport_ForwardsShaStrategyAndUnchangedBody_ThenVerifiesObservedMerge()
    {
        const string message = "  approved body\nkept  ";
        var methods = new List<HttpMethod>();
        string? captured = null;
        var client = CreateCapturingClient((request, body) =>
        {
            methods.Add(request.Method);
            if (request.Method == HttpMethod.Put)
            {
                captured = body;
                return Json(HttpStatusCode.OK, $$"""{"merged":true,"sha":"{{MergeCommit}}"}""");
            }
            return Json(HttpStatusCode.OK, MergedPullRequestJson);
        });

        var result = await client.MergePullRequestAsync("repo-id", "owner", "repo",
            new(1, GitHubMergeMethod.Squash, message, MergeSource), TestContext.Current.CancellationToken);

        result.IsSuccess.Should().BeTrue();
        methods.Should().Equal(HttpMethod.Put, HttpMethod.Get);
        using var json = JsonDocument.Parse(captured!);
        json.RootElement.GetProperty("sha").GetString().Should().Be(MergeSource);
        json.RootElement.GetProperty("merge_method").GetString().Should().Be("squash");
        json.RootElement.GetProperty("commit_message").GetString().Should().Be(message);
        result.Value!.SourceCommitSha.Should().Be(MergeSource);
        result.Value.MergeCommitSha.Should().Be(MergeCommit);
        result.Value.Draft.Should().BeFalse();
    }

    [Fact]
    public async Task MergeTransport_ExpectedShaConflict_IsExplicitRejectionWithoutReadOrReplay()
    {
        var calls = 0;
        var client = CreateClient(_ => { calls++; return Json(HttpStatusCode.Conflict, "{\"message\":\"Head changed\"}"); });

        var result = await client.MergePullRequestAsync("repo-id", "owner", "repo",
            new(1, null, null, MergeSource), TestContext.Current.CancellationToken);

        result.Error.Should().Be(GitHubError.ExpectedSourceCommitMismatch);
        result.HttpStatus.Should().Be(409);
        calls.Should().Be(1);
    }

    [Theory]
    [InlineData("not json")]
    [InlineData("{}")]
    [InlineData("{\"merged\":false}")]
    [InlineData("{\"merged\":true,\"sha\":\"short\"}")]
    public async Task MergeTransport_UnprovenPutResult_IsUnknownWithoutReplay(string response)
    {
        var calls = 0;
        var client = CreateClient(_ => { calls++; return Json(HttpStatusCode.OK, response); });

        var result = await client.MergePullRequestAsync("repo-id", "owner", "repo",
            new(1, null, null, MergeSource), TestContext.Current.CancellationToken);

        result.Error.Should().Be(GitHubError.PullRequestMergeResultUnknown);
        calls.Should().Be(1);
    }

    [Theory]
    [InlineData(500)]
    [InlineData(404)]
    [InlineData(403)]
    public async Task MergeTransport_PutSucceededButReadFailed_IsUnknownWithoutAnotherPut(int readStatus)
    {
        var methods = new List<HttpMethod>();
        var client = CreateClient(request =>
        {
            methods.Add(request.Method);
            return request.Method == HttpMethod.Put
                ? Json(HttpStatusCode.OK, $$"""{"merged":true,"sha":"{{MergeCommit}}"}""")
                : Json((HttpStatusCode)readStatus, "{}");
        });

        var result = await client.MergePullRequestAsync("repo-id", "owner", "repo",
            new(1, null, null, MergeSource), TestContext.Current.CancellationToken);

        result.Error.Should().Be(GitHubError.PullRequestMergeResultUnknown);
        methods.Should().Equal(HttpMethod.Put, HttpMethod.Get);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task MergeTransport_NetworkLossOrCancellation_IsUnknown(bool cancelled)
    {
        var calls = 0;
        var client = CreateClient(_ =>
        {
            calls++;
            if (cancelled) throw new OperationCanceledException();
            throw new HttpRequestException("lost reply");
        });

        var result = await client.MergePullRequestAsync("repo-id", "owner", "repo",
            new(1, null, null, MergeSource), TestContext.Current.CancellationToken);

        result.Error.Should().Be(GitHubError.PullRequestMergeResultUnknown);
        calls.Should().Be(1);
    }

    [Fact]
    public async Task MergeTransport_MismatchingObservedMergeCommit_IsUnknown()
    {
        var calls = 0;
        var client = CreateClient(request =>
        {
            calls++;
            return request.Method == HttpMethod.Put
                ? Json(HttpStatusCode.OK, $$"""{"merged":true,"sha":"{{MergeSource}}"}""")
                : Json(HttpStatusCode.OK, MergedPullRequestJson);
        });

        var result = await client.MergePullRequestAsync("repo-id", "owner", "repo",
            new(1, null, null, MergeSource), TestContext.Current.CancellationToken);

        result.Error.Should().Be(GitHubError.PullRequestMergeResultUnknown);
        calls.Should().Be(2);
    }

    [Fact]
    public async Task PrRead_ExposesActualHeadShaAndNullableDraft()
    {
        var client = CreateClient(_ => Json(HttpStatusCode.OK, MergedPullRequestJson.Replace("\"draft\":false", "\"draft\":null")));

        var result = await client.GetPullRequestAsync("repo-id", "owner", "repo", 1, TestContext.Current.CancellationToken);

        result.Value!.SourceCommitSha.Should().Be(MergeSource);
        result.Value.Draft.Should().BeNull();
    }

    [Fact]
    public async Task PrRead_ExposesRepositoryIdentityForPublicationProof()
    {
        var body = MergedPullRequestJson
            .Replace("\"head\":{", "\"head\":{\"repo\":{\"full_name\":\"owner/repo\"},")
            .Replace("\"base\":{", "\"base\":{\"repo\":{\"full_name\":\"owner/repo\"},");
        var client = CreateClient(_ => Json(HttpStatusCode.OK, body));

        var result = await client.GetPullRequestAsync("repo-id", "owner", "repo", 1, TestContext.Current.CancellationToken);

        result.IsSuccess.Should().BeTrue();
        result.Value!.SourceCommitSha.Should().Be(MergeSource);
        result.Value.HeadRepository.Should().Be("owner/repo");
        result.Value.BaseRepository.Should().Be("owner/repo");
        result.Value.MergeCommitSha.Should().Be(MergeCommit);
    }

    [Theory]
    [InlineData("missing_sha")]
    [InlineData("invalid_sha")]
    [InlineData("wrong_number")]
    public async Task PrRead_UnprovenIdentityOrSourceSha_IsRejected(string kind)
    {
        var body = kind switch
        {
            "missing_sha" => MergedPullRequestJson.Replace($",\"sha\":\"{MergeSource}\"", ""),
            "invalid_sha" => MergedPullRequestJson.Replace(MergeSource, "short"),
            _ => MergedPullRequestJson.Replace("\"number\":1", "\"number\":2"),
        };
        var client = CreateClient(_ => Json(HttpStatusCode.OK, body));

        var result = await client.GetPullRequestAsync("repo-id", "owner", "repo", 1, TestContext.Current.CancellationToken);

        result.Error.Should().Be(GitHubError.InvalidResponse);
    }
}
