using System.Net;
using System.Text;
using System.Text.Json;
using System.Threading.Channels;
using FluentAssertions;
using Githubie.Infrastructure.Configuration;
using Xunit;

namespace Githubie.Cli.Tests;

public sealed class UserHostTransportTests
{
    private static UserHostConnection Connection => new(Guid.NewGuid(), "test-sid", 1, 123, 456,
        "http://127.0.0.1:45460/mcp", "session-secret-never-print");

    [Fact]
    public async Task Initialize_NegotiatedProtocolIsUsedForSubsequentRequests()
    {
        var requests = 0;
        using var factory = new Factory((request, _) =>
        {
            requests++;
            if (requests == 1)
            {
                request.Headers.Contains("MCP-Protocol-Version").Should().BeFalse();
                return Task.FromResult(JsonResponse("{\"jsonrpc\":\"2.0\",\"id\":1,\"result\":{\"protocolVersion\":\"2025-11-25\"}}"));
            }
            request.Headers.GetValues("MCP-Protocol-Version").Single().Should().Be("2025-11-25");
            return Task.FromResult(JsonResponse("{\"jsonrpc\":\"2.0\",\"id\":2,\"result\":{}}"));
        });
        var client = new UserHostMcpClient(factory);
        await client.SendAsync(Connection, "{\"jsonrpc\":\"2.0\",\"id\":1,\"method\":\"initialize\"}", TestContext.Current.CancellationToken);
        await client.SendAsync(Connection, "{\"jsonrpc\":\"2.0\",\"id\":2,\"method\":\"tools/list\"}", TestContext.Current.CancellationToken);
        requests.Should().Be(2);
    }

