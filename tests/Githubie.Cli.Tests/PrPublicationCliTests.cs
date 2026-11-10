using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using FluentAssertions;
using Xunit;

namespace Githubie.Cli.Tests;

public sealed class PrPublicationCliTests
{
    [Theory]
    [InlineData(false, 0)]
    [InlineData(true, 0)]
    [InlineData(false, 1)]
    [InlineData(true, 1)]
    public async Task PublicationCommands_ForwardExactPayloadAndServerOutcome(bool status, int expectedExit)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(30));
        using var portReservation = new TcpListener(IPAddress.Loopback, 0);
        portReservation.Start();
        var port = ((IPEndPoint)portReservation.LocalEndpoint).Port;
        portReservation.Stop();
        using var listener = new HttpListener();
        listener.Prefixes.Add($"http://127.0.0.1:{port}/mcp/");
        listener.Start();
        var config = Path.Combine(Path.GetTempPath(), $"githubie-source-{Guid.NewGuid():N}.json");
        await File.WriteAllTextAsync(config, JsonSerializer.Serialize(new
        {
            mcp_port = port, mcp_path = "/mcp/", provider_authentication = new { issuer = "moyai:test", trust_bundle_path = Path.Combine(Path.GetTempPath(), "moyai-trust.json"), replay_database_path = Path.Combine(Path.GetTempPath(), "moyai-replay.db"), projects = new { sample = "11111111-1111-1111-1111-111111111111" } }, repositories = new { }
        }), timeout.Token);
        try
        {
            string id = Guid.NewGuid().ToString("D");
            string tool = status ? "github_pr_publish_status" : "github_pr_publish_create";
            var arguments = new Dictionary<string, string> { ["repository"] = "sample", ["operation_id"] = id };
            if (!status)
            {
                arguments["source_branch"] = "work/topic";
                arguments["destination_branch"] = "review-target";
                arguments["expected_source_sha"] = new string('a', 40);
                arguments["title"] = "Explicit publication";
            }
            var payload = config + ".arguments.json";
            await File.WriteAllTextAsync(payload, JsonSerializer.Serialize(arguments), timeout.Token);
            var incoming = listener.GetContextAsync();
            using var output = new StringWriter();
            using var error = new StringWriter();
            var execution = CliApplication.RunIsolatedAsync(
                status ? ["--config", config, "pr", "publish-status", "sample", id]
                    : ["--config", config, "pr", "publish-create", "--file", payload],
                output, error, timeout.Token);
            var context = await incoming.WaitAsync(timeout.Token);
            using var reader = new StreamReader(context.Request.InputStream);
            using var request = JsonDocument.Parse(await reader.ReadToEndAsync(timeout.Token));
            var parameters = request.RootElement.GetProperty("params");
            parameters.GetProperty("name").GetString().Should().Be(tool);
            var actual = parameters.GetProperty("arguments");
            actual.EnumerateObject().Select(x => x.Name).Should().BeEquivalentTo(arguments.Keys);
            foreach (var pair in arguments) actual.GetProperty(pair.Key).GetString().Should().Be(pair.Value);
            var response = Encoding.UTF8.GetBytes(expectedExit == 0
                ? "{\"jsonrpc\":\"2.0\",\"id\":1,\"result\":{\"structuredContent\":{\"ok\":true}}}"
                : "{\"jsonrpc\":\"2.0\",\"id\":1,\"result\":{\"isError\":true}}");
            context.Response.ContentType = "application/json";
            context.Response.ContentLength64 = response.Length;
            await context.Response.OutputStream.WriteAsync(response, timeout.Token);
            context.Response.Close();

            (await execution).Should().Be(expectedExit);
        }
        finally
        {
            File.Delete(config);
            File.Delete(config + ".arguments.json");
        }
    }
}
