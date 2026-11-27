namespace Githubie.Application.Repositories;

/// <summary>ユーザープロフィール内の設定と資格のID変更/解除を同一DBトランザクションで確定します。</summary>
public interface IAtomicRepositoryManagementStore
{
    Task RenameRegistrationAsync(string oldRepositoryId, string newRepositoryId, CancellationToken cancellationToken);
    Task DeleteRegistrationAsync(string repositoryId, CancellationToken cancellationToken);
}
