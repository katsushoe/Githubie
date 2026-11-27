using System.Text.Json.Nodes;
using System.Text.Json.Serialization;

namespace Githubie.Application.GitHub;

/// <summary>GitHub側のブランチ保護。内部GET原文は公開応答に含めません。</summary>
public sealed record GitHubBranchProtectionSnapshot(
    string Branch,
    bool CanUpdate,
    JsonObject? Policy,
    [property: JsonIgnore] JsonObject Raw);

/// <summary>承認に表示する更新の前後と、再照合に使う内部データです。</summary>
public sealed record GitHubBranchProtectionPlan(
    JsonObject? Before,
    JsonObject After,
    [property: JsonIgnore] JsonObject? RawBefore);

public sealed record GitHubBranchProtectionMutation(string Branch, bool Changed, JsonObject? Policy);

/// <summary>認証済みGitHubへの保護設定通信。Desktop承認はGatewayが担います。</summary>
public interface IGitHubBranchProtectionBackend
{
    Task<GitHubResult<GitHubBranchProtectionSnapshot>> ReadAsync(string repository, string owner, string repo, string branch, CancellationToken cancellationToken);
    GitHubResult<GitHubBranchProtectionPlan> Prepare(GitHubBranchProtectionSnapshot? current, GitHubBranchProtectionEdit edit);
    Task<GitHubResult<bool>> PutAsync(string repository, string owner, string repo, string branch, JsonObject approvedPayload, CancellationToken cancellationToken);
    Task<GitHubResult<bool>> DeleteAsync(string repository, string owner, string repo, string branch, CancellationToken cancellationToken);
}

/// <summary>登録Repositoryとユーザー承認を通したGitHub側ブランチ保護操作。</summary>
public interface IGitHubBranchProtectionGateway
{
    Task<GitHubResult<GitHubBranchProtectionSnapshot>> GetAsync(string repository, string branch, CancellationToken cancellationToken);
    Task<GitHubResult<GitHubBranchProtectionMutation>> UpdateAsync(string repository, string branch, GitHubBranchProtectionEdit edit, bool createIfMissing, CancellationToken cancellationToken);
    Task<GitHubResult<GitHubBranchProtectionMutation>> DeleteAsync(string repository, string branch, CancellationToken cancellationToken);
}
