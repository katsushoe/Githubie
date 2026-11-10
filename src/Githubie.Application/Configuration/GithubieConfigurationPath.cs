namespace Githubie.Application.Configuration;

/// <summary>実際のインストール先にある本体設定一ファイルだけを使用します。</summary>
public static class GithubieConfigurationPath
{
    public static string CanonicalPath => Resolve(null);

    public static string Resolve(string? requestedPath, string? binDirectory = null)
    {
        var canonicalPath = Path.Combine(
            GithubiePathLayout.FromBinDirectory(binDirectory ?? AppContext.BaseDirectory).ConfigDirectory, "githubie.json");
        if (requestedPath is not null && (!Path.IsPathFullyQualified(requestedPath)
            || !string.Equals(Path.GetFullPath(requestedPath), canonicalPath, StringComparison.OrdinalIgnoreCase)))
            throw new ArgumentException($"Only {canonicalPath} is supported.", nameof(requestedPath));
        return canonicalPath;
    }
}
