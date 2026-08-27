using System.Text.RegularExpressions;
using Githubie.Application.Configuration;
using Githubie.Application.Interactive;
using Githubie.Application.Credentials;

namespace Githubie.Application.Repositories;

public sealed record RepositoryUpdateRequest(
    IReadOnlyList<string> DirectPushBranches,
    IReadOnlyList<string> PullBranches,
    IReadOnlyList<string> ProtectedBranches,
    string TagTargetBranch,
    string TagPattern,
    bool RequireCleanWorkingTree,
    IReadOnlyDictionary<string, WorkflowPolicyOptions>? Workflows = null);

public sealed record RepositoryMutationInfo(bool Approved, string RepositoryId);

public enum RepositoryMutationError
{
    InvalidRepositoryId,
    RepositoryNotRegistered,
    InvalidPolicy,
    ApprovalDenied,
    ApprovalTimedOut,
    ApprovalUnavailable,
    PersistenceFailed,
    DuplicateRepositoryId,
    TokenNotFound,
    CredentialMigrationFailed,
}

public sealed record RepositoryMutationResult(RepositoryMutationInfo? Value, RepositoryMutationError? Error)
{
    public bool IsSuccess => Value is not null && Error is null;
    public static RepositoryMutationResult Success(RepositoryMutationInfo value) => new(value, null);
    public static RepositoryMutationResult Failure(RepositoryMutationError error) => new(null, error);
}

public interface IRepositoryManagementService
{
    Task<RepositoryMutationResult> UpdateAsync(
        string repositoryId, RepositoryUpdateRequest request, CancellationToken cancellationToken);
    Task<RepositoryMutationResult> UnregisterAsync(string repositoryId, CancellationToken cancellationToken);
    Task<RepositoryMutationResult> RenameAsync(
        string oldRepositoryId, string newRepositoryId, CancellationToken cancellationToken);
}

