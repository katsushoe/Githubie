using System.Net;
using System.Net.Http.Headers;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using FluentAssertions;
using Githubie.Application.Configuration;
using Githubie.Application.Git;
using Githubie.Application.GitHub;
using Githubie.Application.Repositories;
using Githubie.Infrastructure.Configuration;
using Microsoft.Extensions.Logging;
using NSubstitute;
using Xunit;

namespace Githubie.Server.Tests;

public sealed class WindowsOwnerAuthenticationTests
{
    [Fact]
    public void OwnerContract_CoversEveryRegisteredRepositoryToolIncludingRegistrationAndManagement()
    {
        var names = typeof(GithubieMcpTools).GetMethods()
            .Select(method => System.Reflection.CustomAttributeExtensions.GetCustomAttribute<ModelContextProtocol.Server.McpServerToolAttribute>(method)?.Name)
            .Where(name => name?.StartsWith("github_", StringComparison.Ordinal) == true)
            .Select(name => name!).Order(StringComparer.Ordinal);
        GithubieAssertionPolicy.OperationNames.Should().Equal(names);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Capability_ExplicitlyDistinguishesOwnerModeFromLegacy(bool owner)
    {
        var repositories = new Dictionary<string, RepositoryOptions> { ["sample"] = Repository(Path.GetTempPath()) };
        var options = new GithubieOptions(45460, "/mcp", repositories) { WindowsOwnerSid = owner ? "S-1-5-21-test" : null };
        var tools = new GithubieMcpTools(Substitute.For<IGitGateway>(), Substitute.For<IGitHubRepositoryGateway>(),
            Substitute.For<IRepositoryRegistrationService>(), Substitute.For<IRepositoryManagementService>(),
            new RepositoryAllowlist(repositories), options);
        var result = await tools.GetProviderCapabilitiesAsync("sample", TestContext.Current.CancellationToken);
        result.Ok.Should().BeTrue();
        var authentication = result.Data!.Authentication!;
        if (!owner) { authentication.OwnerAuthentication.Should().BeNull(); return; }
        authentication.IntegrationMode.Should().Be("windows_owner");
        authentication.DirectConnection.Should().Be("unrestricted");
        var contract = authentication.OwnerAuthentication!;
        contract.Version.Should().Be(1);
        contract.Transport.Should().Be("user_host_schema1");
        contract.Principal.Should().Be("windows_owner");
        contract.AssertionRequired.Should().BeFalse();
        contract.ProjectBindingRequired.Should().BeFalse();
        contract.LegacyTokenRequired.Should().BeFalse();
        contract.CredentialHeader.Should().Be(UserHostProtocol.CredentialHeader);
        contract.GenerationHeader.Should().Be(UserHostProtocol.GenerationHeader);
        contract.Operations.Should().Contain(["github_repository_commit", "github_push", "github_branch_switch",
            "github_local_branch_create", "github_repository_settings_update", "github_pr_create", "github_pr_merge",
            "github_release_create", "github_release_publish", "github_release_asset_upload", "github_release_withdraw"]);
        contract.Operations.Should().OnlyHaveUniqueItems();
        JsonSerializer.Serialize(contract).Should().NotContain("S-1-5-21-test");
    }

    [Fact]
    public void Audit_UsesConfiguredWindowsOwnerAndDoesNotTrustCallerIdentity()
    {
        var logger = new RecordingLogger();
        var options = new GithubieOptions(45460, "/mcp", new Dictionary<string, RepositoryOptions>()) { WindowsOwnerSid = "S-1-5-21-owner" };
        new GithubieAuditLogger(logger, options).Write(new("mcp", "github_push", "sample", "main", null, null, "denied", 1, "policy"));
        logger.Message.Should().Contain("actor_windows_sid=S-1-5-21-owner").And.Contain("authentication_mode=windows_owner");
    }

    [Fact]
    public async Task LegacyService_CannotActivateOwnerMode()
    {
        var path = Path.GetTempFileName();
        try
        {
            await File.WriteAllTextAsync(path, """{"mcp_port":45460,"mcp_path":"/mcp","repositories":{},"user_host":{"authentication_mode":"windows_owner"}}""",
                TestContext.Current.CancellationToken);
            var result = await GithubieCompositionRoot.BuildAsync(path, AppContext.BaseDirectory, TestContext.Current.CancellationToken);
            result.IsSuccess.Should().BeFalse();
            result.Errors.Should().Contain(error => error.Contains("requires the authenticated user host", StringComparison.Ordinal));
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public async Task OwnerHost_RequiresCredentialAndGenerationWithoutIssuerAndRejectsLegacyBearer()
    {
        var root = Path.Combine(Path.GetTempPath(), "Githubie-owner-" + Guid.NewGuid().ToString("N"));
        var layout = GithubiePathLayout.FromUserProfileRoot(AppContext.BaseDirectory, root);
        var identity = await new UserProfileStore(layout).InitializeAsync(TestContext.Current.CancellationToken);
        using var reservation = new TcpListener(IPAddress.Loopback, 0);
        reservation.Start();
        var port = ((IPEndPoint)reservation.LocalEndpoint).Port;
        reservation.Stop();
        var options = new GithubieOptions(port, "/mcp", new Dictionary<string, RepositoryOptions> { ["sample"] = Repository(Path.Combine(root, "repo")) })
        { UserHost = new(MoyaiIntegration: true, AuthenticationMode: "windows_owner") };
        await using (var file = File.Create(Path.Combine(layout.ConfigDirectory, "githubie.json")))
            await new JsonGithubieOptionsLoader().SaveAsync(options, file, TestContext.Current.CancellationToken);
        using var lifetime = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        lifetime.CancelAfter(TimeSpan.FromSeconds(60));
        var running = HostRuntime.RunAsync(["--user-host"], lifetime.Token, layout);
        var connections = new UserHostConnectionStore(layout.DataDirectory, Environment.ProcessPath);
        try
        {
            UserHostConnection? connection = null;
            var deadline = DateTime.UtcNow.AddSeconds(25);
            while (DateTime.UtcNow < deadline && !running.IsCompleted)
            {
                try { connection = connections.Read(identity); break; }
                catch (Exception ex) when (ex is IOException or ArgumentException) { }
                await Task.Delay(50, TestContext.Current.CancellationToken);
            }
            connection.Should().NotBeNull();
            using var client = new HttpClient(new HttpClientHandler { UseProxy = false, AllowAutoRedirect = false });
            using var allowed = await Call(client, connection!, connection!.Credential, connection.Generation);
            allowed.StatusCode.Should().Be(HttpStatusCode.OK);
            var body = await allowed.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
            body.Should().Contain("windows_owner").And.Contain("owner_authentication").And.Contain("github_release_publish")
                .And.NotContain(connection.Credential).And.NotContain("auth_project_mismatch");
            foreach (var pair in new[] { (Credential: (string?)null, Generation: connection.Generation),
                (Credential: "wrong", Generation: connection.Generation), (Credential: connection.Credential, Generation: "stale") })
            {
                using var denied = await Call(client, connection, pair.Credential, pair.Generation);
                denied.StatusCode.Should().Be(HttpStatusCode.Forbidden);
                denied.Headers.GetValues(UserHostProtocol.ErrorHeader).Should().ContainSingle().Which.Should().Be(UserHostProtocol.OwnerRejected);
            }
            using var bearer = await Call(client, connection, connection.Credential, connection.Generation, bearer: true);
            bearer.StatusCode.Should().Be(HttpStatusCode.Forbidden);
            bearer.Headers.GetValues(UserHostProtocol.ErrorHeader).Single().Should().Be(UserHostProtocol.AuthenticationModeMismatch);
            var stopped = await UserHostControlPipe.SendAsync(connection, "stop", null, TestContext.Current.CancellationToken);
            stopped.Success.Should().BeTrue();
            (await running).Should().Be(0);
            File.Exists(Path.Combine(layout.DataDirectory, "connection.json")).Should().BeFalse();
        }
        finally
        {
            lifetime.Cancel();
            try { await running; } catch (OperationCanceledException) { }
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        }
    }

    private static RepositoryOptions Repository(string root) => new("owner", "repo", root, "origin", "develop", "main",
        [], [], ["main"], "main", "^v", "merge", true);

    private static Task<HttpResponseMessage> Call(HttpClient client, UserHostConnection connection, string? credential, string generation, bool bearer = false)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, connection.Endpoint)
        { Content = new StringContent("""{"jsonrpc":"2.0","id":1,"method":"tools/call","params":{"name":"github_provider_capabilities","arguments":{"repository":"sample"}}}""", Encoding.UTF8, "application/json") };
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("text/event-stream"));
        if (credential is not null) request.Headers.Add(UserHostProtocol.CredentialHeader, credential);
        request.Headers.Add(UserHostProtocol.GenerationHeader, generation);
        if (bearer) request.Headers.Authorization = new("Bearer", "legacy-token-not-accepted");
        return client.SendAsync(request, TestContext.Current.CancellationToken);
    }

    private sealed class RecordingLogger : ILogger<GithubieAuditLogger>
    {
        public string Message { get; private set; } = string.Empty;
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel logLevel) => true;
        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
            => Message = formatter(state, exception);
    }
}
