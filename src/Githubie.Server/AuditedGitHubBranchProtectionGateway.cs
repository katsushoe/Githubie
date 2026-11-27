using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using Githubie.Application.GitHub;

namespace Githubie.Server;

/// <summary>GitHub側ブランチ保護操作を秘密値を含めず監査します。</summary>
public sealed class AuditedGitHubBranchProtectionGateway(
    IGitHubBranchProtectionGateway inner, IGithubieAuditLogger audit) : IGitHubBranchProtectionGateway
{
    public Task<GitHubResult<GitHubBranchProtectionSnapshot>> GetAsync(string repository, string branch, CancellationToken cancellationToken) =>
        RunAsync("github_branch_protection_get", repository, branch, () => inner.GetAsync(repository, branch, cancellationToken));

    public Task<GitHubResult<GitHubBranchProtectionMutation>> UpdateAsync(
        string repository, string branch, GitHubBranchProtectionEdit edit, bool createIfMissing, CancellationToken cancellationToken) =>
        RunAsync("github_branch_protection_update", repository, branch, () => inner.UpdateAsync(repository, branch, edit, createIfMissing, cancellationToken));

    public Task<GitHubResult<GitHubBranchProtectionMutation>> DeleteAsync(string repository, string branch, CancellationToken cancellationToken) =>
        RunAsync("github_branch_protection_delete", repository, branch, () => inner.DeleteAsync(repository, branch, cancellationToken));

    private static string Fingerprint(string value) =>
        "sha256:" + Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value ?? string.Empty)));

    private async Task<GitHubResult<T>> RunAsync<T>(string tool, string repository, string branch, Func<Task<GitHubResult<T>>> action)
    {
        var timer = Stopwatch.StartNew();
        try
        {
            var result = await action();
            // この操作では外部のエラー本文を応答・監査へ中継しません。
            if (!result.IsSuccess) result = result with { Diagnostic = null };
            if (!result.IsSuccess && result.CorrelationId is null) result = result with { CorrelationId = Guid.NewGuid().ToString("N") };
            audit.Write(new GithubieAuditEvent("mcp", tool, Fingerprint(repository), Fingerprint(branch), null, null,
                result.IsSuccess ? "success" : "failure", timer.ElapsedMilliseconds,
                result.IsSuccess ? null : result.Error?.ToString(), CorrelationId: result.CorrelationId));
            return result;
        }
        catch (Exception)
        {
            audit.Write(new GithubieAuditEvent("mcp", tool, Fingerprint(repository), Fingerprint(branch), null, null,
                "failure", timer.ElapsedMilliseconds, "unexpected_error"));
            throw;
        }
    }
}
