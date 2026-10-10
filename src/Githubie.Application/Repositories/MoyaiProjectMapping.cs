namespace Githubie.Application.Repositories;

/// <summary>
/// Repository IDとMoyai Project UUIDの対応を解決します。
/// 管理者設定（`provider_authentication.projects`）を優先し、なければ対話承認で登録したRepositoryの対応を使います。
/// </summary>
public sealed class MoyaiProjectMapping(RepositoryAllowlist allowlist, IReadOnlyDictionary<string, Guid>? configured)
{
    private readonly IReadOnlyDictionary<string, Guid> _configured = configured ?? new Dictionary<string, Guid>();

    /// <summary>対応するProject UUIDを返します。対応がなければ<see langword="null"/>です。</summary>
    public Guid? Find(string repositoryId)
    {
        var configuredId = FindConfigured(repositoryId);
        if (configuredId is not null) return configuredId;
        return allowlist.TryGet(repositoryId, out var options) && options.MoyaiProjectId is { } stored && stored != Guid.Empty
            ? stored
            : null;
    }

    /// <summary>
    /// `repositoryId`へ`projectId`を対応付けられるか検査します。
    /// 管理者設定と異なる対応、ほかのRepositoryで使用中のUUIDは拒否します。
    /// </summary>
    public MoyaiProjectMappingError? CheckAssignable(string repositoryId, Guid projectId)
    {
        if (projectId == Guid.Empty) return MoyaiProjectMappingError.InvalidProjectId;

        var configuredId = FindConfigured(repositoryId);
        if (configuredId is not null && configuredId != projectId) return MoyaiProjectMappingError.ConfiguredDifferently;

        var usedByConfigured = _configured.Any(pair => pair.Value == projectId
            && !string.Equals(pair.Key, repositoryId, StringComparison.OrdinalIgnoreCase));
        var usedByRegistered = allowlist.RepositoryIds.Any(other =>
            !string.Equals(other, repositoryId, StringComparison.OrdinalIgnoreCase)
            && allowlist.TryGet(other, out var options)
            && options.MoyaiProjectId == projectId);
        return usedByConfigured || usedByRegistered ? MoyaiProjectMappingError.DuplicateProjectId : null;
    }

    private Guid? FindConfigured(string repositoryId)
    {
        foreach (var (key, value) in _configured)
        {
            if (string.Equals(key, repositoryId, StringComparison.OrdinalIgnoreCase) && value != Guid.Empty) return value;
        }

        return null;
    }
}

/// <summary>Moyai Project UUIDを対応付けられない理由です。</summary>
public enum MoyaiProjectMappingError
{
    /// <summary>空のUUID。</summary>
    InvalidProjectId,

    /// <summary>管理者設定で別のUUIDに対応付け済み。</summary>
    ConfiguredDifferently,

    /// <summary>ほかのRepositoryが同じUUIDを使用中。</summary>
    DuplicateProjectId,
}
