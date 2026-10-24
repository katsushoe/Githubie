using System.Text.Json.Serialization;

namespace Githubie.Infrastructure.Configuration;

/// <summary>自動起動と手動再起動で共通に読み込むユーザーホストの起動モードです。</summary>
public sealed record UserHostOptions(
    [property: JsonPropertyName("moyai_integration")] bool MoyaiIntegration = false,
    [property: JsonPropertyName("direct_unrestricted")] bool DirectUnrestricted = false);
