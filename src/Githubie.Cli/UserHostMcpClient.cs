using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Githubie.Infrastructure.Configuration;
using Githubie.Application.Configuration;

namespace Githubie.Cli;

/// <summary>接続資格を内部HTTPヘッダーだけに付与し、JSON/SSEの応答を転送します。</summary>
internal sealed class UserHostMcpClient(IHttpClientFactory factory, Func<UserHostConnection>? resolve = null)
{
    private string? _protocolVersion;

    public async Task<string?> SendAsync(UserHostConnection connection, string json, CancellationToken cancellationToken)
    {
        string? result = null;
        await StreamAsync(connection, json, frame =>
        {
            using var document = JsonDocument.Parse(frame);
            if (document.RootElement.TryGetProperty("id", out _)) result = frame;
            return Task.CompletedTask;
        }, cancellationToken);
        return result;
    }

    public async Task StreamAsync(UserHostConnection connection, string json, Func<string, Task> emit, CancellationToken cancellationToken)
    {
        var emitted = false;
        async Task Forward(string frame) { emitted = true; await emit(frame); }
        try { await StreamOnceAsync(connection, json, Forward, cancellationToken); }
        catch (OwnerRejectedException) when (resolve is not null && !emitted && IsMetadataRead(json))
        {
            // 資格拒否が確認できたメタデータ要求だけ一度再検出します。再帰/書込再送はしません。
            var refreshed = resolve();
            Volatile.Write(ref _protocolVersion, null);
            await StreamOnceAsync(refreshed, json, emit, cancellationToken);
        }
    }

    private async Task StreamOnceAsync(UserHostConnection connection, string json, Func<string, Task> emit, CancellationToken cancellationToken)
    {
        using var document = JsonDocument.Parse(json);
        var method = document.RootElement.GetProperty("method").GetString() ?? throw new JsonException();
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        var duration = CliApplication.ResolveMcpTimeout(method);
        if (duration != Timeout.InfiniteTimeSpan) timeout.CancelAfter(duration);
        using var client = factory.CreateClient("UserHost");
        using var request = new HttpRequestMessage(HttpMethod.Post, connection.Endpoint)
        { Content = new StringContent(json, Encoding.UTF8, "application/json") };
        request.Headers.Add(UserHostProtocol.CredentialHeader, connection.Credential);
        if (connection.Generation.Length != 0) request.Headers.Add(UserHostProtocol.GenerationHeader, connection.Generation);
        if (method != "initialize" && Volatile.Read(ref _protocolVersion) is { } protocolVersion)
            request.Headers.Add("MCP-Protocol-Version", protocolVersion);
        async Task EmitAsync(string frame)
        {
            if (method == "initialize")
            {
                using var initialized = JsonDocument.Parse(frame);
                if (initialized.RootElement.TryGetProperty("result", out var result)
                    && result.TryGetProperty("protocolVersion", out var negotiated)
                    && negotiated.ValueKind == JsonValueKind.String
                    && DateOnly.TryParseExact(negotiated.GetString(), "yyyy-MM-dd",
                        System.Globalization.CultureInfo.InvariantCulture,
                        System.Globalization.DateTimeStyles.None, out _))
                    Volatile.Write(ref _protocolVersion, negotiated.GetString());
            }
            await emit(frame);
        }
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("text/event-stream"));
        using var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, timeout.Token);
        if (response.StatusCode == System.Net.HttpStatusCode.Forbidden
            && response.Headers.TryGetValues(UserHostProtocol.ErrorHeader, out var codes)
            && codes.SequenceEqual([UserHostProtocol.OwnerRejected])) throw new OwnerRejectedException();
        response.EnsureSuccessStatusCode();
        if (response.Content.Headers.ContentType?.MediaType != "text/event-stream")
        {
            var body = await response.Content.ReadAsStringAsync(timeout.Token);
            if (!string.IsNullOrWhiteSpace(body))
            {
                using var parsed = JsonDocument.Parse(body);
                await EmitAsync(parsed.RootElement.GetRawText());
            }
            return;
        }
        using var reader = new StreamReader(await response.Content.ReadAsStreamAsync(timeout.Token));
        var data = new StringBuilder();
        string? line;
        while ((line = await reader.ReadLineAsync(timeout.Token)) is not null)
        {
            if (line.Length == 0)
            {
                await EmitEventAsync(data, EmitAsync);
                continue;
            }
            if (!line.StartsWith("data:", StringComparison.Ordinal)) continue;
            if (data.Length > 0) data.Append('\n');
            data.Append(line.AsSpan(5).TrimStart());
            if (data.Length > 16_777_216) throw new IOException("MCP response exceeds the maximum frame size.");
        }
        await EmitEventAsync(data, EmitAsync);
    }

    private static bool IsMetadataRead(string json)
    {
        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;
        var method = root.GetProperty("method").GetString();
        return method is "initialize" or "ping" or "tools/list" or "prompts/list"
            || (method == "tools/call" && root.TryGetProperty("params", out var parameters)
                && parameters.TryGetProperty("name", out var name) && name.GetString() == "get_version");
    }

    private sealed class OwnerRejectedException() : HttpRequestException(UserHostProtocol.OwnerRejected,
        null, System.Net.HttpStatusCode.Forbidden) { }

    private static async Task EmitEventAsync(StringBuilder data, Func<string, Task> emit)
    {
        if (data.Length == 0) return;
        using var document = JsonDocument.Parse(data.ToString());
        data.Clear();
        await emit(document.RootElement.GetRawText());
    }
}
