using Githubie.Application.Configuration;

namespace Githubie.Application.Repositories;

/// <summary>新規登録設定と任意の資格を単一の永続化境界で確定します。既存IDは上書きしません。</summary>
public interface IAtomicRepositoryRegistrationStore
{
    Task SaveRegistrationAsync(string repositoryId, RepositoryOptions options, char[]? token,
        CancellationToken cancellationToken);
}
