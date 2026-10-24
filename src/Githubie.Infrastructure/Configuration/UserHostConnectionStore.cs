using System.Diagnostics;
using System.Runtime.Versioning;
using System.Security.Cryptography;
using System.Security.Principal;
using System.Text;
using System.Text.Json;
using Githubie.Infrastructure.Credentials;

namespace Githubie.Infrastructure.Configuration;

public sealed record UserHostConnection(
    [property: System.Text.Json.Serialization.JsonPropertyName("profile_id")] Guid ProfileId,
    [property: System.Text.Json.Serialization.JsonPropertyName("owner_sid")] string OwnerSid,
    [property: System.Text.Json.Serialization.JsonPropertyName("session_id")] int SessionId,
    [property: System.Text.Json.Serialization.JsonPropertyName("process_id")] int ProcessId,
    [property: System.Text.Json.Serialization.JsonPropertyName("process_start_utc_ticks")] long ProcessStartUtcTicks,
    [property: System.Text.Json.Serialization.JsonPropertyName("endpoint")] string Endpoint,
    [property: System.Text.Json.Serialization.JsonPropertyName("credential")] string Credential)
{
    public override string ToString() => $"Githubie user host {ProfileId} (session {SessionId}, process {ProcessId})";
}

/// <summary>起動ごとに変わるHTTP接続資格をCurrentUser DPAPIで保護します。</summary>
[SupportedOSPlatform("windows")]
public sealed class UserHostConnectionStore(string dataDirectory)
{
    private static readonly byte[] Entropy = Encoding.UTF8.GetBytes("Githubie/UserHost/Connection/v2");
    private readonly string _path = Path.Combine(dataDirectory, "connection.dpapi");
    private readonly IDpapiProtector _protector = new CurrentUserDpapiProtector();

    public async Task<UserHostConnection> PublishAsync(UserProfileIdentity identity, Uri endpoint, CancellationToken cancellationToken)
    {
        ValidateEndpoint(endpoint);
        using var process = Process.GetCurrentProcess();
        var connection = new UserHostConnection(identity.ProfileId, identity.OwnerSid, process.SessionId, process.Id,
            process.StartTime.ToUniversalTime().Ticks, endpoint.AbsoluteUri, Convert.ToBase64String(RandomNumberGenerator.GetBytes(32)));
        var plain = JsonSerializer.SerializeToUtf8Bytes(connection);
        var temporary = _path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            await File.WriteAllBytesAsync(temporary, _protector.Protect(plain, Entropy), cancellationToken);
            File.Move(temporary, _path, overwrite: true);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(plain);
            if (File.Exists(temporary)) File.Delete(temporary);
        }
        return connection;
    }

    public UserHostConnection Read(UserProfileIdentity identity)
    {
        var plain = _protector.Unprotect(File.ReadAllBytes(_path), Entropy);
        try
        {
            var connection = JsonSerializer.Deserialize<UserHostConnection>(plain) ?? throw new IOException("Host discovery is invalid.");
            if (connection.OwnerSid != WindowsIdentity.GetCurrent().User?.Value || connection.OwnerSid != identity.OwnerSid
                || connection.ProfileId != identity.ProfileId || connection.SessionId != Process.GetCurrentProcess().SessionId)
                throw new UnauthorizedAccessException("Host discovery belongs to another profile or session.");
            ValidateEndpoint(new Uri(connection.Endpoint));
            using var process = Process.GetProcessById(connection.ProcessId);
            if (process.HasExited || process.SessionId != connection.SessionId
                || process.StartTime.ToUniversalTime().Ticks != connection.ProcessStartUtcTicks)
                throw new IOException("The discovered host is no longer running.");
            if (Convert.FromBase64String(connection.Credential).Length != 32) throw new IOException("Invalid host credential.");
            return connection;
        }
        finally { CryptographicOperations.ZeroMemory(plain); }
    }

    public void Revoke(UserHostConnection owner)
    {
        if (!File.Exists(_path)) return;
        var plain = _protector.Unprotect(File.ReadAllBytes(_path), Entropy);
        try
        {
            var current = JsonSerializer.Deserialize<UserHostConnection>(plain);
            if (current?.Credential == owner.Credential) File.Delete(_path);
        }
        finally { CryptographicOperations.ZeroMemory(plain); }
    }

    private static void ValidateEndpoint(Uri uri)
    {
        if (uri.Scheme != "http" || !uri.IsLoopback || uri.Port <= 0 || !string.IsNullOrEmpty(uri.UserInfo)
            || !string.IsNullOrEmpty(uri.Query) || !string.IsNullOrEmpty(uri.Fragment))
            throw new IOException("Host endpoint must be an HTTP loopback address without credentials or query.");
    }
}
