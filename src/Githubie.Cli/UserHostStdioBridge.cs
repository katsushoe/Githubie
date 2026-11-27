using System.Collections.Concurrent;
using System.Text;
using System.Text.Json;
using Githubie.Infrastructure.Configuration;

namespace Githubie.Cli;

/// <summary>JSON-RPCのID・通知・キャンセルを保ったまま、現在セッションのHTTPホストへ中継します。</summary>
internal static class UserHostStdioBridge
{
    public static async Task<int> RunAsync(UserHostConnection connection, UserHostMcpClient client,
        TextReader input, TextWriter output, CancellationToken cancellationToken)
    {
        using var lifetime = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        using var outputLock = new SemaphoreSlim(1, 1);
        var active = new ConcurrentDictionary<string, CancellationTokenSource>(StringComparer.Ordinal);
        var pending = new List<Task>();
        async Task Emit(string json)
        {
            await outputLock.WaitAsync(lifetime.Token);
            try { await output.WriteLineAsync(json); await output.FlushAsync(lifetime.Token); }
            finally { outputLock.Release(); }
        }
        try
        {
            string? line;
            while ((line = await input.ReadLineAsync(lifetime.Token)) is not null)
            {
                if (line.Length == 0) continue;
                if (Encoding.UTF8.GetByteCount(line) > 1_048_576) throw new IOException("MCP request is too large.");
                using var document = JsonDocument.Parse(line);
                var root = document.RootElement;
                var method = root.GetProperty("method").GetString();
                if (method == "notifications/cancelled")
                {
                    if (root.TryGetProperty("params", out var parameters) && parameters.TryGetProperty("requestId", out var cancelled)
                        && cancelled.ValueKind is JsonValueKind.String or JsonValueKind.Number)
                    {
                        lock (active)
                            if (active.TryGetValue(cancelled.GetRawText(), out var requestLifetime)) requestLifetime.Cancel();
                    }
                    continue;
                }
                var id = root.TryGetProperty("id", out var idElement) ? idElement.Clone() : (JsonElement?)null;
                var key = id?.GetRawText() ?? "notification/" + Guid.NewGuid().ToString("N");
                var requestCancellation = CancellationTokenSource.CreateLinkedTokenSource(lifetime.Token);
                if (active.Count >= 64 || !active.TryAdd(key, requestCancellation))
                {
                    requestCancellation.Dispose();
                    if (id is not null) await Emit(Error(id, -32600, "Duplicate request ID or too many pending requests."));
                    continue;
                }
                pending.RemoveAll(task => task.IsCompletedSuccessfully);
                pending.Add(ForwardAsync(line, id, key, requestCancellation));
            }
            // EOF ends the bridge session. Completed replies were already flushed; outstanding host work is cancelled.
        }
        finally
        {
            lifetime.Cancel();
            await Task.WhenAll(pending);
        }
        return 0;

        async Task ForwardAsync(string json, JsonElement? id, string key, CancellationTokenSource requestLifetime)
        {
            try { await client.StreamAsync(connection, json, Emit, requestLifetime.Token); }
            catch (OperationCanceledException) when (lifetime.IsCancellationRequested) { }
            catch (Exception exception) when (exception is HttpRequestException or IOException or JsonException or OperationCanceledException)
            {
                if (id is not null && !lifetime.IsCancellationRequested)
                    await Emit(Error(id, exception is OperationCanceledException ? -32800 : -32603,
                        exception is OperationCanceledException ? "Request cancelled." : "User host request failed. Inspect state before retrying a mutation."));
            }
            finally
            {
                lock (active) { active.TryRemove(key, out _); requestLifetime.Dispose(); }
            }
        }
    }

    private static string Error(JsonElement? id, int code, string message) => JsonSerializer.Serialize(new
    { jsonrpc = "2.0", id, error = new { code, message } });
}
