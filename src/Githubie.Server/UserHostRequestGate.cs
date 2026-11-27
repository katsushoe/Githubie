using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.Http;

namespace Githubie.Server;

/// <summary>bootstrapやMoyai要求にも、検出済みユーザー資格と起動世代を要求します。</summary>
public static class UserHostRequestGate
{
    public const string CredentialHeader = Githubie.Application.Configuration.UserHostProtocol.CredentialHeader;

    public static Task<bool> IsAuthorizedAsync(HttpRequest request, string credential, int port,
        bool providerAuthenticationEnabled, CancellationToken cancellationToken, string? generation = null)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var host = request.Host;
        if (host.Port != port || !(host.Host.Equals("localhost", StringComparison.OrdinalIgnoreCase)
            || host.Host == "127.0.0.1" || host.Host is "::1" or "[::1]")) return Task.FromResult(false);
        if (request.Headers.Origin.Count > 1 || !McpOriginValidator.IsAllowed(request.Headers.Origin.FirstOrDefault(), port))
            return Task.FromResult(false);
        if (generation is not null && (request.Headers[Githubie.Application.Configuration.UserHostProtocol.GenerationHeader].Count != 1
            || request.Headers[Githubie.Application.Configuration.UserHostProtocol.GenerationHeader].ToString() != generation))
            return Task.FromResult(false);
        if (request.Headers[CredentialHeader].Count != 1) return Task.FromResult(false);
        var actual = Encoding.UTF8.GetBytes(request.Headers[CredentialHeader].ToString());
        var expected = Encoding.UTF8.GetBytes(credential);
        return Task.FromResult(CryptographicOperations.FixedTimeEquals(actual, expected));
    }
}
