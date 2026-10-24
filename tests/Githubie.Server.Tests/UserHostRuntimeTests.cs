using System.Net;
using System.Net.Http.Headers;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using FluentAssertions;
using Githubie.Application.Configuration;
using Githubie.Infrastructure.Configuration;
using Xunit;

namespace Githubie.Server.Tests;

public sealed class UserHostRuntimeTests
{
    [Fact]
    public async Task FreshProfile_IsolatesDataRejectsUncredentialedCallsAndRevokesOnShutdown()
    {
        var root = Path.Combine(Path.GetTempPath(), "Githubie-user-host-" + Guid.NewGuid().ToString("N"));
        var layout = GithubiePathLayout.FromUserProfileRoot(AppContext.BaseDirectory, root);
        var profiles = new UserProfileStore(layout);
        var identity = await profiles.InitializeAsync(TestContext.Current.CancellationToken);
        using var reservation = new TcpListener(IPAddress.Loopback, 0);
        reservation.Start();
        var port = ((IPEndPoint)reservation.LocalEndpoint).Port;
        reservation.Stop();
        await File.WriteAllTextAsync(Path.Combine(layout.ConfigDirectory, "githubie.json"), JsonSerializer.Serialize(new
        { mcp_port = port, mcp_path = "/mcp", repositories = new { } }), TestContext.Current.CancellationToken);
        using var lifetime = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        lifetime.CancelAfter(TimeSpan.FromSeconds(40));
        var running = HostRuntime.RunAsync(["--user-host"], lifetime.Token, layout);
        var connections = new UserHostConnectionStore(layout.DataDirectory);
        try
        {
            UserHostConnection? connection = null;
            var deadline = DateTime.UtcNow.AddSeconds(20);
            while (DateTime.UtcNow < deadline && !running.IsCompleted)
            {
                try { connection = connections.Read(identity); break; }
                catch (Exception exception) when (exception is IOException or ArgumentException) { }
                await Task.Delay(50, TestContext.Current.CancellationToken);
            }
            connection.Should().NotBeNull("the isolated host must publish protected discovery after binding");
            connection!.ProfileId.Should().Be(identity.ProfileId);
            connection.ToString().Should().NotContain(connection.Credential);
            using var client = new HttpClient(new HttpClientHandler { UseProxy = false });
            using var rejected = await Call(client, connection, "list_projects", authenticated: false);
            rejected.StatusCode.Should().Be(HttpStatusCode.Forbidden);
            using var allowed = await Call(client, connection, "list_projects", authenticated: true);
            allowed.StatusCode.Should().Be(HttpStatusCode.OK);
            (await allowed.Content.ReadAsStringAsync(TestContext.Current.CancellationToken)).Should().NotContain("repository_not_found");
            using var metadata = await Call(client, connection, "get_version", authenticated: false);
            metadata.StatusCode.Should().Be(HttpStatusCode.OK);
            var control = await UserHostControlPipe.SendAsync(connection, "status", null, TestContext.Current.CancellationToken);
            control.Success.Should().BeTrue();
            var duplicate = () => HostRuntime.RunAsync(["--user-host"], TestContext.Current.CancellationToken, layout);
            await duplicate.Should().ThrowAsync<IOException>();
            var stop = await UserHostControlPipe.SendAsync(connection, "stop", null, TestContext.Current.CancellationToken);
            stop.Success.Should().BeTrue();
            (await running).Should().Be(0);
            File.Exists(Path.Combine(layout.DataDirectory, "connection.dpapi")).Should().BeFalse();
            var repositories = await new Githubie.Infrastructure.Configuration.SqliteRepositoryConfigurationStore(layout.RepositoryDatabasePath)
                .LoadExistingAsync(TestContext.Current.CancellationToken);
            repositories.Should().BeEmpty("a fresh profile never imports machine repositories");
        }
        finally
        {
            lifetime.Cancel();
            try { await running; } catch (OperationCanceledException) { }
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        }
    }

    private static Task<HttpResponseMessage> Call(HttpClient client, UserHostConnection connection, string tool, bool authenticated)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, connection.Endpoint)
        {
            Content = new StringContent(JsonSerializer.Serialize(new { jsonrpc = "2.0", id = 1, method = "tools/call",
                @params = new { name = tool, arguments = new { } } }), Encoding.UTF8, "application/json"),
        };
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("text/event-stream"));
        if (authenticated) request.Headers.Add(UserHostRequestGate.CredentialHeader, connection.Credential);
        return client.SendAsync(request, TestContext.Current.CancellationToken);
    }
}
