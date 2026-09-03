using Githubie.Application.Repositories;
using System.Collections.Concurrent;

namespace Githubie.Application.GitHub;

/// <summary>
/// Repository Allowlistの解決とRepository Policyの適用を行ったうえで<see cref="IGitHubApiClient"/>を呼び出します。
/// PRのsource/destinationおよびTagの対象branchはAgentに自由指定させず、設定から決定します。
/// </summary>
public sealed class GitHubRepositoryGateway(
    RepositoryAllowlist allowlist,
    IGitHubApiClient apiClient,
    TimeProvider? timeProvider = null,
    TimeSpan? mergeabilityPollInterval = null) : IGitHubRepositoryGateway
{
    private static readonly ConcurrentDictionary<string, SemaphoreSlim> WorkflowLocks = new(StringComparer.Ordinal);
    private readonly RepositoryAllowlist _allowlist = allowlist;
    private readonly IGitHubApiClient _apiClient = apiClient;
    private readonly TimeProvider _timeProvider = timeProvider ?? TimeProvider.System;
    private readonly TimeSpan _mergeabilityPollInterval = mergeabilityPollInterval ?? TimeSpan.FromSeconds(2);
    private const int MergeabilityPollAttempts = 3;

    public async Task<GitHubResult<GitHubRepositoryInfo>> GetRepositoryAsync(string repository, CancellationToken cancellationToken)
    {
        var resolved = Resolve(repository);
        if (resolved.Error is not null) return GitHubResult<GitHubRepositoryInfo>.Failure(resolved.Error.Value);
        var options = resolved.Options!;
        return await _apiClient.GetRepositoryAsync(repository, options.GitHubOwner, options.GitHubRepo, cancellationToken);
    }

    public async Task<GitHubResult<GitHubRepositoryInfo>> UpdateRepositoryDescriptionAsync(
        string repository, string description, CancellationToken cancellationToken)
    {
        if (description.EnumerateRunes().Count() > 350)
            return GitHubResult<GitHubRepositoryInfo>.Failure(GitHubError.RepositoryDescriptionInvalid);

        var resolved = Resolve(repository);
        if (resolved.Error is not null) return GitHubResult<GitHubRepositoryInfo>.Failure(resolved.Error.Value);
        var options = resolved.Options!;
        return await _apiClient.UpdateRepositoryDescriptionAsync(
            repository, options.GitHubOwner, options.GitHubRepo, description, cancellationToken);
    }

    public async Task<GitHubResult<GitHubWorkflowDispatchInfo>> DispatchWorkflowAsync(
        string repository, GitHubWorkflowDispatchRequest request, CancellationToken cancellationToken)
    {
        var resolved = Resolve(repository);
        if (resolved.Error is not null) return GitHubResult<GitHubWorkflowDispatchInfo>.Failure(resolved.Error.Value);
        var options = resolved.Options!;
        if (!options.Workflows.TryGetValue(request.Workflow, out var policy))
            return GitHubResult<GitHubWorkflowDispatchInfo>.Failure(GitHubError.WorkflowNotAllowed);
        if (!policy.AllowedRefs.Contains(request.Ref, StringComparer.Ordinal))
            return GitHubResult<GitHubWorkflowDispatchInfo>.Failure(GitHubError.WorkflowRefNotAllowed);
        if (!ValidateWorkflowInputs(request.Inputs, policy.Inputs))
            return GitHubResult<GitHubWorkflowDispatchInfo>.Failure(GitHubError.WorkflowInputInvalid);

        var gate = WorkflowLocks.GetOrAdd($"{repository}\n{request.Workflow}", _ => new SemaphoreSlim(Math.Max(1, policy.MaxConcurrent)));
        if (!await gate.WaitAsync(0, cancellationToken))
            return GitHubResult<GitHubWorkflowDispatchInfo>.Failure(GitHubError.WorkflowConcurrencyLimit);
        try
        {
            var before = await _apiClient.ListWorkflowRunsAsync(
                repository, options.GitHubOwner, options.GitHubRepo, request.Workflow, request.Ref,
                "workflow_dispatch", null, 20, cancellationToken);
            if (!before.IsSuccess) return GitHubResult<GitHubWorkflowDispatchInfo>.Failure(before.Error!.Value);
            var previousIds = before.Value!.Select(x => x.Id).ToHashSet();
            var dispatchedAt = DateTimeOffset.UtcNow;
            var dispatched = await _apiClient.DispatchWorkflowAsync(
                repository, options.GitHubOwner, options.GitHubRepo, request, cancellationToken);
            if (!dispatched.IsSuccess) return GitHubResult<GitHubWorkflowDispatchInfo>.Failure(dispatched.Error!.Value);

            var timeout = TimeSpan.FromSeconds(Math.Clamp(policy.CorrelationTimeoutSeconds, 1, 120));
            var deadline = DateTimeOffset.UtcNow + timeout;
            do
            {
                var after = await _apiClient.ListWorkflowRunsAsync(
                    repository, options.GitHubOwner, options.GitHubRepo, request.Workflow, request.Ref,
                    "workflow_dispatch", null, 20, cancellationToken);
                if (!after.IsSuccess) return GitHubResult<GitHubWorkflowDispatchInfo>.Failure(after.Error!.Value);
                var candidates = after.Value!.Where(x => !previousIds.Contains(x.Id) && x.CreatedAt >= dispatchedAt.AddSeconds(-2)).ToArray();
                if (candidates.Length == 1)
                    return GitHubResult<GitHubWorkflowDispatchInfo>.Success(
                        new(request.Workflow, request.Ref, dispatchedAt, candidates[0]));
                if (candidates.Length > 1)
                    return GitHubResult<GitHubWorkflowDispatchInfo>.Failure(GitHubError.WorkflowRunCorrelationFailed);
                await Task.Delay(TimeSpan.FromSeconds(1), cancellationToken);
            } while (DateTimeOffset.UtcNow < deadline);
            return GitHubResult<GitHubWorkflowDispatchInfo>.Failure(GitHubError.WorkflowRunCorrelationFailed);
        }
        finally { gate.Release(); }
    }

    public async Task<GitHubResult<GitHubWorkflowRunInfo>> GetWorkflowRunAsync(
        string repository, long runId, CancellationToken cancellationToken)
    {
        var resolved = Resolve(repository);
        if (resolved.Error is not null) return GitHubResult<GitHubWorkflowRunInfo>.Failure(resolved.Error.Value);
        var options = resolved.Options!;
        return await _apiClient.GetWorkflowRunAsync(repository, options.GitHubOwner, options.GitHubRepo, runId, cancellationToken);
    }

    public async Task<GitHubResult<IReadOnlyList<GitHubWorkflowRunInfo>>> ListWorkflowRunsAsync(
        string repository, string? workflow, string? branch, string? eventName, string? status,
        int limit, CancellationToken cancellationToken)
    {
        if (limit is < 1 or > 100) return GitHubResult<IReadOnlyList<GitHubWorkflowRunInfo>>.Failure(GitHubError.WorkflowInputInvalid);
        var resolved = Resolve(repository);
        if (resolved.Error is not null) return GitHubResult<IReadOnlyList<GitHubWorkflowRunInfo>>.Failure(resolved.Error.Value);
        var options = resolved.Options!;
        if (workflow is not null && !options.Workflows.ContainsKey(workflow))
            return GitHubResult<IReadOnlyList<GitHubWorkflowRunInfo>>.Failure(GitHubError.WorkflowNotAllowed);
        var safeBranch = branch is null || options.PullBranches.Contains(branch, StringComparer.Ordinal);
        if (!safeBranch) return GitHubResult<IReadOnlyList<GitHubWorkflowRunInfo>>.Failure(GitHubError.WorkflowRefNotAllowed);
        return await _apiClient.ListWorkflowRunsAsync(
            repository, options.GitHubOwner, options.GitHubRepo, workflow, branch, eventName, status, limit, cancellationToken);
    }

    private static bool ValidateWorkflowInputs(
        IReadOnlyDictionary<string, string> values,
        IReadOnlyDictionary<string, Configuration.WorkflowInputPolicyOptions> schema)
    {
        if (values.Keys.Any(key => !schema.ContainsKey(key))) return false;
        foreach (var (key, rule) in schema)
        {
            if (rule.Required && (!values.TryGetValue(key, out var requiredValue) || string.IsNullOrEmpty(requiredValue))) return false;
            if (!values.TryGetValue(key, out var value)) continue;
            if (value.EnumerateRunes().Count() > rule.MaxLength || rule.MaxLength is < 1 or > 4096) return false;
            if (rule.Type == "boolean" && !bool.TryParse(value, out _)) return false;
            if (rule.Type == "integer" && !long.TryParse(value, System.Globalization.NumberStyles.Integer, System.Globalization.CultureInfo.InvariantCulture, out _)) return false;
            if (rule.Type is not ("string" or "boolean" or "integer")) return false;
        }
        return true;
    }

    public async Task<GitHubResult<IReadOnlyList<GitHubBranchInfo>>> ListBranchesAsync(string repository, CancellationToken cancellationToken)
    {
        var resolved = Resolve(repository);
        if (resolved.Error is not null)
        {
            return GitHubResult<IReadOnlyList<GitHubBranchInfo>>.Failure(resolved.Error.Value);
        }

        return await _apiClient.ListBranchesAsync(repository, resolved.Options!.GitHubOwner, resolved.Options.GitHubRepo, cancellationToken);
    }

    public async Task<GitHubResult<GitHubBranchInfo>> GetBranchAsync(string repository, string branch, CancellationToken cancellationToken)
    {
        var resolved = Resolve(repository);
        if (resolved.Error is not null)
        {
            return GitHubResult<GitHubBranchInfo>.Failure(resolved.Error.Value);
        }

        return await _apiClient.GetBranchAsync(repository, resolved.Options!.GitHubOwner, resolved.Options.GitHubRepo, branch, cancellationToken);
    }

    public async Task<GitHubResult<GitHubBranchInfo>> CreateBranchAsync(
        string repository, string branch, string source, CancellationToken cancellationToken)
    {
        var resolved = Resolve(repository);
        if (resolved.Error is not null) return GitHubResult<GitHubBranchInfo>.Failure(resolved.Error.Value);
        var options = resolved.Options!;
        if (!IsAllowedBranch(options, branch)) return GitHubResult<GitHubBranchInfo>.Failure(GitHubError.BranchNotAllowed);
        if (string.IsNullOrWhiteSpace(source)) return GitHubResult<GitHubBranchInfo>.Failure(GitHubError.BranchSourceInvalid);
        string sourceSha;
        if (source.Length == 40 && source.All(Uri.IsHexDigit))
        {
            var commit = await _apiClient.GetCommitShaAsync(repository, options.GitHubOwner, options.GitHubRepo, source, cancellationToken);
            if (!commit.IsSuccess) return GitHubResult<GitHubBranchInfo>.Failure(commit.Error!.Value);
            sourceSha = commit.Value!;
        }
        else
        {
            var sourceBranch = await _apiClient.GetBranchAsync(repository, options.GitHubOwner, options.GitHubRepo, source, cancellationToken);
            if (!sourceBranch.IsSuccess) return GitHubResult<GitHubBranchInfo>.Failure(sourceBranch.Error!.Value);
            sourceSha = sourceBranch.Value!.HeadSha;
        }
        return await _apiClient.CreateBranchAsync(
            repository, options.GitHubOwner, options.GitHubRepo, branch, sourceSha, cancellationToken);
    }

    public async Task<GitHubResult<bool>> DeleteBranchAsync(
        string repository, string branch, CancellationToken cancellationToken)
    {
        var resolved = Resolve(repository);
        if (resolved.Error is not null) return GitHubResult<bool>.Failure(resolved.Error.Value);
        var options = resolved.Options!;
        if (options.ProtectedBranches.Contains(branch, StringComparer.Ordinal))
            return GitHubResult<bool>.Failure(GitHubError.ProtectedBranch);
        if (!IsAllowedBranch(options, branch)) return GitHubResult<bool>.Failure(GitHubError.BranchNotAllowed);
        return await _apiClient.DeleteBranchAsync(
            repository, options.GitHubOwner, options.GitHubRepo, branch, cancellationToken);
    }

    public async Task<GitHubResult<IReadOnlyList<GitHubPullRequestInfo>>> ListPullRequestsAsync(
        string repository, GitHubPullRequestState? state, string? source, string? destination, CancellationToken cancellationToken)
    {
        var resolved = Resolve(repository);
        if (resolved.Error is not null)
        {
            return GitHubResult<IReadOnlyList<GitHubPullRequestInfo>>.Failure(resolved.Error.Value);
        }

        return await _apiClient.ListPullRequestsAsync(repository, resolved.Options!.GitHubOwner, resolved.Options.GitHubRepo, state, source, destination, cancellationToken);
    }

    public async Task<GitHubResult<GitHubPullRequestInfo>> GetPullRequestAsync(string repository, int number, CancellationToken cancellationToken)
    {
        var resolved = Resolve(repository);
        if (resolved.Error is not null)
        {
            return GitHubResult<GitHubPullRequestInfo>.Failure(resolved.Error.Value);
        }

        return await _apiClient.GetPullRequestAsync(repository, resolved.Options!.GitHubOwner, resolved.Options.GitHubRepo, number, cancellationToken);
    }

    public async Task<GitHubResult<IReadOnlyList<GitHubIssueInfo>>> ListIssuesAsync(
        string repository, GitHubIssueState? state, CancellationToken cancellationToken)
    {
        var resolved = Resolve(repository);
        if (resolved.Error is not null)
            return GitHubResult<IReadOnlyList<GitHubIssueInfo>>.Failure(resolved.Error.Value);

        return await _apiClient.ListIssuesAsync(
            repository, resolved.Options!.GitHubOwner, resolved.Options.GitHubRepo, state, cancellationToken);
    }

    public async Task<GitHubResult<GitHubIssueInfo>> GetIssueAsync(
        string repository, int number, CancellationToken cancellationToken)
    {
        var resolved = Resolve(repository);
        if (resolved.Error is not null) return GitHubResult<GitHubIssueInfo>.Failure(resolved.Error.Value);
        return await _apiClient.GetIssueAsync(
            repository, resolved.Options!.GitHubOwner, resolved.Options.GitHubRepo, number, cancellationToken);
    }

    public async Task<GitHubResult<GitHubPullRequestDiff>> GetPullRequestDiffAsync(string repository, int number, CancellationToken cancellationToken)
    {
        var resolved = Resolve(repository);
        if (resolved.Error is not null)
        {
            return GitHubResult<GitHubPullRequestDiff>.Failure(resolved.Error.Value);
        }

        return await _apiClient.GetPullRequestDiffAsync(repository, resolved.Options!.GitHubOwner, resolved.Options.GitHubRepo, number, cancellationToken);
    }

    public async Task<GitHubResult<GitHubPullRequestInfo>> CreatePullRequestAsync(
        string repository, GitHubPullRequestCreate request, CancellationToken cancellationToken)
    {
        var resolved = Resolve(repository);
        if (resolved.Error is not null)
        {
            return GitHubResult<GitHubPullRequestInfo>.Failure(resolved.Error.Value);
        }

        var options = resolved.Options!;
        var policy = options.ToPolicy(repository);
        var routeResult = policy.ValidatePullRequest(options.DevelopBranch, options.MainBranch);
        if (!routeResult.IsAllowed)
        {
            return GitHubResult<GitHubPullRequestInfo>.Failure(GitHubError.PullRequestRouteNotAllowed);
        }

        return await _apiClient.CreatePullRequestAsync(
            repository, options.GitHubOwner, options.GitHubRepo, options.DevelopBranch, options.MainBranch, request, cancellationToken);
    }

    public async Task<GitHubResult<GitHubPullRequestInfo>> MergePullRequestAsync(
        string repository, GitHubPullRequestMerge request, CancellationToken cancellationToken)
    {
        var resolved = Resolve(repository);
        if (resolved.Error is not null)
        {
            return GitHubResult<GitHubPullRequestInfo>.Failure(resolved.Error.Value);
        }

        var options = resolved.Options!;

        var current = await GetStableMergeabilityAsync(
            repository, options.GitHubOwner, options.GitHubRepo, request.Number, cancellationToken);
        if (!current.IsSuccess)
        {
            return current;
        }

        if (current.Value!.State != GitHubPullRequestState.Open)
        {
            return GitHubResult<GitHubPullRequestInfo>.Failure(GitHubError.PullRequestNotOpen);
        }

        var mergeabilityError = MapMergeabilityError(current.Value.MergeabilityStatus);
        if (mergeabilityError is not null)
        {
            return GitHubResult<GitHubPullRequestInfo>.Failure(mergeabilityError.Value);
        }

        var policy = options.ToPolicy(repository);
        var routeResult = policy.ValidatePullRequest(current.Value.Source, current.Value.Destination);
        if (!routeResult.IsAllowed)
        {
            return GitHubResult<GitHubPullRequestInfo>.Failure(GitHubError.PullRequestRouteNotAllowed);
        }

        var merged = await _apiClient.MergePullRequestAsync(
            repository, options.GitHubOwner, options.GitHubRepo, request, cancellationToken);
        if (merged.Error != GitHubError.PullRequestNotMergeable)
        {
            return merged;
        }

        var refreshed = await _apiClient.GetPullRequestAsync(
            repository, options.GitHubOwner, options.GitHubRepo, request.Number, cancellationToken);
        if (!refreshed.IsSuccess)
        {
            return refreshed;
        }

        var refreshedError = MapMergeabilityError(refreshed.Value!.MergeabilityStatus);
        return GitHubResult<GitHubPullRequestInfo>.Failure(
            refreshedError ?? GitHubError.MergeabilityUnknownRetryable);
    }

    private async Task<GitHubResult<GitHubPullRequestInfo>> GetStableMergeabilityAsync(
        string repository, string owner, string repo, int number, CancellationToken cancellationToken)
    {
        GitHubResult<GitHubPullRequestInfo>? current = null;
        for (var attempt = 0; attempt < MergeabilityPollAttempts; attempt++)
        {
            current = await _apiClient.GetPullRequestAsync(repository, owner, repo, number, cancellationToken);
            if (!current.IsSuccess || current.Value!.MergeabilityStatus is not
                (GitHubMergeabilityStatus.CalculatingRetryable or GitHubMergeabilityStatus.UnknownRetryable))
            {
                return current;
            }
            if (attempt < MergeabilityPollAttempts - 1)
            {
                await Task.Delay(_mergeabilityPollInterval, _timeProvider, cancellationToken);
            }
        }
        return current!;
    }

    private static GitHubError? MapMergeabilityError(string status) => status switch
    {
        GitHubMergeabilityStatus.CalculatingRetryable => GitHubError.MergeabilityCalculating,
        GitHubMergeabilityStatus.UnknownRetryable => GitHubError.MergeabilityUnknownRetryable,
        GitHubMergeabilityStatus.Conflicting => GitHubError.PullRequestNotMergeable,
        GitHubMergeabilityStatus.Blocked => GitHubError.PullRequestBlocked,
        _ => null,
    };

    public Task<GitHubResult<GitHubPullRequestInfo>> ClosePullRequestAsync(
        string repository, int number, CancellationToken cancellationToken) =>
        ChangePullRequestStateAsync(repository, number, GitHubPullRequestState.Closed, cancellationToken);

    public Task<GitHubResult<GitHubPullRequestInfo>> ReopenPullRequestAsync(
        string repository, int number, CancellationToken cancellationToken) =>
        ChangePullRequestStateAsync(repository, number, GitHubPullRequestState.Open, cancellationToken);

    public async Task<GitHubResult<IReadOnlyList<GitHubPullRequestComment>>> ListPullRequestCommentsAsync(
        string repository, int number, CancellationToken cancellationToken)
    {
        var resolved = Resolve(repository);
        if (resolved.Error is not null) return GitHubResult<IReadOnlyList<GitHubPullRequestComment>>.Failure(resolved.Error.Value);
        var options = resolved.Options!;
        var pullRequest = await _apiClient.GetPullRequestAsync(
            repository, options.GitHubOwner, options.GitHubRepo, number, cancellationToken);
        if (!pullRequest.IsSuccess) return GitHubResult<IReadOnlyList<GitHubPullRequestComment>>.Failure(pullRequest.Error!.Value);
        return await _apiClient.ListPullRequestCommentsAsync(
            repository, options.GitHubOwner, options.GitHubRepo, number, cancellationToken);
    }

    public async Task<GitHubResult<GitHubPullRequestComment>> CreatePullRequestCommentAsync(
        string repository, int number, string body, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(body) || body.Length > 65536)
            return GitHubResult<GitHubPullRequestComment>.Failure(GitHubError.PullRequestCommentInvalid);
        var resolved = Resolve(repository);
        if (resolved.Error is not null) return GitHubResult<GitHubPullRequestComment>.Failure(resolved.Error.Value);
        var options = resolved.Options!;
        var pullRequest = await _apiClient.GetPullRequestAsync(
            repository, options.GitHubOwner, options.GitHubRepo, number, cancellationToken);
        if (!pullRequest.IsSuccess) return GitHubResult<GitHubPullRequestComment>.Failure(pullRequest.Error!.Value);
        return await _apiClient.CreatePullRequestCommentAsync(
            repository, options.GitHubOwner, options.GitHubRepo, number, body, cancellationToken);
    }

    public Task<GitHubResult<GitHubPullRequestReview>> ApprovePullRequestAsync(
        string repository, int number, string? body, CancellationToken cancellationToken) =>
        CreatePullRequestReviewAsync(repository, number, GitHubPullRequestReviewAction.Approve, body, cancellationToken);

    public Task<GitHubResult<GitHubPullRequestReview>> RequestPullRequestChangesAsync(
        string repository, int number, string body, CancellationToken cancellationToken) =>
        CreatePullRequestReviewAsync(repository, number, GitHubPullRequestReviewAction.RequestChanges, body, cancellationToken);

    private async Task<GitHubResult<GitHubPullRequestReview>> CreatePullRequestReviewAsync(
        string repository, int number, GitHubPullRequestReviewAction action, string? body, CancellationToken cancellationToken)
    {
        if (body?.Length > 65536 || action == GitHubPullRequestReviewAction.RequestChanges && string.IsNullOrWhiteSpace(body))
            return GitHubResult<GitHubPullRequestReview>.Failure(GitHubError.PullRequestReviewInvalid);
        var resolved = Resolve(repository);
        if (resolved.Error is not null) return GitHubResult<GitHubPullRequestReview>.Failure(resolved.Error.Value);
        var options = resolved.Options!;
        var pullRequest = await _apiClient.GetPullRequestAsync(
            repository, options.GitHubOwner, options.GitHubRepo, number, cancellationToken);
        if (!pullRequest.IsSuccess) return GitHubResult<GitHubPullRequestReview>.Failure(pullRequest.Error!.Value);
        if (pullRequest.Value!.State != GitHubPullRequestState.Open)
            return GitHubResult<GitHubPullRequestReview>.Failure(GitHubError.PullRequestNotOpen);
        return await _apiClient.CreatePullRequestReviewAsync(
            repository, options.GitHubOwner, options.GitHubRepo, number, action, body, cancellationToken);
    }

    private async Task<GitHubResult<GitHubPullRequestInfo>> ChangePullRequestStateAsync(
        string repository, int number, GitHubPullRequestState target, CancellationToken cancellationToken)
    {
        var resolved = Resolve(repository);
        if (resolved.Error is not null) return GitHubResult<GitHubPullRequestInfo>.Failure(resolved.Error.Value);
        var options = resolved.Options!;
        var current = await _apiClient.GetPullRequestAsync(
            repository, options.GitHubOwner, options.GitHubRepo, number, cancellationToken);
        if (!current.IsSuccess) return current;
        if (current.Value!.State == GitHubPullRequestState.Merged)
            return GitHubResult<GitHubPullRequestInfo>.Failure(GitHubError.PullRequestStateNotAllowed);
        if (current.Value.State == target) return current;
        return await _apiClient.UpdatePullRequestStateAsync(
            repository, options.GitHubOwner, options.GitHubRepo, number, target, cancellationToken);
    }

    public async Task<GitHubResult<IReadOnlyList<GitHubTagInfo>>> ListTagsAsync(string repository, CancellationToken cancellationToken)
    {
        var resolved = Resolve(repository);
        if (resolved.Error is not null)
        {
            return GitHubResult<IReadOnlyList<GitHubTagInfo>>.Failure(resolved.Error.Value);
        }

        return await _apiClient.ListTagsAsync(repository, resolved.Options!.GitHubOwner, resolved.Options.GitHubRepo, cancellationToken);
    }

    public async Task<GitHubResult<GitHubTagInfo>> GetTagAsync(string repository, string tag, CancellationToken cancellationToken)
    {
        var resolved = Resolve(repository);
        if (resolved.Error is not null)
        {
            return GitHubResult<GitHubTagInfo>.Failure(resolved.Error.Value);
        }

        return await _apiClient.GetTagAsync(repository, resolved.Options!.GitHubOwner, resolved.Options.GitHubRepo, tag, cancellationToken);
    }

    public async Task<GitHubResult<GitHubTagInfo>> CreateTagAsync(
        string repository, string tag, string source, string? message, CancellationToken cancellationToken)
    {
        var resolved = Resolve(repository);
        if (resolved.Error is not null)
        {
            return GitHubResult<GitHubTagInfo>.Failure(resolved.Error.Value);
        }

        var options = resolved.Options!;
        var policy = options.ToPolicy(repository);
        var tagPolicyResult = policy.ValidateTag(tag, options.TagTargetBranch);
        if (!tagPolicyResult.IsAllowed)
        {
            return GitHubResult<GitHubTagInfo>.Failure(MapPolicyError(tagPolicyResult.ErrorCode!.Value));
        }

        if (string.IsNullOrWhiteSpace(source))
        {
            return GitHubResult<GitHubTagInfo>.Failure(GitHubError.TagSourceInvalid);
        }

        string targetCommitSha;
        if (source.Length == 40 && source.All(Uri.IsHexDigit))
        {
            var commit = await _apiClient.GetCommitShaAsync(
                repository, options.GitHubOwner, options.GitHubRepo, source, cancellationToken);
            if (!commit.IsSuccess)
            {
                return GitHubResult<GitHubTagInfo>.Failure(MapTagSourceError(commit.Error!.Value));
            }

            targetCommitSha = commit.Value!;
        }
        else
        {
            if (!IsValidBranchSource(source))
            {
                return GitHubResult<GitHubTagInfo>.Failure(GitHubError.TagSourceInvalid);
            }

            var targetBranch = await _apiClient.GetBranchAsync(
                repository, options.GitHubOwner, options.GitHubRepo, source, cancellationToken);
            if (!targetBranch.IsSuccess)
            {
                return GitHubResult<GitHubTagInfo>.Failure(MapTagSourceError(targetBranch.Error!.Value));
            }

            targetCommitSha = targetBranch.Value!.HeadSha;
        }

        return await _apiClient.CreateTagAsync(
            repository,
            options.GitHubOwner,
            options.GitHubRepo,
            new GitHubTagCreate(tag, targetCommitSha, message),
            cancellationToken);
    }

    public async Task<GitHubResult<bool>> DeleteTagAsync(string repository, string tag, CancellationToken cancellationToken)
    {
        var resolved = Resolve(repository);
        if (resolved.Error is not null) return GitHubResult<bool>.Failure(resolved.Error.Value);
        var options = resolved.Options!;
        var policy = options.ToPolicy(repository).ValidateTag(tag, options.TagTargetBranch);
        if (!policy.IsAllowed) return GitHubResult<bool>.Failure(MapPolicyError(policy.ErrorCode!.Value));
        return await _apiClient.DeleteTagAsync(repository, options.GitHubOwner, options.GitHubRepo, tag, cancellationToken);
    }

    public async Task<GitHubResult<IReadOnlyList<GitHubReleaseInfo>>> ListReleasesAsync(string repository, CancellationToken cancellationToken)
    {
        var resolved = Resolve(repository);
        if (resolved.Error is not null) return GitHubResult<IReadOnlyList<GitHubReleaseInfo>>.Failure(resolved.Error.Value);
        var options = resolved.Options!;
        return await _apiClient.ListReleasesAsync(repository, options.GitHubOwner, options.GitHubRepo, cancellationToken);
    }

    public async Task<GitHubResult<GitHubReleaseInfo>> GetReleaseAsync(string repository, string tag, CancellationToken cancellationToken)
    {
        var resolved = Resolve(repository);
        if (resolved.Error is not null) return GitHubResult<GitHubReleaseInfo>.Failure(resolved.Error.Value);
        var options = resolved.Options!;
        return await _apiClient.GetReleaseAsync(repository, options.GitHubOwner, options.GitHubRepo, tag, cancellationToken);
    }

    public async Task<GitHubResult<GitHubReleaseInfo>> UpdateReleaseAsync(
        string repository, long releaseId, GitHubReleaseUpdate request, CancellationToken cancellationToken)
    {
        var resolved = Resolve(repository);
        if (resolved.Error is not null) return GitHubResult<GitHubReleaseInfo>.Failure(resolved.Error.Value);
        var options = resolved.Options!;
        return await _apiClient.UpdateReleaseAsync(repository, options.GitHubOwner, options.GitHubRepo, releaseId, request, cancellationToken);
    }

    public async Task<GitHubResult<bool>> DeleteReleaseAsync(
        string repository, long releaseId, CancellationToken cancellationToken)
    {
        var resolved = Resolve(repository);
        if (resolved.Error is not null) return GitHubResult<bool>.Failure(resolved.Error.Value);
        var options = resolved.Options!;
        return await _apiClient.DeleteReleaseAsync(repository, options.GitHubOwner, options.GitHubRepo, releaseId, cancellationToken);
    }

    public async Task<GitHubResult<GitHubReleaseInfo>> UploadReleaseAssetsAsync(
        string repository, GitHubReleaseAssetUpload request, CancellationToken cancellationToken)
    {
        var resolved = Resolve(repository);
        if (resolved.Error is not null) return GitHubResult<GitHubReleaseInfo>.Failure(resolved.Error.Value);
        var options = resolved.Options!;
        return await _apiClient.UploadReleaseAssetsAsync(
            repository, options.GitHubOwner, options.GitHubRepo, options.LocalRoot, request, cancellationToken);
    }

    public async Task<GitHubResult<GitHubReleaseInfo>> CreateReleaseAsync(
        string repository,
        GitHubReleaseCreate request,
        CancellationToken cancellationToken)
    {
        var resolved = Resolve(repository);
        if (resolved.Error is not null) return GitHubResult<GitHubReleaseInfo>.Failure(resolved.Error.Value);

        var options = resolved.Options!;
        var policy = options.ToPolicy(repository);
        var tagPolicy = policy.ValidateTag(request.Tag, options.TagTargetBranch);
        if (!tagPolicy.IsAllowed) return GitHubResult<GitHubReleaseInfo>.Failure(MapPolicyError(tagPolicy.ErrorCode!.Value));

        var tag = await _apiClient.GetTagAsync(repository, options.GitHubOwner, options.GitHubRepo, request.Tag, cancellationToken);
        if (!tag.IsSuccess) return GitHubResult<GitHubReleaseInfo>.Failure(tag.Error!.Value);
        var main = await _apiClient.GetBranchAsync(repository, options.GitHubOwner, options.GitHubRepo, options.TagTargetBranch, cancellationToken);
        if (!main.IsSuccess) return GitHubResult<GitHubReleaseInfo>.Failure(main.Error!.Value);
        if (!string.Equals(tag.Value!.TargetCommitSha, main.Value!.HeadSha, StringComparison.OrdinalIgnoreCase))
            return GitHubResult<GitHubReleaseInfo>.Failure(GitHubError.TagTargetNotAllowed);

        return await _apiClient.CreateReleaseAsync(
            repository, options.GitHubOwner, options.GitHubRepo, options.LocalRoot, request, cancellationToken);
    }

    private static bool IsAllowedBranch(Configuration.RepositoryOptions options, string branch) =>
        options.DirectPushBranches.Contains(branch, StringComparer.Ordinal) ||
        options.PullBranches.Contains(branch, StringComparer.Ordinal);

    private static bool IsValidBranchSource(string source) =>
        source.Length <= 255 &&
        source is not "." &&
        !source.StartsWith('/') &&
        !source.EndsWith('/') &&
        !source.EndsWith('.') &&
        !source.Contains("..", StringComparison.Ordinal) &&
        !source.Contains("@{", StringComparison.Ordinal) &&
        !source.Split('/').Any(segment => segment.StartsWith('.') || segment.EndsWith(".lock", StringComparison.OrdinalIgnoreCase)) &&
        !source.Any(character => char.IsControl(character) || char.IsWhiteSpace(character) || "~^:?*[\\".Contains(character));

    private static GitHubError MapTagSourceError(GitHubError error) => error switch
    {
        GitHubError.BranchNotFound or GitHubError.BranchSourceNotFound => GitHubError.TagSourceNotFound,
        _ => error,
    };

    private (Configuration.RepositoryOptions? Options, GitHubError? Error) Resolve(string repository)
    {
        if (!RepositoryId.TryNormalize(repository, out repository) || !_allowlist.TryGet(repository, out var options))
        {
            return (null, GitHubError.RepositoryNotFound);
        }

        return (options, null);
    }

    private static GitHubError MapPolicyError(Domain.PolicyErrorCode error) => error switch
    {
        Domain.PolicyErrorCode.TagInvalid => GitHubError.TagInvalid,
        Domain.PolicyErrorCode.TagTargetNotAllowed => GitHubError.TagTargetNotAllowed,
        _ => GitHubError.ApiError,
    };
}
