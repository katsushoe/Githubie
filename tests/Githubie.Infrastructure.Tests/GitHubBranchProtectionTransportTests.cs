using System.Net;
using System.Text;
using System.Text.Json.Nodes;
using FluentAssertions;
using Githubie.Application.GitHub;
using Githubie.Infrastructure.GitHub;
using Xunit;

namespace Githubie.Infrastructure.Tests;

public sealed partial class GitHubApiClientTests
{
    private const string ProtectionResponse = """
        {"url":"https://example.invalid/rule","required_status_checks":null,
         "required_pull_request_reviews":null,"restrictions":null,
         "required_signatures":{"enabled":false},"enforce_admins":{"enabled":true},
         "required_linear_history":{"enabled":false},"allow_force_pushes":{"enabled":false},
         "allow_deletions":{"enabled":false},"block_creations":{"enabled":false},
         "required_conversation_resolution":{"enabled":false},"lock_branch":{"enabled":false},
         "allow_fork_syncing":{"enabled":false}}
        """;

    [Fact]
    public async Task Read_EncodesBranchAndReturnsOnlySuccessfulObject()
    {
        var client = CreateClient(request =>
        {
            request.Method.Should().Be(HttpMethod.Get);
            request.RequestUri!.AbsoluteUri.Should().Contain("branches/feature%2Fexample/protection");
            return JsonResponse(HttpStatusCode.OK, ProtectionResponse);
        });
        var result = await client.ReadBranchProtectionAsync("fixture", "owner", "repo", "feature/example", TestContext.Current.CancellationToken);
        result.IsSuccess.Should().BeTrue();
        result.Value!["enforce_admins"]!["enabled"]!.GetValue<bool>().Should().BeTrue();
    }

    [Fact]
    public async Task Read_404IsNotConvertedToUnprotected_AndSecretIsNotReturned()
    {
        var client = CreateClient(_ => JsonResponse(HttpStatusCode.NotFound, """{"message":"dummy-token"}"""));
        var result = await client.ReadBranchProtectionAsync("fixture", "owner", "repo", "main", TestContext.Current.CancellationToken);
        result.IsSuccess.Should().BeFalse();
        result.Error.Should().Be(GitHubError.BranchNotFound);
        result.Diagnostic.Should().BeNull();
    }

    [Fact]
    public async Task Read_RejectsCredentialReflectedInSuccessfulResponse()
    {
        var client = CreateClient(_ => JsonResponse(HttpStatusCode.OK, """{"required_status_checks":{"contexts":["dummy-token"]}}"""));
        var result = await client.ReadBranchProtectionAsync("fixture", "owner", "repo", "main", TestContext.Current.CancellationToken);
        result.Error.Should().Be(GitHubError.InvalidResponse);
        result.Value.Should().BeNull();
        result.Diagnostic.Should().BeNull();
    }

    [Fact]
    public async Task Put_RejectsCredentialInProposedPolicyBeforeSending()
    {
        var calls = 0;
        var client = CreateClient(_ => { calls++; return JsonResponse(HttpStatusCode.OK, ProtectionResponse); });
        var result = await client.PutBranchProtectionAsync("fixture", "owner", "repo", "main",
            new JsonObject { ["context"] = "dummy-token" }, TestContext.Current.CancellationToken);
        result.Error.Should().Be(GitHubError.InvalidResponse);
        calls.Should().Be(0);
    }

    [Fact]
    public async Task Read_RejectsCredentialAsBranchBeforeSending()
    {
        var calls = 0;
        var client = CreateClient(_ => { calls++; return JsonResponse(HttpStatusCode.OK, ProtectionResponse); });
        var result = await client.ReadBranchProtectionAsync("fixture", "owner", "repo", "dummy-token", TestContext.Current.CancellationToken);
        result.Error.Should().Be(GitHubError.InvalidResponse);
        calls.Should().Be(0);
    }