    [Fact]
    public async Task SseTransport_ForwardsNotificationsAndResponseWithoutPrintingCredential()
    {
        using var factory = new Factory((request, _) =>
        {
            request.Headers.GetValues("X-Githubie-Session").Single().Should().Be(Connection.Credential);
            var sse = "event: message\ndata: {\"jsonrpc\":\"2.0\",\"method\":\"notifications/message\",\"params\":{}}\n\nevent: message\ndata: {\"jsonrpc\":\"2.0\",\"id\":1,\"result\":{\"ok\":true}}\n\n";
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(sse, Encoding.UTF8, "text/event-stream") });
        });
        var frames = new List<string>();
        await new UserHostMcpClient(factory).StreamAsync(Connection, "{\"jsonrpc\":\"2.0\",\"id\":1,\"method\":\"tools/call\",\"params\":{}}",
            frame => { frames.Add(frame); return Task.CompletedTask; }, TestContext.Current.CancellationToken);
        frames.Should().HaveCount(2);
        frames[0].Should().Contain("notifications/message");
        frames[1].Should().Contain("\"id\":1");
        string.Join('\n', frames).Should().NotContain(Connection.Credential);
    }

    [Fact]
    public async Task StdioBridge_KeepsRequestIdAndStandardJsonRpcOutput()
    {
        using var factory = new Factory(async (request, cancellationToken) =>
        {
            using var document = JsonDocument.Parse(await request.Content!.ReadAsStringAsync(cancellationToken));
            var id = document.RootElement.GetProperty("id").Clone();
            return JsonResponse(JsonSerializer.Serialize(new { jsonrpc = "2.0", id, result = new { ok = true } }));
        });
        var input = new QueuedReader();
        input.Add("{\"jsonrpc\":\"2.0\",\"id\":\"client-42\",\"method\":\"tools/list\"}");
        using var output = new CompletingWriter(input);
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(10));
        (await UserHostStdioBridge.RunAsync(Connection, new UserHostMcpClient(factory), input, output, timeout.Token)).Should().Be(0);
        using var parsed = JsonDocument.Parse(output.ToString());
        parsed.RootElement.GetProperty("id").GetString().Should().Be("client-42");
        output.ToString().Should().NotContain(Connection.Credential).And.NotContain("ready");
    }

    [Fact]
    public async Task StdioCancellation_CancelsPendingHttpRequestAndReturnsCancellationError()
    {
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var factory = new Factory(async (_, cancellationToken) =>
        {
            started.TrySetResult();
            await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            return JsonResponse("{}");
        });
        var input = new QueuedReader();
        input.Add("{\"jsonrpc\":\"2.0\",\"id\":9,\"method\":\"tools/call\",\"params\":{}}");
        using var output = new CompletingWriter(input);
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(10));
        var bridge = UserHostStdioBridge.RunAsync(Connection, new UserHostMcpClient(factory), input, output, timeout.Token);
        await started.Task.WaitAsync(timeout.Token);
        input.Add("{\"jsonrpc\":\"2.0\",\"method\":\"notifications/cancelled\",\"params\":{\"requestId\":9}}");
        (await bridge).Should().Be(0);
        using var parsed = JsonDocument.Parse(output.ToString());
        parsed.RootElement.GetProperty("id").GetInt32().Should().Be(9);
        parsed.RootElement.GetProperty("error").GetProperty("code").GetInt32().Should().Be(-32800);
    }

    private static HttpResponseMessage JsonResponse(string body) => new(HttpStatusCode.OK)
    { Content = new StringContent(body, Encoding.UTF8, "application/json") };

    [Fact]
    public async Task MetadataOwnerRejection_RefreshesCredentialOnceAndForwardsGeneration()
    {
        var calls = 0;
        var resolutions = 0;
        var refreshed = Connection with { Credential = "rotated-secret", Generation = "new-generation" };
        using var factory = new Factory((request, _) =>
        {
            calls++;
            if (calls == 1) return Task.FromResult(OwnerRejected());
            request.Headers.GetValues("X-Githubie-Session").Single().Should().Be(refreshed.Credential);
            request.Headers.GetValues("X-Githubie-Generation").Single().Should().Be(refreshed.Generation);
            return Task.FromResult(JsonResponse("{\"jsonrpc\":\"2.0\",\"id\":1,\"result\":{}}"));
        });
        var client = new UserHostMcpClient(factory, () => { resolutions++; return refreshed; });
        var result = await client.SendAsync(Connection, "{\"jsonrpc\":\"2.0\",\"id\":1,\"method\":\"tools/list\"}", TestContext.Current.CancellationToken);
        calls.Should().Be(2);
        resolutions.Should().Be(1);
        result.Should().NotContain(refreshed.Credential);
    }

    [Theory]
    [InlineData("tools/list", 2, 1)]
    [InlineData("tools/call", 1, 0)]
    public async Task RepeatedOwnerRejection_IsBoundedAndNeverReplaysMutation(string method, int attempts, int refreshes)
    {
        var calls = 0;
        var resolutions = 0;
        using var factory = new Factory((_, _) => { calls++; return Task.FromResult(OwnerRejected()); });
        var client = new UserHostMcpClient(factory, () => { resolutions++; return Connection; });
        var send = () => client.SendAsync(Connection,
            JsonSerializer.Serialize(new { jsonrpc = "2.0", id = 1, method, @params = new { name = "github_repository_commit" } }),
            TestContext.Current.CancellationToken);
        await send.Should().ThrowAsync<HttpRequestException>().WithMessage("user_host_owner_rejected");
        calls.Should().Be(attempts);
        resolutions.Should().Be(refreshes);
    }

    [Fact]
    public async Task TransportFailure_NeverRefreshesOrReplaysMutation()
    {
        var calls = 0;
        var resolutions = 0;
        using var factory = new Factory((_, _) => { calls++; throw new HttpRequestException("dispatch outcome unknown"); });
        var client = new UserHostMcpClient(factory, () => { resolutions++; return Connection; });
        var send = () => client.SendAsync(Connection,
            "{\"jsonrpc\":\"2.0\",\"id\":1,\"method\":\"tools/call\",\"params\":{\"name\":\"github_repository_commit\"}}",
            TestContext.Current.CancellationToken);
        await send.Should().ThrowAsync<HttpRequestException>();
        calls.Should().Be(1);
        resolutions.Should().Be(0);
    }

    private static HttpResponseMessage OwnerRejected()
    {
        var response = new HttpResponseMessage(HttpStatusCode.Forbidden);
        response.Headers.Add("X-Githubie-Error", "user_host_owner_rejected");
        return response;
    }

    private sealed class Factory(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> respond) : IHttpClientFactory, IDisposable
    {
        private readonly Handler _handler = new(respond);
        public HttpClient CreateClient(string name) => new(_handler, disposeHandler: false) { Timeout = Timeout.InfiniteTimeSpan };
        public void Dispose() => _handler.Dispose();
    }
    private sealed class Handler(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> respond) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) => respond(request, cancellationToken);
    }
    private sealed class QueuedReader : TextReader
    {
        private readonly Channel<string> _lines = Channel.CreateUnbounded<string>();
        public void Add(string line) => _lines.Writer.TryWrite(line);
        public void Complete() => _lines.Writer.TryComplete();
        public override async ValueTask<string?> ReadLineAsync(CancellationToken cancellationToken)
        {
            while (await _lines.Reader.WaitToReadAsync(cancellationToken))
                if (_lines.Reader.TryRead(out var line)) return line;
            return null;
        }
    }
    private sealed class CompletingWriter(QueuedReader input) : StringWriter
    {
        public override Task WriteLineAsync(string? value)
        {
            WriteLine(value);
            input.Complete();
            return Task.CompletedTask;
        }
    }
}
