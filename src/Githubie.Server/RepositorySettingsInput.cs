using System.Text.Json.Serialization;
using Githubie.Application.Repositories;
using Githubie.Domain;

namespace Githubie.Server;

/// <summary>MCP input model retaining field presence so omitted and explicit null values stay distinct.</summary>
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed class RepositorySettingsInput
{
    private IReadOnlyList<string>? _directPushBranches;
    private IReadOnlyList<string>? _protectedBranches;
    private IReadOnlyList<string>? _pullBranches;
    private string? _tagPattern;
    private string? _tagTargetBranch;
    private bool? _requireCleanWorkingTree;
    private string? _commitAuthorName;
    private string? _commitAuthorEmail;
    private IReadOnlyList<PullRequestRoute>? _pullRequestRoutes;

    [JsonPropertyName("direct_push_branches")] public IReadOnlyList<string>? DirectPushBranches { get => _directPushBranches; init { _directPushBranches = value; HasDirectPushBranches = true; } }
    [JsonPropertyName("protected_branches")] public IReadOnlyList<string>? ProtectedBranches { get => _protectedBranches; init { _protectedBranches = value; HasProtectedBranches = true; } }
    [JsonPropertyName("pull_branches")] public IReadOnlyList<string>? PullBranches { get => _pullBranches; init { _pullBranches = value; HasPullBranches = true; } }
    [JsonPropertyName("tag_pattern")] public string? TagPattern { get => _tagPattern; init { _tagPattern = value; HasTagPattern = true; } }
    [JsonPropertyName("tag_target_branch")] public string? TagTargetBranch { get => _tagTargetBranch; init { _tagTargetBranch = value; HasTagTargetBranch = true; } }
    [JsonPropertyName("require_clean_working_tree")] public bool? RequireCleanWorkingTree { get => _requireCleanWorkingTree; init { _requireCleanWorkingTree = value; HasRequireCleanWorkingTree = true; } }
    [JsonPropertyName("commit_author_name")] public string? CommitAuthorName { get => _commitAuthorName; init { _commitAuthorName = value; HasCommitAuthorName = true; } }
    [JsonPropertyName("commit_author_email")] public string? CommitAuthorEmail { get => _commitAuthorEmail; init { _commitAuthorEmail = value; HasCommitAuthorEmail = true; } }
    [JsonPropertyName("pull_request_routes")] public IReadOnlyList<PullRequestRoute>? PullRequestRoutes { get => _pullRequestRoutes; init { _pullRequestRoutes = value; HasPullRequestRoutes = true; } }

    [JsonIgnore] public bool HasDirectPushBranches { get; private init; }
    [JsonIgnore] public bool HasProtectedBranches { get; private init; }
    [JsonIgnore] public bool HasPullBranches { get; private init; }
    [JsonIgnore] public bool HasTagPattern { get; private init; }
    [JsonIgnore] public bool HasTagTargetBranch { get; private init; }
    [JsonIgnore] public bool HasRequireCleanWorkingTree { get; private init; }
    [JsonIgnore] public bool HasCommitAuthorName { get; private init; }
    [JsonIgnore] public bool HasCommitAuthorEmail { get; private init; }
    [JsonIgnore] public bool HasPullRequestRoutes { get; private init; }

    public RepositorySettingsChanges? ToChanges()
    {
        if ((!HasDirectPushBranches && !HasProtectedBranches && !HasPullBranches && !HasTagPattern
                && !HasTagTargetBranch && !HasRequireCleanWorkingTree && !HasCommitAuthorName && !HasCommitAuthorEmail && !HasPullRequestRoutes)
            || (HasDirectPushBranches && DirectPushBranches is null)
            || (HasProtectedBranches && ProtectedBranches is null)
            || (HasPullBranches && PullBranches is null)
            || (HasTagPattern && TagPattern is null)
            || (HasTagTargetBranch && TagTargetBranch is null)
            || (HasRequireCleanWorkingTree && RequireCleanWorkingTree is null)
            || (HasCommitAuthorName && CommitAuthorName is null)
            || (HasCommitAuthorEmail && CommitAuthorEmail is null)
            || (HasPullRequestRoutes && PullRequestRoutes is null)
            || HasCommitAuthorName != HasCommitAuthorEmail)
            return null;

        return new(DirectPushBranches, HasDirectPushBranches, ProtectedBranches, HasProtectedBranches,
            PullBranches, HasPullBranches, TagPattern, HasTagPattern, TagTargetBranch, HasTagTargetBranch,
            RequireCleanWorkingTree, HasRequireCleanWorkingTree, CommitAuthorName, HasCommitAuthorName,
            CommitAuthorEmail, HasCommitAuthorEmail, PullRequestRoutes, HasPullRequestRoutes);
    }
}
