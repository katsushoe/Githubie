using System.Text;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Xunit;

namespace Githubie.Server.Tests;

public sealed class UserHostRequestGateTests
{
    private const string Credential = "random-session-credential";

    [Theory]
    [InlineData("list_projects")]
    [InlineData("github_repository_status")]
    [InlineData("github_repository_register")]
    [InlineData("unknown_tool")]
    public async Task LoopbackWithoutCredential_RejectsBusinessCalls(string tool)
    {
        var request = Request(tool);
        (await Authorize(request)).Should().BeFalse();
        request.Body.Position.Should().Be(0);
    }

    [Fact]
    public async Task CorrectCredential_AcceptsBusinessCallAndPreservesBody()
    {
        var request = Request("list_projects");
        request.Headers[UserHostRequestGate.CredentialHeader] = Credential;
        (await Authorize(request)).Should().BeTrue();
        request.Body.Position.Should().Be(0);
    }

    [Theory]
    [InlineData("evil.example:45460", null)]
    [InlineData("localhost:45461", null)]
    [InlineData("localhost:45460", "https://evil.example")]
    public async Task CorrectCredential_DoesNotBypassHostOrOrigin(string host, string? origin)
    {
        var request = Request("list_projects");
        request.Host = new HostString(host);
        request.Headers[UserHostRequestGate.CredentialHeader] = Credential;
        if (origin is not null) request.Headers.Origin = origin;
        (await Authorize(request)).Should().BeFalse();
    }

    [Theory]
    [InlineData("get_version", true)]
    [InlineData("github_provider_capabilities", false)]
    public async Task Bootstrap_ExposesOnlyNonRepositoryMetadata(string tool, bool allowed)
    {
        (await Authorize(Request(tool))).Should().Be(allowed);
    }

    [Theory]
    [InlineData("github_repository_status", true, true)]
    [InlineData("github_repository_status", false, false)]
    [InlineData("list_projects", true, false)]
    [InlineData("unknown_tool", true, false)]
    public async Task ProviderMarker_DelegatesOnlyKnownScopedToolsToAssertionValidation(string tool, bool providerEnabled, bool delegated)
    {
        var request = Request(tool);
        request.Headers.Authorization = "Bearer deliberately-not-a-valid-signature";
        request.Headers["X-Moyai-Operation-Id"] = "test-operation";
        (await Authorize(request, providerEnabled)).Should().Be(delegated);
        // This gate delegates; GithubieAssertionMiddleware remains responsible for cryptographic validation.
    }

    private static Task<bool> Authorize(HttpRequest request, bool providerEnabled = false) =>
        UserHostRequestGate.IsAuthorizedAsync(request, Credential, 45460, providerEnabled, TestContext.Current.CancellationToken);

    private static HttpRequest Request(string tool)
    {
        var request = new DefaultHttpContext().Request;
        request.Host = new HostString("localhost", 45460);
        request.Method = "POST";
        request.Body = new MemoryStream(System.Text.Json.JsonSerializer.SerializeToUtf8Bytes(new
        { jsonrpc = "2.0", id = 1, method = "tools/call", @params = new { name = tool, arguments = new { } } }));
        return request;
    }
}
