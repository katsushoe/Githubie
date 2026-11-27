using System.Security.AccessControl;
using System.Security.Cryptography;
using System.Security.Principal;
using System.Text;
using System.Text.Json.Nodes;
using Tomlyn;
using Tomlyn.Model;
using Tomlyn.Serialization;

namespace Githubie.Cli;

/// <summary>ユーザーのMCP設定のGithubieエントリだけを構成します。秘密値は登録しません。</summary>
internal sealed class ClientRegistration(string executable)
{
    public async Task<bool> ApplyAsync(string client, string path, bool configure, bool replaceLegacy, CancellationToken cancellationToken)
    {
        if (client is not ("codex" or "claude")) throw new ArgumentException("Supported clients: codex, claude.");
        var fullPath = Path.GetFullPath(path);
        if (!configure && !File.Exists(fullPath)) return false;
        var directory = Path.GetDirectoryName(fullPath)!;
        Directory.CreateDirectory(directory);
        for (var info = new DirectoryInfo(directory); info is not null; info = info.Parent)
            if ((info.Attributes & FileAttributes.ReparsePoint) != 0) throw new IOException("Client configuration must not traverse reparse points.");
        if (File.Exists(fullPath) && (File.GetAttributes(fullPath) & FileAttributes.ReparsePoint) != 0)
            throw new IOException("Client configuration must not be a symbolic link.");
        using var ownedLock = new FileStream(fullPath + ".githubie.lock", FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
        var existed = File.Exists(fullPath);
        var original = existed ? await File.ReadAllBytesAsync(fullPath, cancellationToken) : [];
        using var snapshot = new StreamReader(new MemoryStream(original), Encoding.UTF8, detectEncodingFromByteOrderMarks: true);
        var text = await snapshot.ReadToEndAsync(cancellationToken);
        var edited = Edit(client, text, configure, replaceLegacy);
        if (edited is null) return false;
        var temporary = fullPath + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            await using (var file = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            {
                var access = existed ? new FileInfo(fullPath).GetAccessControl(AccessControlSections.Access) : PrivateAccess();
                new FileInfo(temporary).SetAccessControl(access);
                var content = Encoding.UTF8.GetBytes(edited);
                await file.WriteAsync(content, cancellationToken);
                file.Flush(flushToDisk: true);
            }
            var current = File.Exists(fullPath) ? await File.ReadAllBytesAsync(fullPath, cancellationToken) : [];
            if (existed != File.Exists(fullPath) || (existed && !CryptographicOperations.FixedTimeEquals(
                SHA256.HashData(original), SHA256.HashData(current))))
                throw new IOException("The client configuration changed concurrently. Retry after its other writer finishes.");
            // All fallible parsing and staging precede the single atomic publication. Failure leaves the original intact.
            File.Move(temporary, fullPath, overwrite: existed);
            return true;
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }

    internal string? Edit(string client, string text, bool configure, bool replaceLegacy)
    {
        try { return client == "codex" ? EditToml(text, configure, replaceLegacy) : EditJson(text, configure, replaceLegacy); }
        catch (Exception exception) when (exception.GetType().Namespace?.StartsWith("Tomlyn", StringComparison.Ordinal) == true)
        { throw new InvalidDataException("The existing TOML configuration is invalid."); }
    }

    private string? EditToml(string text, bool configure, bool replaceLegacy)
    {
        var options = new TomlSerializerOptions { MetadataStore = new TomlMetadataStore() };
        var root = string.IsNullOrWhiteSpace(text) ? new TomlTable() : TomlSerializer.Deserialize<TomlTable>(text, options)
            ?? throw new InvalidDataException("The client configuration is empty.");
        root.TryGetValue("mcp_servers", out var value);
        if (value is not null and not TomlTable) throw new InvalidDataException("mcp_servers must be a table.");
        var servers = value as TomlTable ?? new TomlTable();
        servers.TryGetValue("githubie", out var existing);
        var entry = existing as TomlTable;
        var owned = entry is not null && entry.TryGetValue("command", out var command) && SameCommand(command as string)
            && entry.TryGetValue("args", out var args) && args is TomlArray array && array.Count == 2 &&
            Equals(array[0], "mcp") && Equals(array[1], "stdio") && !entry.ContainsKey("url");
        if (configure && owned) return null;
        if (!configure && existing is null) return null;
        if (existing is not null && !owned)
        {
            var legacy = configure && replaceLegacy && entry is not null && entry.TryGetValue("url", out var url)
                && IsLegacyEndpoint(url as string) && entry.Keys.All(key => key is "url" or "enabled" or "startup_timeout_sec" or "tool_timeout_sec" or "enabled_tools" or "disabled_tools");
            if (!legacy) throw new IOException("The githubie entry is not owned by this installation. It was preserved.");
        }
        if (configure)
        {
            entry ??= new TomlTable();
            entry.Remove("url");
            entry["command"] = Path.GetFullPath(executable);
            entry["args"] = new TomlArray { "mcp", "stdio" };
            servers["githubie"] = entry;
            root["mcp_servers"] = servers;
        }
        else servers.Remove("githubie");
        var result = TomlSerializer.Serialize(root, options);
        _ = TomlSerializer.Deserialize<TomlTable>(result);
        return result;
    }

    private string? EditJson(string text, bool configure, bool replaceLegacy)
    {
        var root = string.IsNullOrWhiteSpace(text) ? new JsonObject() : JsonNode.Parse(text) as JsonObject
            ?? throw new InvalidDataException("The client configuration must be a JSON object.");
        var value = root["mcpServers"];
        if (value is not null and not JsonObject) throw new InvalidDataException("mcpServers must be an object.");
        var servers = value as JsonObject ?? new JsonObject();
        var existing = servers["githubie"];
        var entry = existing as JsonObject;
        var owned = entry is not null && SameCommand(entry["command"]?.GetValue<string>()) && entry["args"] is JsonArray args
            && args.Count == 2 && args[0]?.GetValue<string>() == "mcp" && args[1]?.GetValue<string>() == "stdio"
            && entry["url"] is null && (entry["type"] is null || entry["type"]?.GetValue<string>() == "stdio");
        if (configure && owned) return null;
        if (!configure && existing is null) return null;
        if (existing is not null && !owned)
        {
            var legacy = configure && replaceLegacy && entry is not null && IsLegacyEndpoint(entry["url"]?.GetValue<string>())
                && entry.All(pair => pair.Key is "url" or "type");
            if (!legacy) throw new IOException("The githubie entry is not owned by this installation. It was preserved.");
        }
        if (configure)
        {
            servers["githubie"] = new JsonObject { ["type"] = "stdio", ["command"] = Path.GetFullPath(executable),
                ["args"] = new JsonArray("mcp", "stdio") };
            if (value is null) root["mcpServers"] = servers;
        }
        else servers.Remove("githubie");
        var result = root.ToJsonString(new System.Text.Json.JsonSerializerOptions { WriteIndented = true });
        _ = JsonNode.Parse(result);
        return result;
    }

    private bool SameCommand(string? command) => command is not null && Path.IsPathFullyQualified(command) &&
        string.Equals(Path.GetFullPath(command), Path.GetFullPath(executable), StringComparison.OrdinalIgnoreCase);

    private static bool IsLegacyEndpoint(string? url) => url is "http://127.0.0.1:45460/mcp" or "http://localhost:45460/mcp";

    private static FileSecurity PrivateAccess()
    {
        var access = new FileSecurity();
        access.SetAccessRuleProtection(true, false);
        access.AddAccessRule(new FileSystemAccessRule(WindowsIdentity.GetCurrent().User
            ?? throw new UnauthorizedAccessException("A current user SID is required."), FileSystemRights.FullControl, AccessControlType.Allow));
        return access;
    }
}
