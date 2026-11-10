using FluentAssertions;
using Githubie.Cli;
using Xunit;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;

namespace Githubie.Cli.Tests;

public sealed class GithubieMcpClientTests
{
    [Theory]
    [InlineData(true, 0)]
    [InlineData(false, 1)]
    public async Task McpCall_ForwardsArgumentsAndServerOutcome(bool success, int expectedExit)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(30));
        using var reservation = new TcpListener(IPAddress.Loopback, 0);
        reservation.Start();
        var port = ((IPEndPoint)reservation.LocalEndpoint).Port;
        reservation.Stop();
        using var listener = new HttpListener();
        listener.Prefixes.Add($"http://127.0.0.1:{port}/mcp/");
        listener.Start();
        var config = Path.Combine(Path.GetTempPath(), $"githubiemcp-test-{Guid.NewGuid():N}.json");
        await File.WriteAllTextAsync(config, JsonSerializer.Serialize(new
        {
            mcp_port = port, mcp_path = "/mcp/", repositories = new { },
            provider_authentication = new { issuer = "moyai:test", trust_bundle_path = Path.Combine(Path.GetTempPath(), "moyai-trust.json"), replay_database_path = Path.Combine(Path.GetTempPath(), "moyai-replay.db"), projects = new { sample = "11111111-1111-1111-1111-111111111111" } },
        }), timeout.Token);
        try
        {
            using var output = new StringWriter();
            using var error = new StringWriter();
            var incoming = listener.GetContextAsync();
            var execution = GithubieMcpClient.RunIsolatedAsync(["--config", config, "call", "github_branch_get", "{\"repository\":\"sample\",\"branch\":\"feature/example\"}"], output, error, timeout.Token);
            var context = await incoming.WaitAsync(timeout.Token);
            using var reader = new StreamReader(context.Request.InputStream);
            using var request = JsonDocument.Parse(await reader.ReadToEndAsync(timeout.Token));
            var parameters = request.RootElement.GetProperty("params");
            parameters.GetProperty("name").GetString().Should().Be("github_branch_get");
            parameters.GetProperty("arguments").GetProperty("branch").GetString().Should().Be("feature/example");
            var response = Encoding.UTF8.GetBytes(success
                ? "{\"jsonrpc\":\"2.0\",\"id\":1,\"result\":{\"structuredContent\":{\"ok\":true}}}"
                : "{\"jsonrpc\":\"2.0\",\"id\":1,\"result\":{\"isError\":true}}");
            context.Response.ContentType = "application/json";
            context.Response.ContentLength64 = response.Length;
            await context.Response.OutputStream.WriteAsync(response, timeout.Token);
            context.Response.Close();
            (await execution).Should().Be(expectedExit);
            output.ToString().Should().Contain("jsonrpc");
        }
        finally { File.Delete(config); }
    }

    [Fact]
    public async Task Help_DescribesClientAndCanonicalConfiguration()
    {
        using var output = new StringWriter();
        using var error = new StringWriter();
        var exit = await GithubieMcpClient.RunAsync(["--help"], output, error, TestContext.Current.CancellationToken);
        exit.Should().Be(0);
        output.ToString().Should().Contain("does not start the server").And.Contain("config/githubie.json").And.Contain("interactive UI");
        error.ToString().Should().BeEmpty();
    }

    [Theory]
    [InlineData("start")]
    [InlineData("stop")]
    [InlineData("restart")]
    [InlineData("host")]
    [InlineData("service")]
    public async Task ServerManagement_IsRejectedWithoutLaunching(string command)
    {
        using var output = new StringWriter();
        using var error = new StringWriter();
        var exit = await GithubieMcpClient.RunAsync([command], output, error, TestContext.Current.CancellationToken);
        exit.Should().Be(1);
        error.ToString().Should().Contain("cannot start or manage the server");
    }

    [Fact]
    public async Task NonCanonicalConfiguration_IsRejectedThroughExistingCli()
    {
        using var output = new StringWriter();
        using var error = new StringWriter();
        var path = Path.Combine(Path.GetTempPath(), "not-the-installation", "githubie.json");
        var exit = await GithubieMcpClient.RunAsync(["--config", path, "call", "get_version"], output, error, TestContext.Current.CancellationToken);
        exit.Should().Be(1);
        error.ToString().Should().NotBeEmpty();
    }

    [Fact]
    public async Task Version_MatchesExistingCli()
    {
        using var output = new StringWriter();
        using var expected = new StringWriter();
        using var error = new StringWriter();
        var token = TestContext.Current.CancellationToken;
        var actualCode = await GithubieMcpClient.RunAsync(["--version"], output, error, token);
        var expectedCode = await CliApplication.RunAsync(["version"], expected, error, token);
        actualCode.Should().Be(expectedCode);
        output.ToString().Should().Be(expected.ToString());
    }
}
