using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;

namespace Githubie.Server;

/// <summary>
/// MCP Tool定義・Structured Outputに用いるJSON設定です。
/// リフレクションベースのTool Schema生成のため、TypeInfoResolverを明示します
/// (未設定だとJsonSerializerOptionsがreadonly化される際に例外になります)。
/// </summary>
public static class GithubieMcpJson
{
    public static JsonSerializerOptions CreateOptions()
    {
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web)
        {
            PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
            TypeInfoResolver = new DefaultJsonTypeInfoResolver(),
        };

        options.Converters.Add(new JsonStringEnumConverter(JsonNamingPolicy.SnakeCaseLower));

        return options;
    }
}