    [Fact]
    public async Task Put_WritesExplicitNullsAndVerifiesReturnedState()
    {
        var current = JsonNode.Parse(ProtectionResponse)!.AsObject();
        GitHubBranchProtectionPayload.TryBuild(current, new(AllowDeletions: true), out var approved).Should().BeTrue();
        current["allow_deletions"]!["enabled"] = true;
        var calls = 0;
        var client = CreateCapturingClient((request, body) =>
        {
            calls++;
            request.Method.Should().Be(HttpMethod.Put);
            JsonNode.DeepEquals(JsonNode.Parse(body!), approved).Should().BeTrue();
            JsonNode.Parse(body!)!.AsObject().ContainsKey("required_status_checks").Should().BeTrue();
            return JsonResponse(HttpStatusCode.OK, current.ToJsonString());
        });
        var result = await client.PutBranchProtectionAsync("fixture", "owner", "repo", "main", approved!, TestContext.Current.CancellationToken);
        result.IsSuccess.Should().BeTrue();
        calls.Should().Be(1);
    }

    [Theory]
    [InlineData(HttpStatusCode.InternalServerError)]
    [InlineData(HttpStatusCode.RequestTimeout)]
    public async Task Put_UncertainHttpFailure_IsUnknownAndNeverReturnsBody(HttpStatusCode status)
    {
        var calls = 0;
        var client = CreateClient(_ => { calls++; return JsonResponse(status, """{"message":"dummy-token"}"""); });
        var result = await client.PutBranchProtectionAsync("fixture", "owner", "repo", "main", new JsonObject(), TestContext.Current.CancellationToken);
        result.Error.Should().Be(GitHubError.BranchProtectionResultUnknown);
        result.Diagnostic.Should().BeNull();
        calls.Should().Be(1);
    }

    [Fact]
    public async Task Put_TransportLoss_IsUnknownWithoutReplay()
    {
        var calls = 0;
        var client = CreateClient(_ => { calls++; throw new HttpRequestException("dummy-token"); });
        var result = await client.PutBranchProtectionAsync("fixture", "owner", "repo", "main", new JsonObject(), TestContext.Current.CancellationToken);
        result.Error.Should().Be(GitHubError.BranchProtectionResultUnknown);
        result.Diagnostic.Should().BeNull();
        calls.Should().Be(1);
    }

    [Fact]
    public async Task Put_MalformedSuccess_IsUnknown()
    {
        var client = CreateClient(_ => JsonResponse(HttpStatusCode.OK, "not-json"));
        var result = await client.PutBranchProtectionAsync("fixture", "owner", "repo", "main", new JsonObject(), TestContext.Current.CancellationToken);
        result.Error.Should().Be(GitHubError.BranchProtectionResultUnknown);
    }

    [Theory]
    [InlineData(HttpStatusCode.NoContent, true)]
    [InlineData(HttpStatusCode.OK, false)]
    public async Task Delete_RequiresExpectedNoContentResponse(HttpStatusCode status, bool success)
    {
        var client = CreateClient(_ => new HttpResponseMessage(status));
        var result = await client.DeleteBranchProtectionAsync("fixture", "owner", "repo", "main", TestContext.Current.CancellationToken);
        result.IsSuccess.Should().Be(success);
        if (!success) result.Error.Should().Be(GitHubError.BranchProtectionResultUnknown);
    }

    [Fact]
    public async Task Read_RejectsOversizedResponse()
    {
        var client = CreateClient(_ => JsonResponse(HttpStatusCode.OK, new string('x', 1024 * 1024 + 1)));
        var result = await client.ReadBranchProtectionAsync("fixture", "owner", "repo", "main", TestContext.Current.CancellationToken);
        result.Error.Should().Be(GitHubError.InvalidResponse);
    }

    private static HttpResponseMessage JsonResponse(HttpStatusCode status, string body) =>
        new(status) { Content = new StringContent(body, Encoding.UTF8, "application/json") };
}
