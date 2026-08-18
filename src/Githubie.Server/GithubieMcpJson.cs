using System.Text.Json;
using System.Text.Json.Serialization;

namespace Githubie.Server;

/// <summary>
/// MCP Tool定義・Structured Outputに用いるJSON設定です。
/// </summary>
public static class GithubieMcpJson
{
    public static JsonSerializerOptions CreateOptions()
    {
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web)
        {
            PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        };

        options.Converters.Add(new JsonStringEnumConverter(JsonNamingPolicy.SnakeCaseLower));

        return options;
    }
}
