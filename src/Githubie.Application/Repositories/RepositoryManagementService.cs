using System.Text.RegularExpressions;
using System.Text.Json.Serialization;
using Githubie.Application.Configuration;
using Githubie.Application.Interactive;
using Githubie.Application.Credentials;
using Githubie.Domain;

namespace Githubie.Application.Repositories;

public sealed record RepositoryUpdateRequest(
    IReadOnlyList<string> DirectPushBranches,
    IReadOnlyList<string> PullBranches,
    IReadOnlyList<string> ProtectedBranches,
    string TagTargetBranch,
    string TagPattern,
    bool RequireCleanWorkingTree,
    IReadOnlyDictionary<string, WorkflowPolicyOptions>? Workflows = null,
    string? CommitAuthorName = null,
    string? CommitAuthorEmail = null);

public sealed record RepositoryMutationInfo(bool Approved, string RepositoryId)
{
    [JsonPropertyName("settings"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public RepositorySettings? Settings { get; init; }

    [JsonPropertyName("changed_fields"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public IReadOnlyList<string>? ChangedFields { get; init; }
}

public sealed record RepositorySettings(
    [property: JsonPropertyName("direct_push_branches")] IReadOnlyList<string> DirectPushBranches,
    [property: JsonPropertyName("protected_branches")] IReadOnlyList<string> ProtectedBranches,
    [property: JsonPropertyName("pull_branches")] IReadOnlyList<string> PullBranches,
    [property: JsonPropertyName("tag_pattern")] string TagPattern,
    [property: JsonPropertyName("tag_target_branch")] string TagTargetBranch,
    [property: JsonPropertyName("require_clean_working_tree")] bool RequireCleanWorkingTree,
    [property: JsonPropertyName("commit_author_name"), JsonIgnore(Condition = JsonIgnoreCondition.Never)] string? CommitAuthorName,
    [property: JsonPropertyName("commit_author_email"), JsonIgnore(Condition = JsonIgnoreCondition.Never)] string? CommitAuthorEmail,
    [property: JsonPropertyName("pull_request_routes")] IReadOnlyList<PullRequestRoute> PullRequestRoutes);

public sealed record RepositorySettingsChanges(
    IReadOnlyList<string>? DirectPushBranches = null,
    bool HasDirectPushBranches = false,
    IReadOnlyList<string>? ProtectedBranches = null,
    bool HasProtectedBranches = false,
    IReadOnlyList<string>? PullBranches = null,
    bool HasPullBranches = false,
    string? TagPattern = null,
    bool HasTagPattern = false,
    string? TagTargetBranch = null,
    bool HasTagTargetBranch = false,
    bool? RequireCleanWorkingTree = null,
    bool HasRequireCleanWorkingTree = false,
    string? CommitAuthorName = null,
    bool HasCommitAuthorName = false,
    string? CommitAuthorEmail = null,
    bool HasCommitAuthorEmail = false,
    IReadOnlyList<PullRequestRoute>? PullRequestRoutes = null,
    bool HasPullRequestRoutes = false);

public enum RepositoryMutationError
{
    InvalidRepositoryId,
    RepositoryNotRegistered,
    InvalidPolicy,
    InvalidAuthorIdentity,
    ApprovalDenied,
    ApprovalTimedOut,
    ApprovalUnavailable,
    PersistenceFailed,
    DuplicateRepositoryId,
    TokenNotFound,
    CredentialMigrationFailed,
    InvalidMoyaiProjectId,
    DuplicateMoyaiProjectId,
}

public sealed record RepositoryMutationResult(RepositoryMutationInfo? Value, RepositoryMutationError? Error)
{
    public bool IsSuccess => Value is not null && Error is null;
    public static RepositoryMutationResult Success(RepositoryMutationInfo value) => new(value, null);
    public static RepositoryMutationResult Failure(RepositoryMutationError error) => new(null, error);
}

public interface IRepositoryManagementService
{
    bool TryGetSettings(string repositoryId, out RepositorySettings settings);
    Task<RepositoryMutationResult> UpdateSettingsAsync(string repositoryId, RepositorySettingsChanges changes, CancellationToken cancellationToken);
    Task<RepositoryMutationResult> UpdateAsync(
        string repositoryId, RepositoryUpdateRequest request, CancellationToken cancellationToken);
    Task<RepositoryMutationResult> UnregisterAsync(string repositoryId, CancellationToken cancellationToken);
    /// <summary>登録済みRepositoryへMoyai Project UUIDを対話承認後に対応付け、実行中サービスへ即時反映します。</summary>
    Task<RepositoryMutationResult> SetMoyaiProjectAsync(
        string repositoryId, Guid moyaiProjectId, CancellationToken cancellationToken);
    Task<RepositoryMutationResult> RenameAsync(
        string oldRepositoryId, string newRepositoryId, CancellationToken cancellationToken);
}

/// <summary>登録済みRepositoryのPolicy変更と登録解除を提供します。</summary>
public sealed class RepositoryManagementService(
    RepositoryAllowlist allowlist,
    IInteractiveApprovalPrompt approvalPrompt,
    IRepositoryConfigurationStore configurationStore,
    IApiTokenStore tokenStore,
    MoyaiProjectMapping? moyaiProjects = null,
    IAtomicRepositoryManagementStore? atomicManagementStore = null,
    RepositoryMutationLock? mutationLock = null) : IRepositoryManagementService
{
    private readonly MoyaiProjectMapping _moyaiProjects = moyaiProjects ?? new MoyaiProjectMapping(allowlist, null);

    private static readonly TimeSpan ApprovalTimeout = TimeSpan.FromMinutes(5);
    private readonly SemaphoreSlim _mutationLock = mutationLock?.Gate ?? new(1, 1);

    public bool TryGetSettings(string repositoryId, out RepositorySettings settings)
    {
        if (RepositoryId.TryNormalize(repositoryId, out var normalized) && allowlist.TryGet(normalized, out var current))
        {
            settings = ToSettings(current);
            return true;
        }
        settings = null!;
        return false;
    }

    public async Task<RepositoryMutationResult> UpdateSettingsAsync(
        string repositoryId, RepositorySettingsChanges changes, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(changes);
        if (!RepositoryId.TryNormalize(repositoryId, out repositoryId))
            return RepositoryMutationResult.Failure(RepositoryMutationError.InvalidRepositoryId);
        if (!Any(changes)) return RepositoryMutationResult.Failure(RepositoryMutationError.InvalidPolicy);
        if (changes.HasCommitAuthorName != changes.HasCommitAuthorEmail
            || (changes.HasCommitAuthorName && !CommitAuthorIdentity.IsValid(changes.CommitAuthorName, changes.CommitAuthorEmail)))
            return RepositoryMutationResult.Failure(RepositoryMutationError.InvalidAuthorIdentity);

        await _mutationLock.WaitAsync(cancellationToken);
        try
        {
            if (!allowlist.TryGet(repositoryId, out var current))
                return RepositoryMutationResult.Failure(RepositoryMutationError.RepositoryNotRegistered);
            var updated = current with
            {
                DirectPushBranches = changes.HasDirectPushBranches ? changes.DirectPushBranches! : current.DirectPushBranches,
                ProtectedBranches = changes.HasProtectedBranches ? changes.ProtectedBranches! : current.ProtectedBranches,
                PullBranches = changes.HasPullBranches ? changes.PullBranches! : current.PullBranches,
                TagPattern = changes.HasTagPattern ? changes.TagPattern! : current.TagPattern,
                TagTargetBranch = changes.HasTagTargetBranch ? changes.TagTargetBranch! : current.TagTargetBranch,
                RequireCleanWorkingTree = changes.HasRequireCleanWorkingTree ? changes.RequireCleanWorkingTree!.Value : current.RequireCleanWorkingTree,
                CommitAuthorName = changes.HasCommitAuthorName ? changes.CommitAuthorName : current.CommitAuthorName,
                CommitAuthorEmail = changes.HasCommitAuthorEmail ? changes.CommitAuthorEmail : current.CommitAuthorEmail,
                PullRequestRoutes = changes.HasPullRequestRoutes ? changes.PullRequestRoutes! : current.PullRequestRoutes,
            };
            if (!IsValidSettings(updated)) return RepositoryMutationResult.Failure(RepositoryMutationError.InvalidPolicy);
            var approval = await approvalPrompt.RequestApprovalAsync(new ApprovalPromptRequest(
                "Githubie repository settings update", $"Update selected settings for '{repositoryId}'",
                ["Fields: " + string.Join(", ", RequestedNames(changes))]), ApprovalTimeout, cancellationToken);
            var approvalError = MapApprovalError(approval.Outcome);
            if (approvalError is not null) return RepositoryMutationResult.Failure(approvalError.Value);
            try { await configurationStore.SaveRepositoryAsync(repositoryId, updated, cancellationToken); }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            { return RepositoryMutationResult.Failure(RepositoryMutationError.PersistenceFailed); }
            if (!allowlist.TryUpdate(repositoryId, updated))
                return RepositoryMutationResult.Failure(RepositoryMutationError.RepositoryNotRegistered);
            return RepositoryMutationResult.Success(new RepositoryMutationInfo(true, repositoryId)
            {
                Settings = ToSettings(updated),
                ChangedFields = ChangedNames(current, updated).ToArray(),
            });
        }
        finally { _mutationLock.Release(); }
    }

    private static bool Any(RepositorySettingsChanges c) => c.HasDirectPushBranches || c.HasProtectedBranches || c.HasPullBranches || c.HasTagPattern || c.HasTagTargetBranch || c.HasRequireCleanWorkingTree || c.HasCommitAuthorName || c.HasCommitAuthorEmail || c.HasPullRequestRoutes;
    private static IEnumerable<string> RequestedNames(RepositorySettingsChanges c)
    {
        if (c.HasDirectPushBranches) yield return "direct_push_branches";
        if (c.HasProtectedBranches) yield return "protected_branches";
        if (c.HasPullBranches) yield return "pull_branches";
        if (c.HasTagPattern) yield return "tag_pattern";
        if (c.HasTagTargetBranch) yield return "tag_target_branch";
        if (c.HasRequireCleanWorkingTree) yield return "require_clean_working_tree";
        if (c.HasCommitAuthorName) yield return "commit_author_name";
        if (c.HasCommitAuthorEmail) yield return "commit_author_email";
        if (c.HasPullRequestRoutes) yield return "pull_request_routes";
    }
    private static IEnumerable<string> ChangedNames(RepositoryOptions old, RepositoryOptions updated)
    {
        if (!old.DirectPushBranches.SequenceEqual(updated.DirectPushBranches)) yield return "direct_push_branches";
        if (!old.ProtectedBranches.SequenceEqual(updated.ProtectedBranches)) yield return "protected_branches";
        if (!old.PullBranches.SequenceEqual(updated.PullBranches)) yield return "pull_branches";
        if (old.TagPattern != updated.TagPattern) yield return "tag_pattern";
        if (old.TagTargetBranch != updated.TagTargetBranch) yield return "tag_target_branch";
        if (old.RequireCleanWorkingTree != updated.RequireCleanWorkingTree) yield return "require_clean_working_tree";
        if (old.CommitAuthorName != updated.CommitAuthorName) yield return "commit_author_name";
        if (old.CommitAuthorEmail != updated.CommitAuthorEmail) yield return "commit_author_email";
        if (!old.PullRequestRoutes.SequenceEqual(updated.PullRequestRoutes)) yield return "pull_request_routes";
    }
    private static RepositorySettings ToSettings(RepositoryOptions r) => new(r.DirectPushBranches, r.ProtectedBranches, r.PullBranches, r.TagPattern, r.TagTargetBranch, r.RequireCleanWorkingTree, r.CommitAuthorName, r.CommitAuthorEmail, r.PullRequestRoutes);
    private static bool IsValidSettings(RepositoryOptions r)
    {
        if (!IsValidName(r.TagTargetBranch) || string.IsNullOrWhiteSpace(r.TagPattern)
            || !r.DirectPushBranches.All(IsValidName) || !r.PullBranches.All(IsValidName) || !r.ProtectedBranches.All(IsValidName)
            || r.PullRequestRoutes is null || r.PullRequestRoutes.Any(route => route is null
                || !IsValidName(route.SourceBranch) || !IsValidName(route.DestinationBranch))
            || r.PullRequestRoutes.Distinct().Count() != r.PullRequestRoutes.Count
            || !((r.CommitAuthorName is null && r.CommitAuthorEmail is null) || CommitAuthorIdentity.IsValid(r.CommitAuthorName, r.CommitAuthorEmail)))
            return false;
        try { _ = new Regex(r.TagPattern, RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1)); return true; }
        catch (ArgumentException) { return false; }
    }

    public async Task<RepositoryMutationResult> UpdateAsync(
        string repositoryId, RepositoryUpdateRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (!RepositoryId.TryNormalize(repositoryId, out repositoryId))
            return RepositoryMutationResult.Failure(RepositoryMutationError.InvalidRepositoryId);
        if (!IsValidPolicy(request))
            return RepositoryMutationResult.Failure(RepositoryMutationError.InvalidPolicy);
        if ((request.CommitAuthorName is null) != (request.CommitAuthorEmail is null)
            || (request.CommitAuthorName is not null && !CommitAuthorIdentity.IsValid(request.CommitAuthorName, request.CommitAuthorEmail)))
            return RepositoryMutationResult.Failure(RepositoryMutationError.InvalidAuthorIdentity);

        await _mutationLock.WaitAsync(cancellationToken);
        try
        {
            if (!allowlist.TryGet(repositoryId, out var existing))
                return RepositoryMutationResult.Failure(RepositoryMutationError.RepositoryNotRegistered);

            var approval = await approvalPrompt.RequestApprovalAsync(
                new ApprovalPromptRequest(
                    "Githubie repository update",
                    $"Update repository policy and commit author for '{repositoryId}'",
                    [$"Direct push: {string.Join(", ", request.DirectPushBranches)}",
                     $"Pull: {string.Join(", ", request.PullBranches)}",
                     $"Protected: {string.Join(", ", request.ProtectedBranches)}",
                     $"Tag target: {request.TagTargetBranch}",
                     $"Commit author: {request.CommitAuthorName ?? existing.CommitAuthorName ?? "repository local configuration"} <{request.CommitAuthorEmail ?? existing.CommitAuthorEmail ?? "repository local configuration"}>",
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
                CommitAuthorName = request.CommitAuthorName ?? existing.CommitAuthorName,
                CommitAuthorEmail = request.CommitAuthorEmail ?? existing.CommitAuthorEmail,
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

    public async Task<RepositoryMutationResult> SetMoyaiProjectAsync(
        string repositoryId, Guid moyaiProjectId, CancellationToken cancellationToken)
    {
        if (!RepositoryId.TryNormalize(repositoryId, out repositoryId))
            return RepositoryMutationResult.Failure(RepositoryMutationError.InvalidRepositoryId);
        if (moyaiProjectId == Guid.Empty)
            return RepositoryMutationResult.Failure(RepositoryMutationError.InvalidMoyaiProjectId);

        await _mutationLock.WaitAsync(cancellationToken);
        try
        {
            if (!allowlist.TryGet(repositoryId, out var existing))
                return RepositoryMutationResult.Failure(RepositoryMutationError.RepositoryNotRegistered);
            if (_moyaiProjects.CheckAssignable(repositoryId, moyaiProjectId) is not null)
                return RepositoryMutationResult.Failure(RepositoryMutationError.DuplicateMoyaiProjectId);
            if (existing.MoyaiProjectId == moyaiProjectId)
                return RepositoryMutationResult.Success(new RepositoryMutationInfo(true, repositoryId));

            var approval = await approvalPrompt.RequestApprovalAsync(
                new ApprovalPromptRequest(
                    "Githubie Moyai project mapping",
                    $"Map '{repositoryId}' ({existing.GitHubOwner}/{existing.GitHubRepo}) to a Moyai project",
                    [$"Moyai Project: {moyaiProjectId}",
                     $"Previous: {existing.MoyaiProjectId?.ToString() ?? "not mapped"}",
                     "Moyai requests for this repository are accepted after approval without a service restart."]),
                ApprovalTimeout,
                cancellationToken);
            var approvalError = MapApprovalError(approval.Outcome);
            if (approvalError is not null) return RepositoryMutationResult.Failure(approvalError.Value);

            var updated = existing with { MoyaiProjectId = moyaiProjectId };
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
        if (!RepositoryId.TryNormalize(repositoryId, out repositoryId))
            return RepositoryMutationResult.Failure(RepositoryMutationError.InvalidRepositoryId);
        await _mutationLock.WaitAsync(cancellationToken);
        try
        {
            if (!allowlist.TryGet(repositoryId, out _))
                return RepositoryMutationResult.Failure(RepositoryMutationError.RepositoryNotRegistered);
            try
            {
                if (atomicManagementStore is not null) await atomicManagementStore.DeleteRegistrationAsync(repositoryId, cancellationToken);
                else await configurationStore.DeleteRepositoryAsync(repositoryId, cancellationToken);
            }
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
        if (!RepositoryId.TryNormalize(oldRepositoryId, out oldRepositoryId)
            || !RepositoryId.TryNormalize(newRepositoryId, out newRepositoryId))
            return RepositoryMutationResult.Failure(RepositoryMutationError.InvalidRepositoryId);
        await _mutationLock.WaitAsync(cancellationToken);
        try
        {
            if (!allowlist.TryGet(oldRepositoryId, out _))
                return RepositoryMutationResult.Failure(RepositoryMutationError.RepositoryNotRegistered);
            if (allowlist.TryGet(newRepositoryId, out _))
                return RepositoryMutationResult.Failure(RepositoryMutationError.DuplicateRepositoryId);

            if (atomicManagementStore is not null)
            {
                try { await atomicManagementStore.RenameRegistrationAsync(oldRepositoryId, newRepositoryId, cancellationToken); }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                { return RepositoryMutationResult.Failure(RepositoryMutationError.PersistenceFailed); }
                if (!allowlist.TryRename(oldRepositoryId, newRepositoryId))
                    throw new InvalidOperationException("The committed repository rename could not be published.");
                return RepositoryMutationResult.Success(new RepositoryMutationInfo(false, newRepositoryId));
            }
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
