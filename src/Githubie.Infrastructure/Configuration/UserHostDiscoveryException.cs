namespace Githubie.Infrastructure.Configuration;

/// <summary>資格や保護された本文を含まない、接続検出の安定した失敗コードです。</summary>
public sealed class UserHostDiscoveryException(string code) : IOException(code)
{
    public string Code { get; } = code;
}
