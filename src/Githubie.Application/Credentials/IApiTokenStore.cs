namespace Githubie.Application.Credentials;

/// <summary>
/// リポジトリ単位のGitHub Personal Access Tokenを保管するポートです。
/// トークン本体を戻り値としてキャッシュせず、都度読み出すことを前提とします。
/// </summary>
public interface IApiTokenStore
{
    ApiTokenStoreResult Save(string repositoryId, ReadOnlySpan<char> token);

    ApiTokenStoreReadResult Read(string repositoryId);

    ApiTokenStoreResult Delete(string repositoryId);

    ApiTokenStoreResult Rename(string oldRepositoryId, string newRepositoryId);
}
