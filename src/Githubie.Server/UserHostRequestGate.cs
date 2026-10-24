using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Http;

namespace Githubie.Server;

/// <summary>loopbackでもユーザー資格を要求します。Moyaiの例外は後段で検証される既知のToolだけです。</summary>
public static class UserHostRequestGate
{
    public const string CredentialHeader = Githubie.Application.Configuration.UserHostProtocol.CredentialHeader;
    private const int MaximumRequestBytes = 1_048_576;

    public static async Task<bool> IsAuthorizedAsync(HttpRequest request, string credential, int port,
        bool providerAuthenticationEnabled, CancellationToken cancellationToken)
    {
        var host = request.Host;
        if (host.Port != port || !(host.Host.Equals("localhost", StringComparison.OrdinalIgnoreCase)
            || host.Host == "127.0.0.1" || host.Host is "::1" or "[::1]")) return false;
        if (request.Headers.Origin.Count > 1 || !McpOriginValidator.IsAllowed(request.Headers.Origin.FirstOrDefault(), port)) return false;
        if (request.Headers[CredentialHeader].Count == 1)
        {
            var actual = Encoding.UTF8.GetBytes(request.Headers[CredentialHeader].ToString());
            var expected = Encoding.UTF8.GetBytes(credential);
            if (CryptographicOperations.FixedTimeEquals(actual, expected)) return true;
        }
        if (!HttpMethods.IsPost(request.Method) || request.ContentLength > MaximumRequestBytes) return false;
        request.EnableBuffering();
        try
        {
            using var stream = new MemoryStream();
            var buffer = new byte[8192];
            int read;
            while ((read = await request.Body.ReadAsync(buffer, cancellationToken)) != 0)
            {
                if (stream.Length + read > MaximumRequestBytes) return false;
                await stream.WriteAsync(buffer.AsMemory(0, read), cancellationToken);
            }
            using var document = JsonDocument.Parse(stream.ToArray());
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object || !root.TryGetProperty("method", out var methodElement)
                || methodElement.ValueKind != JsonValueKind.String) return false;
            var method = methodElement.GetString();
            if (method is "initialize" or "ping" or "tools/list" or "prompts/list" or "notifications/initialized") return true;
            if (method != "tools/call" || !root.TryGetProperty("params", out var parameters)
                || parameters.ValueKind != JsonValueKind.Object || !parameters.TryGetProperty("name", out var nameElement)
                || nameElement.ValueKind != JsonValueKind.String) return false;
            var tool = nameElement.GetString()!;
            if (tool == "get_version") return true;
            // Merely providing a Bearer header never grants access to an unvalidated Tool.
            return providerAuthenticationEnabled && request.Headers.Authorization.Count == 1
                && request.Headers.ContainsKey("X-Moyai-Operation-Id") && GithubieAssertionPolicy.TryGetScopes(tool, out _);
        }
        catch (JsonException) { return false; }
        finally { request.Body.Position = 0; }
    }
}
