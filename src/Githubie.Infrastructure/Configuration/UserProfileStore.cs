using System.Runtime.Versioning;
using System.Security.AccessControl;
using System.Security.Principal;
using System.Text.Json;
using Githubie.Application.Configuration;

namespace Githubie.Infrastructure.Configuration;

public sealed record UserProfileIdentity(
    [property: System.Text.Json.Serialization.JsonPropertyName("schema_version")] int SchemaVersion,
    [property: System.Text.Json.Serialization.JsonPropertyName("profile_id")] Guid ProfileId,
    [property: System.Text.Json.Serialization.JsonPropertyName("owner_sid")] string OwnerSid);

/// <summary>新規のユーザープロフィールだけを初期化し、Service構成の暗黙取り込みを防止します。</summary>
[SupportedOSPlatform("windows")]
public sealed class UserProfileStore(GithubiePathLayout layout)
{
    public UserProfileIdentity ReadIdentity()
    {
        var path = Path.Combine(Path.GetDirectoryName(layout.ConfigDirectory)!, "profile.json");
        RejectReparsePoints(path);
        using var user = WindowsIdentity.GetCurrent();
        var identity = JsonSerializer.Deserialize<UserProfileIdentity>(File.ReadAllText(path))
            ?? throw new InvalidDataException("User profile identity is missing.");
        if (identity.SchemaVersion != 1 || identity.ProfileId == Guid.Empty || identity.OwnerSid != user.User?.Value)
            throw new UnauthorizedAccessException("The user profile belongs to a different identity or version.");
        return identity;
    }

    public async Task<UserProfileIdentity> InitializeAsync(CancellationToken cancellationToken)
    {
        using var user = WindowsIdentity.GetCurrent();
        using var process = System.Diagnostics.Process.GetCurrentProcess();
        if (process.SessionId == 0 || new WindowsPrincipal(user).IsInRole(WindowsBuiltInRole.Administrator))
            throw new UnauthorizedAccessException("A non-elevated signed-in user session is required.");
        var sid = user.User ?? throw new UnauthorizedAccessException("A Windows user SID is required.");
        if (sid.IsWellKnown(WellKnownSidType.LocalSystemSid) || sid.IsWellKnown(WellKnownSidType.LocalServiceSid)
            || sid.IsWellKnown(WellKnownSidType.NetworkServiceSid))
            throw new UnauthorizedAccessException("A signed-in user profile is required.");
        var root = Path.GetDirectoryName(layout.ConfigDirectory)!;
        RejectReparsePoints(root);
        var created = !Directory.Exists(root);
        Directory.CreateDirectory(root);
        var identityPath = Path.Combine(root, "profile.json");
        if (!created && !File.Exists(identityPath) && Directory.EnumerateFileSystemEntries(root).Any())
            throw new IOException("The existing directory is not a Githubie user profile.");
        if (created) RestrictDirectory(root, sid);
        using var initializationLock = new FileStream(Path.Combine(root, "profile.lock"), FileMode.OpenOrCreate,
            FileAccess.ReadWrite, FileShare.None);
        UserProfileIdentity identity;
        if (File.Exists(identityPath))
        {
            identity = JsonSerializer.Deserialize<UserProfileIdentity>(await File.ReadAllTextAsync(identityPath, cancellationToken))
                ?? throw new InvalidDataException("User profile identity is missing.");
            if (identity.SchemaVersion != 1 || identity.ProfileId == Guid.Empty || identity.OwnerSid != sid.Value)
                throw new UnauthorizedAccessException("The user profile belongs to a different identity or version.");
        }
        else
        {
            identity = new UserProfileIdentity(1, Guid.NewGuid(), sid.Value);
            await WriteJsonAsync(identityPath, identity, cancellationToken);
        }
        RestrictDirectory(root, sid);
        foreach (var directory in new[] { layout.ConfigDirectory, layout.DataDirectory, layout.LogsDirectory, layout.SecretsDirectory })
        {
            RejectReparsePoints(directory);
            Directory.CreateDirectory(directory);
            RestrictDirectory(directory, sid);
        }
        var config = Path.Combine(layout.ConfigDirectory, "githubie.json");
        var hostOptions = Path.Combine(layout.ConfigDirectory, "host.json");
        if (!File.Exists(hostOptions)) await WriteJsonAsync(hostOptions, new UserHostOptions(), cancellationToken);
        if (!File.Exists(config))
        {
            // Each fresh profile selects its own port. A later bind race fails closed; no foreign listener is adopted.
            using var portReservation = new System.Net.Sockets.TcpListener(System.Net.IPAddress.Loopback, 0);
            portReservation.Start();
            var port = ((System.Net.IPEndPoint)portReservation.LocalEndpoint).Port;
            await WriteJsonAsync(config, new { mcp_port = port, mcp_path = GithubieOptions.DefaultMcpPath,
                repositories = new Dictionary<string, RepositoryOptions>() }, cancellationToken);
        }
        return identity;
    }

    internal static async Task WriteJsonAsync<T>(string path, T value, CancellationToken cancellationToken, JsonSerializerOptions? options = null)
    {
        var temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            await File.WriteAllTextAsync(temporary, JsonSerializer.Serialize(value, options ?? new JsonSerializerOptions { WriteIndented = true }), cancellationToken);
            File.Move(temporary, path, overwrite: true);
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }

    private static void RestrictDirectory(string path, SecurityIdentifier sid)
    {
        var security = new DirectorySecurity();
        security.SetAccessRuleProtection(true, false);
        security.AddAccessRule(new FileSystemAccessRule(sid, FileSystemRights.FullControl,
            InheritanceFlags.ContainerInherit | InheritanceFlags.ObjectInherit, PropagationFlags.None, AccessControlType.Allow));
        new DirectoryInfo(path).SetAccessControl(security);
    }

    private static void RejectReparsePoints(string path)
    {
        for (var current = new DirectoryInfo(path); current is not null; current = current.Parent)
            if (current.Exists && (current.Attributes & FileAttributes.ReparsePoint) != 0)
                throw new IOException("User profile paths must not contain reparse points.");
    }
}
