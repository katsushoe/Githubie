using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using FluentAssertions;
using Xunit;

namespace Githubie.Cli.Tests;

public sealed class TagSourceCliTests
{
    [Theory]
    [InlineData("develop", 0)]
    [InlineData("1111111111111111111111111111111111111111", 0)]
    [InlineData(null, 1)]
    [InlineData("", 1)]
    public async Task McpCall_ForwardsExplicitSourceAndServerOutcome(string? source, int expectedExit)
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
        var config = Path.Combine(Path.GetTempPath(), $"githubie-tag-source-{Guid.NewGuid():N}.json");
        await File.WriteAllTextAsync(config, JsonSerializer.Serialize(new { mcp_port = port, mcp_path = "/mcp/", repositories = new { } }), timeout.Token);
        try
        {
            var arguments = new Dictionary<string, string> { ["repository"] = "sample", ["tag"] = "v1.2.3.4" };
            if (source is not null) arguments["source"] = source;
            var incoming = listener.GetContextAsync();
            using var output = new StringWriter();
            using var error = new StringWriter();
            var execution = CliApplication.RunAsync(
                ["--config", config, "mcp", "call", "github_tag_create", JsonSerializer.Serialize(arguments)], output, error, timeout.Token);
            var context = await incoming.WaitAsync(timeout.Token);
            using var reader = new StreamReader(context.Request.InputStream);
            using var request = JsonDocument.Parse(await reader.ReadToEndAsync(timeout.Token));
            var actual = request.RootElement.GetProperty("params").GetProperty("arguments");
            actual.TryGetProperty("source", out var value).Should().Be(source is not null);
            if (source is not null) value.GetString().Should().Be(source);
            var response = Encoding.UTF8.GetBytes(expectedExit == 0
                ? "{\"jsonrpc\":\"2.0\",\"id\":1,\"result\":{\"structuredContent\":{\"ok\":true}}}"
                : "{\"jsonrpc\":\"2.0\",\"id\":1,\"result\":{\"isError\":true}}");
            context.Response.ContentLength64 = response.Length;
            await context.Response.OutputStream.WriteAsync(response, timeout.Token);
            context.Response.Close();
            (await execution).Should().Be(expectedExit);
        }
        finally
        {
            File.Delete(config);
        }
    }
}
