using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.Versioning;
using System.Security.Cryptography;
using System.Security.Principal;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Githubie.Application.Configuration;
using Githubie.Infrastructure.Credentials;

namespace Githubie.Infrastructure.Configuration;

/// <summary>検証後にメモリー内だけで使用する接続情報です。</summary>
public sealed record UserHostConnection(Guid ProfileId, string OwnerSid, int SessionId, int ProcessId,
    long ProcessStartUtcTicks, string Endpoint, string Credential)
{
    public string Generation { get; init; } = "";
    public override string ToString() => $"Githubie user host {ProfileId} (session {SessionId}, process {ProcessId})";
}

/// <summary>schema 1の公開メタデータとCurrentUser DPAPIで保護した資格です。</summary>
public sealed record UserHostDiscovery(int SchemaVersion, string ProviderId, Guid ProfileId, string OwnerSid,
    int SessionId, int ProcessId, long ProcessStartUtcTicks, string Endpoint, string Generation,
    string RuntimePath, string RuntimeSha256, string ProfileSha256, string ProtectedCredential)
{
    public override string ToString() => $"Githubie discovery v{SchemaVersion} ({ProfileId})";
}

/// <summary>毎起動の資格を版付きdiscoveryとして公開し、選択runtime/profileに結び付けます。</summary>
[SupportedOSPlatform("windows")]
public sealed class UserHostConnectionStore
{
    private static readonly byte[] Entropy = Encoding.UTF8.GetBytes("Githubie/UserHost/Connection/v1");
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
    };
    private readonly string _dataDirectory;
    private readonly string _path;
    private readonly string _profilePath;
    private readonly string _runtimePath;
    private readonly IDpapiProtector _protector = new CurrentUserDpapiProtector();

    public UserHostConnectionStore(string dataDirectory, string? expectedRuntimePath = null)
    {
        _dataDirectory = Path.GetFullPath(dataDirectory);
        _path = Path.Combine(_dataDirectory, "connection.json");
        _profilePath = Path.Combine(Path.GetDirectoryName(_dataDirectory)!, "profile.json");
        _runtimePath = Path.GetFullPath(expectedRuntimePath ?? Path.Combine(AppContext.BaseDirectory, "Githubie.Server.exe"));
    }

    public async Task<UserHostConnection> PublishAsync(UserProfileIdentity identity, Uri endpoint, CancellationToken cancellationToken)
    {
        ValidateEndpoint(endpoint);
        ValidateProfile(identity);
        using var process = Process.GetCurrentProcess();
        ValidateRuntime(process, _runtimePath, Hash(_runtimePath));
        var connection = new UserHostConnection(identity.ProfileId, identity.OwnerSid, process.SessionId, process.Id,
            process.StartTime.ToUniversalTime().Ticks, endpoint.AbsoluteUri, Convert.ToBase64String(RandomNumberGenerator.GetBytes(32)))
        { Generation = Guid.NewGuid().ToString("N") };
        var plain = Encoding.UTF8.GetBytes(connection.Credential);
        try
        {
            var record = new UserHostDiscovery(UserHostProtocol.DiscoveryVersion, UserHostProtocol.ProviderId,
                connection.ProfileId, connection.OwnerSid, connection.SessionId, connection.ProcessId,
                connection.ProcessStartUtcTicks, connection.Endpoint, connection.Generation, _runtimePath,
                Hash(_runtimePath), Hash(_profilePath), Convert.ToBase64String(_protector.Protect(plain, Entropy)));
            await UserProfileStore.WriteJsonAsync(_path, record, cancellationToken, JsonOptions);
        }
        finally { CryptographicOperations.ZeroMemory(plain); }
        return connection;
    }

    public UserHostConnection Read(UserProfileIdentity identity)
    {
        try
        {
            ValidateProfile(identity);
            if (!File.Exists(_path)) throw new UserHostDiscoveryException("discovery_missing");
            UserHostDiscoverySecurity.ValidatePrivate(_path, identity.OwnerSid, false);
            var record = ReadRecord();
            if (record.SchemaVersion != UserHostProtocol.DiscoveryVersion || record.ProviderId != UserHostProtocol.ProviderId)
                throw new UserHostDiscoveryException("discovery_unsupported");
            using var current = Process.GetCurrentProcess();
            if (record.ProfileId != identity.ProfileId || record.OwnerSid != identity.OwnerSid || record.SessionId != current.SessionId
                || !Guid.TryParseExact(record.Generation, "N", out _) || record.ProfileSha256 != Hash(_profilePath))
                throw new UserHostDiscoveryException("discovery_profile_mismatch");
            if (!Uri.TryCreate(record.Endpoint, UriKind.Absolute, out var endpoint))
                throw new UserHostDiscoveryException("discovery_invalid");
            ValidateEndpoint(endpoint);
            using var process = Process.GetProcessById(record.ProcessId);
            if (process.HasExited || process.SessionId != record.SessionId || process.StartTime.ToUniversalTime().Ticks != record.ProcessStartUtcTicks)
                throw new UserHostDiscoveryException("discovery_host_stale");
            ValidateRuntime(process, record.RuntimePath, record.RuntimeSha256);
            var plain = _protector.Unprotect(Convert.FromBase64String(record.ProtectedCredential), Entropy);
            try
            {
                var credential = Encoding.UTF8.GetString(plain);
                var decoded = Convert.FromBase64String(credential);
                try
                {
                    if (decoded.Length != 32) throw new UserHostDiscoveryException("discovery_credential_invalid");
                }
                finally { CryptographicOperations.ZeroMemory(decoded); }
                return new(record.ProfileId, record.OwnerSid, record.SessionId, record.ProcessId,
                    record.ProcessStartUtcTicks, record.Endpoint, credential) { Generation = record.Generation };
            }
            finally { CryptographicOperations.ZeroMemory(plain); }
        }
        catch (UserHostDiscoveryException) { throw; }
        catch (UnauthorizedAccessException) { throw new UserHostDiscoveryException("discovery_acl_rejected"); }
        catch (CryptographicException) { throw new UserHostDiscoveryException("discovery_credential_invalid"); }
        catch (ArgumentException) { throw new UserHostDiscoveryException("discovery_host_stale"); }
        catch (Win32Exception) { throw new UserHostDiscoveryException("discovery_runtime_unavailable"); }
        catch (Exception ex) when (ex is IOException or JsonException or FormatException or InvalidOperationException)
        { throw new UserHostDiscoveryException("discovery_invalid"); }
    }

    public void Revoke(UserHostConnection owner)
    {
        if (!File.Exists(_path)) return;
        var current = Read(new UserProfileIdentity(1, owner.ProfileId, owner.OwnerSid));
        if (current.Generation == owner.Generation && current.Credential == owner.Credential) File.Delete(_path);
    }

    private UserHostDiscovery ReadRecord()
    {
        using var stream = new FileStream(_path, FileMode.Open, FileAccess.Read, FileShare.Read);
        if (stream.Length > 65_536) throw new UserHostDiscoveryException("discovery_invalid");
        using var document = JsonDocument.Parse(stream, new JsonDocumentOptions { MaxDepth = 8 });
        if (document.RootElement.ValueKind != JsonValueKind.Object
            || document.RootElement.EnumerateObject().Select(p => p.Name).Distinct(StringComparer.Ordinal).Count()
                != document.RootElement.EnumerateObject().Count()) throw new UserHostDiscoveryException("discovery_invalid");
        var record = document.RootElement.Deserialize<UserHostDiscovery>(JsonOptions) ?? throw new UserHostDiscoveryException("discovery_invalid");
        if (document.RootElement.EnumerateObject().Count() != 13 || record.OwnerSid is null || record.Endpoint is null
            || record.Generation is null || record.RuntimePath is null || record.RuntimeSha256 is null
            || record.ProfileSha256 is null || record.ProtectedCredential is null
            || record.ProcessId <= 0 || record.ProcessStartUtcTicks <= 0 || record.SessionId < 0)
            throw new UserHostDiscoveryException("discovery_invalid");
        return record;
    }

    private void ValidateProfile(UserProfileIdentity identity)
    {
        using var user = WindowsIdentity.GetCurrent();
        if (identity.SchemaVersion != 1 || identity.ProfileId == Guid.Empty || user.User?.Value != identity.OwnerSid)
            throw new UserHostDiscoveryException("discovery_profile_mismatch");
        UserHostDiscoverySecurity.ValidatePrivate(Path.GetDirectoryName(_dataDirectory)!, identity.OwnerSid, true);
        UserHostDiscoverySecurity.ValidatePrivate(_dataDirectory, identity.OwnerSid, true);
        UserHostDiscoverySecurity.ValidatePrivate(_profilePath, identity.OwnerSid, false);
        var persisted = JsonSerializer.Deserialize<UserProfileIdentity>(File.ReadAllText(_profilePath));
        if (persisted != identity) throw new UserHostDiscoveryException("discovery_profile_mismatch");
    }

    private void ValidateRuntime(Process process, string path, string hash)
    {
        UserHostDiscoverySecurity.ValidatePath(path);
        if (!string.Equals(Path.GetFullPath(path), _runtimePath, StringComparison.OrdinalIgnoreCase)
            || !string.Equals(process.MainModule?.FileName, _runtimePath, StringComparison.OrdinalIgnoreCase)
            || !string.Equals(hash, Hash(_runtimePath), StringComparison.Ordinal))
            throw new UserHostDiscoveryException("discovery_runtime_mismatch");
    }

    private static string Hash(string path)
    {
        using var stream = File.OpenRead(path);
        return Convert.ToHexString(SHA256.HashData(stream));
    }

    private static void ValidateEndpoint(Uri uri)
    {
        if (uri.Scheme != "http" || uri.Host != "127.0.0.1" || uri.Port <= 0 || uri.AbsolutePath != "/mcp"
            || !string.IsNullOrEmpty(uri.UserInfo) || !string.IsNullOrEmpty(uri.Query) || !string.IsNullOrEmpty(uri.Fragment))
            throw new UserHostDiscoveryException("discovery_endpoint_rejected");
    }
}