/// <summary>登録済みRepositoryのPolicy変更と登録解除を提供します。</summary>
public sealed class RepositoryManagementService(
    RepositoryAllowlist allowlist,
    IInteractiveApprovalPrompt approvalPrompt,
    IRepositoryConfigurationStore configurationStore,
    IApiTokenStore tokenStore) : IRepositoryManagementService
{
    private static readonly TimeSpan ApprovalTimeout = TimeSpan.FromMinutes(5);
    private readonly SemaphoreSlim _mutationLock = new(1, 1);

    public async Task<RepositoryMutationResult> UpdateAsync(
        string repositoryId, RepositoryUpdateRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (!RepositoryId.IsValid(repositoryId))
            return RepositoryMutationResult.Failure(RepositoryMutationError.InvalidRepositoryId);
        if (!IsValidPolicy(request))
            return RepositoryMutationResult.Failure(RepositoryMutationError.InvalidPolicy);

        await _mutationLock.WaitAsync(cancellationToken);
        try
        {
            if (!allowlist.TryGet(repositoryId, out var existing))
                return RepositoryMutationResult.Failure(RepositoryMutationError.RepositoryNotRegistered);

            var approval = await approvalPrompt.RequestApprovalAsync(
                new ApprovalPromptRequest(
                    "Githubie repository update",
                    $"Update branch policy for '{repositoryId}'",
                    [$"Direct push: {string.Join(", ", request.DirectPushBranches)}",
                     $"Pull: {string.Join(", ", request.PullBranches)}",
                     $"Protected: {string.Join(", ", request.ProtectedBranches)}",
                     $"Tag target: {request.TagTargetBranch}",
                     $"Workflows: {string.Join(", ", request.Workflows?.Keys ?? existing.Workflows.Keys)}"]),
                ApprovalTimeout,
                cancellationToken);
            var approvalError = MapApprovalError(approval.Outcome);
            if (approvalError is not null) return RepositoryMutationResult.Failure(approvalError.Value);

            var updated = existing with
            {
                DirectPushBranches = request.DirectPushBranches,
                PullBranches = request.PullBranches,
                ProtectedBranches = request.ProtectedBranches,
                TagTargetBranch = request.TagTargetBranch,
                TagPattern = request.TagPattern,
                RequireCleanWorkingTree = request.RequireCleanWorkingTree,
                Workflows = request.Workflows ?? existing.Workflows,
            };
            try { await configurationStore.SaveRepositoryAsync(repositoryId, updated, cancellationToken); }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            { return RepositoryMutationResult.Failure(RepositoryMutationError.PersistenceFailed); }
            if (!allowlist.TryUpdate(repositoryId, updated))
                return RepositoryMutationResult.Failure(RepositoryMutationError.RepositoryNotRegistered);
            return RepositoryMutationResult.Success(new RepositoryMutationInfo(true, repositoryId));
        }
        finally { _mutationLock.Release(); }
    }

    public async Task<RepositoryMutationResult> UnregisterAsync(
        string repositoryId, CancellationToken cancellationToken)
    {
        if (!RepositoryId.IsValid(repositoryId))
            return RepositoryMutationResult.Failure(RepositoryMutationError.InvalidRepositoryId);
        await _mutationLock.WaitAsync(cancellationToken);
        try
        {
            if (!allowlist.TryGet(repositoryId, out _))
                return RepositoryMutationResult.Failure(RepositoryMutationError.RepositoryNotRegistered);
            try { await configurationStore.DeleteRepositoryAsync(repositoryId, cancellationToken); }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or KeyNotFoundException)
            { return RepositoryMutationResult.Failure(RepositoryMutationError.PersistenceFailed); }
            allowlist.TryRemove(repositoryId);
            return RepositoryMutationResult.Success(new RepositoryMutationInfo(false, repositoryId));
        }
        finally { _mutationLock.Release(); }
    }

    public async Task<RepositoryMutationResult> RenameAsync(
        string oldRepositoryId, string newRepositoryId, CancellationToken cancellationToken)
    {
        if (!RepositoryId.IsValid(oldRepositoryId) || !RepositoryId.IsValid(newRepositoryId))
            return RepositoryMutationResult.Failure(RepositoryMutationError.InvalidRepositoryId);
        await _mutationLock.WaitAsync(cancellationToken);
        try
        {
            if (!allowlist.TryGet(oldRepositoryId, out _))
                return RepositoryMutationResult.Failure(RepositoryMutationError.RepositoryNotRegistered);
            if (allowlist.TryGet(newRepositoryId, out _))
                return RepositoryMutationResult.Failure(RepositoryMutationError.DuplicateRepositoryId);

            var tokenMove = tokenStore.Rename(oldRepositoryId, newRepositoryId);
            if (!tokenMove.IsSuccess)
                return RepositoryMutationResult.Failure(tokenMove.Error == ApiTokenStoreError.TokenNotFound
                    ? RepositoryMutationError.TokenNotFound
                    : RepositoryMutationError.CredentialMigrationFailed);
            try
            {
                await configurationStore.RenameRepositoryAsync(oldRepositoryId, newRepositoryId, cancellationToken);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or KeyNotFoundException or InvalidOperationException)
            {
                tokenStore.Rename(newRepositoryId, oldRepositoryId);
                return RepositoryMutationResult.Failure(RepositoryMutationError.PersistenceFailed);
            }
            if (!allowlist.TryRename(oldRepositoryId, newRepositoryId))
            {
                await configurationStore.RenameRepositoryAsync(newRepositoryId, oldRepositoryId, CancellationToken.None);
                tokenStore.Rename(newRepositoryId, oldRepositoryId);
                return RepositoryMutationResult.Failure(RepositoryMutationError.PersistenceFailed);
            }
            return RepositoryMutationResult.Success(new RepositoryMutationInfo(false, newRepositoryId));
        }
        finally { _mutationLock.Release(); }
    }

    private static bool IsValidPolicy(RepositoryUpdateRequest request)
    {
        if (request.DirectPushBranches is null || request.PullBranches is null || request.ProtectedBranches is null)
            return false;
        if (!IsValidName(request.TagTargetBranch)) return false;
        if (request.DirectPushBranches.Any(x => !IsValidName(x))
            || request.PullBranches.Any(x => !IsValidName(x))
            || request.ProtectedBranches.Any(x => !IsValidName(x))) return false;
        try { _ = new Regex(request.TagPattern, RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1)); }
        catch (ArgumentException) { return false; }
        if (request.Workflows is null) return true;
        return request.Workflows.All(item =>
            !string.IsNullOrWhiteSpace(item.Key)
            && item.Value.AllowedRefs.Count > 0
            && item.Value.AllowedRefs.All(IsValidName)
            && item.Value.MaxConcurrent is >= 1 and <= 10
            && item.Value.CorrelationTimeoutSeconds is >= 1 and <= 120
            && item.Value.Inputs.All(input =>
                !string.IsNullOrWhiteSpace(input.Key)
                && input.Value.Type is "string" or "boolean" or "integer"
                && input.Value.MaxLength is >= 1 and <= 4096));
    }

    private static bool IsValidName(string value) =>
        !string.IsNullOrWhiteSpace(value) && value[0] != '-'
        && value.IndexOfAny([' ', '\t', '\r', '\n']) < 0;

    private static RepositoryMutationError? MapApprovalError(ApprovalOutcome outcome) => outcome switch
    {
        ApprovalOutcome.Approved => null,
        ApprovalOutcome.Denied => RepositoryMutationError.ApprovalDenied,
        ApprovalOutcome.TimedOut => RepositoryMutationError.ApprovalTimedOut,
        _ => RepositoryMutationError.ApprovalUnavailable,
    };
}
