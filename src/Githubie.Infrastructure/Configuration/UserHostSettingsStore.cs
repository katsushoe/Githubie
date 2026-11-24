using System.Text.Json;
using System.Text.Json.Nodes;
using Githubie.Application.Configuration;

namespace Githubie.Infrastructure.Configuration;

/// <summary>単一設定の user_host を読み、他の設定を保持して更新します。</summary>
public sealed class UserHostSettingsStore(string configPath)
{
    private static readonly JsonSerializerOptions Options = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        UnmappedMemberHandling = System.Text.Json.Serialization.JsonUnmappedMemberHandling.Disallow
    };

    public UserHostOptions Read() => ReadSettings(ReadDocument());

    internal JsonObject ReadDocument() => JsonNode.Parse(File.ReadAllText(configPath)) as JsonObject
        ?? throw new InvalidDataException("The Githubie configuration must be an object.");

    private static UserHostOptions ReadSettings(JsonObject document)
    {
        if (!document.TryGetPropertyValue("user_host", out var node)) return new();
        var settings = node?.Deserialize<UserHostOptions>(Options)
            ?? throw new InvalidDataException("user_host must be an object.");
        if (!UserHostAuthenticationMode.IsValid(settings.AuthenticationMode))
            throw new InvalidDataException("user_host.authentication_mode must be legacy or windows_owner.");
        if (settings.AuthenticationMode == UserHostAuthenticationMode.Legacy
            && settings.DirectUnrestricted && !settings.MoyaiIntegration)
            throw new InvalidDataException("user_host.direct_unrestricted requires moyai_integration.");
        return settings;
    }

    internal string PrepareDesktopSettings(bool enabled, bool? startupUpdateEnabled)
    {
        var document = ReadDocument();
        var settings = ReadSettings(document) with { AutostartEnabled = enabled };
        if (startupUpdateEnabled.HasValue)
            settings = settings with { StartupUpdateEnabled = startupUpdateEnabled.Value };
        document["user_host"] = JsonSerializer.SerializeToNode(settings, Options);
        var temporary = configPath + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            File.WriteAllText(temporary, document.ToJsonString(new JsonSerializerOptions { WriteIndented = true }));
            return temporary;
        }
        catch
        {
            if (File.Exists(temporary)) File.Delete(temporary);
            throw;
        }
    }
}
