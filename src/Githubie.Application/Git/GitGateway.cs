using Githubie.Application.Repositories;
using Githubie.Application.Interactive;

namespace Githubie.Application.Git;

/// <summary>
/// Repository Allowlist・Local Path検証・Remote URL検証・Repository Policyを適用したうえで
/// <see cref="IGitCommandClient"/>を呼び出すアプリケーション層Gatewayです。
/// </summary>
public sealed class GitGateway(
    RepositoryAllowlist allowlist,
    LocalPathValidator localPathValidator,
    IGitCommandClient commandClient,
    IInteractiveApprovalPrompt approvalPrompt) : IGitGateway
{
    private readonly RepositoryAllowlist _allowlist = allowlist;
    private readonly LocalPathValidator _localPathValidator = localPathValidator;
    private readonly IGitCommandClient _commandClient = commandClient;
    private readonly IInteractiveApprovalPrompt _approvalPrompt = approvalPrompt;
    private static readonly TimeSpan ApprovalTimeout = TimeSpan.FromSeconds(120);

    public async Task<GitGatewayResult<GitRepositoryStatus>> GetStatusAsync(string repository, CancellationToken cancellationToken)
    {
        var resolved = Resolve(repository);
        if (resolved.Error is not null)
        {
            return GitGatewayResult<GitRepositoryStatus>.Failure(resolved.Error.Value);
        }

        var options = resolved.Options!;
        var root = options.LocalRoot;

        var branch = await _commandClient.GetCurrentBranchAsync(root, cancellationToken);
        if (!branch.IsSuccess)
        {
            return GitGatewayResult<GitRepositoryStatus>.Failure(MapCommandFailure(branch.Failure!.Value));
        }

        var head = await _commandClient.GetHeadAsync(root, cancellationToken);
        if (!head.IsSuccess)
        {
            return GitGatewayResult<GitRepositoryStatus>.Failure(MapCommandFailure(head.Failure!.Value));
        }

        var remoteDevelopHead = await _commandClient.GetRemoteHeadAsync(root, options.Remote, options.DevelopBranch, cancellationToken);
        var remoteMainHead = await _commandClient.GetRemoteHeadAsync(root, options.Remote, options.MainBranch, cancellationToken);

        var aheadBehind = await _commandClient.GetAheadBehindAsync(root, options.Remote, branch.StandardOutput, cancellationToken);
        var (ahead, behind) = ParseAheadBehind(aheadBehind.StandardOutput);

        var status = await _commandClient.GetStatusAsync(root, cancellationToken);
        if (!status.IsSuccess)
        {
            return GitGatewayResult<GitRepositoryStatus>.Failure(MapCommandFailure(status.Failure!.Value));
        }

        var snapshot = new GitRepositoryStatus(
            Repository: repository,
            LocalBranch: branch.StandardOutput,
            LocalHead: head.StandardOutput,
            RemoteDevelopHead: remoteDevelopHead.IsSuccess ? remoteDevelopHead.StandardOutput : string.Empty,
            RemoteMainHead: remoteMainHead.IsSuccess ? remoteMainHead.StandardOutput : string.Empty,
            Ahead: ahead,
            Behind: behind,
            WorkingTreeClean: status.StandardOutput.Length == 0);

        return GitGatewayResult<GitRepositoryStatus>.Success(snapshot);
    }

    public async Task<GitGatewayResult<Unit>> FetchAsync(string repository, CancellationToken cancellationToken)
    {
        var resolved = Resolve(repository);
        if (resolved.Error is not null)
        {
            return GitGatewayResult<Unit>.Failure(resolved.Error.Value);
        }

        var result = await _commandClient.FetchAsync(resolved.Options!.LocalRoot, repository, resolved.Options.Remote, cancellationToken);
        return result.IsSuccess
            ? GitGatewayResult<Unit>.Success(Unit.Value)
            : GitGatewayResult<Unit>.Failure(MapCommandFailure(result.Failure!.Value));
    }

    public async Task<GitGatewayResult<Unit>> PullAsync(string repository, string branch, CancellationToken cancellationToken)
    {
        var resolved = Resolve(repository);
        if (resolved.Error is not null)
        {
            return GitGatewayResult<Unit>.Failure(resolved.Error.Value);
        }

        var options = resolved.Options!;
        if (!options.PullBranches.Contains(branch, StringComparer.Ordinal))
        {
            return GitGatewayResult<Unit>.Failure(GitGatewayError.BranchNotAllowed);
        }

        var result = await _commandClient.PullFastForwardOnlyAsync(options.LocalRoot, repository, options.Remote, branch, cancellationToken);
        if (result.IsSuccess)
        {
            return GitGatewayResult<Unit>.Success(Unit.Value);
        }

        return result.Failure == GitCommandFailure.Failed
            ? GitGatewayResult<Unit>.Failure(GitGatewayError.NonFastForward)
            : GitGatewayResult<Unit>.Failure(MapCommandFailure(result.Failure!.Value));
    }

    public async Task<GitGatewayResult<Unit>> PushAsync(string repository, CancellationToken cancellationToken)
    {
        var resolved = Resolve(repository);
        if (resolved.Error is not null)
        {
            return GitGatewayResult<Unit>.Failure(resolved.Error.Value);
        }

        var options = resolved.Options!;
        var root = options.LocalRoot;

        var branch = await _commandClient.GetCurrentBranchAsync(root, cancellationToken);
        if (!branch.IsSuccess)
        {
            return GitGatewayResult<Unit>.Failure(MapCommandFailure(branch.Failure!.Value));
        }

        var remoteUrl = await _commandClient.GetRemoteUrlAsync(root, options.Remote, cancellationToken);
        if (!remoteUrl.IsSuccess)
        {
            return GitGatewayResult<Unit>.Failure(MapCommandFailure(remoteUrl.Failure!.Value));
        }

        if (!GitHubRemoteUrlValidator.IsExpectedRemote(remoteUrl.StandardOutput, options.GitHubOwner, options.GitHubRepo))
        {
            return GitGatewayResult<Unit>.Failure(GitGatewayError.RemoteMismatch);
        }

        var policy = options.ToPolicy(repository);

        var status = await _commandClient.GetStatusAsync(root, cancellationToken);
        var workingTreeClean = status.IsSuccess && status.StandardOutput.Length == 0;

        var policyResult = policy.ValidatePush(branch.StandardOutput, workingTreeClean);
        if (!policyResult.IsAllowed)
        {
            return GitGatewayResult<Unit>.Failure(MapPolicyError(policyResult.ErrorCode!.Value));
        }

        var aheadBehind = await _commandClient.GetAheadBehindAsync(root, options.Remote, branch.StandardOutput, cancellationToken);
        if (!aheadBehind.IsSuccess)
        {
            return GitGatewayResult<Unit>.Failure(MapCommandFailure(aheadBehind.Failure!.Value));
        }

        var (ahead, _) = ParseAheadBehind(aheadBehind.StandardOutput);
        if (ahead == 0)
        {
            return GitGatewayResult<Unit>.Failure(GitGatewayError.NothingToPush);
        }

        var result = await _commandClient.PushAsync(root, repository, options.Remote, branch.StandardOutput, cancellationToken);
        if (result.IsSuccess)
        {
            return GitGatewayResult<Unit>.Success(Unit.Value);
        }

        return result.Failure == GitCommandFailure.Failed
            ? GitGatewayResult<Unit>.Failure(ClassifyPushFailure(result.StandardError))
            : GitGatewayResult<Unit>.Failure(MapCommandFailure(result.Failure!.Value));
    }

    public async Task<GitGatewayResult<GitHistoryRewriteResult>> RewriteHistoryAsync(
        string repository,
        IReadOnlyList<GitHistoryRewriteRef> refs,
        bool dryRun,
        CancellationToken cancellationToken)
    {
        var resolved = Resolve(repository);
        if (resolved.Error is not null) return GitGatewayResult<GitHistoryRewriteResult>.Failure(resolved.Error.Value);
        if (refs.Count == 0 || refs.Any(item => !IsValidRewriteRef(item)))
            return GitGatewayResult<GitHistoryRewriteResult>.Failure(GitGatewayError.InvalidRef);
        if (refs.Select(item => item.Ref).Distinct(StringComparer.Ordinal).Count() != refs.Count)
            return GitGatewayResult<GitHistoryRewriteResult>.Failure(GitGatewayError.DuplicateRef);

        var options = resolved.Options!;
        var remoteUrl = await _commandClient.GetRemoteUrlAsync(options.LocalRoot, options.Remote, cancellationToken);
        if (!remoteUrl.IsSuccess) return GitGatewayResult<GitHistoryRewriteResult>.Failure(MapCommandFailure(remoteUrl.Failure!.Value));
        if (!GitHubRemoteUrlValidator.IsExpectedRemote(remoteUrl.StandardOutput, options.GitHubOwner, options.GitHubRepo))
            return GitGatewayResult<GitHistoryRewriteResult>.Failure(GitGatewayError.RemoteMismatch);

        var plan = await BuildRewritePlanAsync(options.LocalRoot, repository, options.Remote, refs, cancellationToken);
        if (plan.Error is not null) return GitGatewayResult<GitHistoryRewriteResult>.Failure(plan.Error.Value);
        if (dryRun)
            return GitGatewayResult<GitHistoryRewriteResult>.Success(new(true, "not_requested", plan.Results!));
        if (plan.Results!.Any(item => item.RejectionReason is not null))
            return GitGatewayResult<GitHistoryRewriteResult>.Failure(GitGatewayError.LeaseConflict);

        var approval = await _approvalPrompt.RequestApprovalAsync(
            new ApprovalPromptRequest(
                "Githubie - History Rewrite Approval",
                $"Rewrite {refs.Count} published ref(s) in {repository}",
                plan.Results!.Select(item => $"{item.Ref}: {item.OldSha} -> {item.NewSha}").ToArray()),
            ApprovalTimeout,
            cancellationToken);
        var approvalError = MapApprovalError(approval.Outcome);
        if (approvalError is not null) return GitGatewayResult<GitHistoryRewriteResult>.Failure(approvalError.Value);

        var recheck = await BuildRewritePlanAsync(options.LocalRoot, repository, options.Remote, refs, cancellationToken);
        if (recheck.Error is not null) return GitGatewayResult<GitHistoryRewriteResult>.Failure(recheck.Error.Value);
        if (recheck.Results!.Any(item => item.RejectionReason is not null))
            return GitGatewayResult<GitHistoryRewriteResult>.Failure(GitGatewayError.LeaseConflict);

        var push = await _commandClient.PushHistoryRewriteAsync(options.LocalRoot, repository, options.Remote, refs, cancellationToken);
        if (!push.IsSuccess)
        {
            var error = push.StandardError.Contains("atomic", StringComparison.OrdinalIgnoreCase)
                ? GitGatewayError.AtomicNotSupported
                : push.StandardError.Contains("permission", StringComparison.OrdinalIgnoreCase) ||
                  push.StandardError.Contains("403", StringComparison.OrdinalIgnoreCase)
                    ? GitGatewayError.PermissionDenied
                    : GitGatewayError.GitFailed;
            return GitGatewayResult<GitHistoryRewriteResult>.Failure(error);
        }

        return GitGatewayResult<GitHistoryRewriteResult>.Success(new(
            false, "approved", recheck.Results!.Select(item => item with { Success = true }).ToArray()));
    }

    private async Task<(IReadOnlyList<GitHistoryRewriteRefResult>? Results, GitGatewayError? Error)> BuildRewritePlanAsync(
        string root, string repository, string remote, IReadOnlyList<GitHistoryRewriteRef> refs, CancellationToken cancellationToken)
    {
        var results = new List<GitHistoryRewriteRefResult>(refs.Count);
        foreach (var item in refs)
        {
            var local = await _commandClient.GetLocalRefAsync(root, item.NewLocalSha, cancellationToken);
            if (!local.IsSuccess) return (null, MapCommandFailure(local.Failure!.Value));
            var remoteResult = await _commandClient.GetRemoteRefAsync(root, repository, remote, item.Ref, cancellationToken);
            if (!remoteResult.IsSuccess) return (null, MapCommandFailure(remoteResult.Failure!.Value));
            var remoteSha = ParseLsRemoteSha(remoteResult.StandardOutput, item.Ref);
            if (remoteSha is null) return (null, GitGatewayError.InvalidRef);
            var reason = string.Equals(remoteSha, item.ExpectedRemoteSha, StringComparison.OrdinalIgnoreCase)
                ? null : "expected_remote_sha_mismatch";
            results.Add(new(item.Ref, remoteSha, local.StandardOutput, false, reason));
        }
        return (results, null);
    }

    private static bool IsValidRewriteRef(GitHistoryRewriteRef item) =>
        (item.Ref.StartsWith("refs/heads/", StringComparison.Ordinal) || item.Ref.StartsWith("refs/tags/", StringComparison.Ordinal)) &&
        item.Ref.Length > "refs/tags/".Length && !item.Ref.Contains("..", StringComparison.Ordinal) &&
        !item.Ref.Contains("@{", StringComparison.Ordinal) && !item.Ref.EndsWith(".", StringComparison.Ordinal) &&
        !item.Ref.EndsWith(".lock", StringComparison.OrdinalIgnoreCase) &&
        item.Ref.All(character => !char.IsControl(character) && " ~^:?*[\\".IndexOf(character, StringComparison.Ordinal) < 0) &&
        IsSha(item.NewLocalSha) && IsSha(item.ExpectedRemoteSha);

    private static bool IsSha(string value) => value.Length == 40 && value.All(Uri.IsHexDigit);

    private static string? ParseLsRemoteSha(string output, string reference)
    {
        var parts = output.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
        return parts.Length == 2 && parts[1].Equals(reference, StringComparison.Ordinal) && IsSha(parts[0]) ? parts[0] : null;
    }

    private static GitGatewayError? MapApprovalError(ApprovalOutcome outcome) => outcome switch
    {
        ApprovalOutcome.Approved => null,
        ApprovalOutcome.Denied => GitGatewayError.ApprovalDenied,
        ApprovalOutcome.TimedOut => GitGatewayError.ApprovalTimedOut,
        _ => GitGatewayError.ApprovalUnavailable,
    };

    private (Configuration.RepositoryOptions? Options, GitGatewayError? Error) Resolve(string repository)
    {
        if (!RepositoryId.IsValid(repository))
        {
            return (null, GitGatewayError.RepositoryNotFound);
        }

        if (!_allowlist.TryGet(repository, out var options))
        {
            return (null, GitGatewayError.RepositoryNotAllowed);
        }

        var validation = _localPathValidator.Validate(options);
        if (!validation.IsAllowed)
        {
            return (null, MapValidationError(validation.Error!.Value));
        }

        return (options, null);
    }

    private static (int Ahead, int Behind) ParseAheadBehind(string standardOutput)
    {
        var parts = standardOutput.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length != 2 || !int.TryParse(parts[0], out var ahead) || !int.TryParse(parts[1], out var behind))
        {
            return (0, 0);
        }

        return (ahead, behind);
    }

    private static GitGatewayError MapCommandFailure(GitCommandFailure failure) => failure switch
    {
        GitCommandFailure.NotFound => GitGatewayError.GitNotFound,
        GitCommandFailure.TimedOut => GitGatewayError.GitTimedOut,
        GitCommandFailure.Cancelled => GitGatewayError.GitCancelled,
        _ => GitGatewayError.GitFailed,
    };

    private static GitGatewayError ClassifyPushFailure(string standardError)
    {
        if (ContainsAny(standardError, "authentication failed", "could not read username", "invalid username or password"))
            return GitGatewayError.AuthenticationFailed;
        if (ContainsAny(standardError, "permission denied", "write access to repository not granted", "requested url returned error: 403"))
            return GitGatewayError.PermissionDenied;
        if (ContainsAny(standardError, "non-fast-forward", "fetch first", "[rejected]"))
            return GitGatewayError.NonFastForward;
        return GitGatewayError.GitFailed;
    }

    private static bool ContainsAny(string value, params string[] candidates) =>
        candidates.Any(candidate => value.Contains(candidate, StringComparison.OrdinalIgnoreCase));

    private static GitGatewayError MapPolicyError(Domain.PolicyErrorCode error) => error switch
    {
        Domain.PolicyErrorCode.WorkingTreeDirty => GitGatewayError.WorkingTreeDirty,
        Domain.PolicyErrorCode.BranchNotAllowed => GitGatewayError.BranchNotAllowed,
        Domain.PolicyErrorCode.ProtectedBranch => GitGatewayError.ProtectedBranch,
        _ => GitGatewayError.GitFailed,
    };

    private static GitGatewayError MapValidationError(RepositoryValidationError error) => error switch
    {
        RepositoryValidationError.LocalRootNotFound => GitGatewayError.LocalRootNotFound,
        RepositoryValidationError.GitMetadataNotFound => GitGatewayError.GitMetadataNotFound,
        RepositoryValidationError.ReparsePointDetected => GitGatewayError.ReparsePointDetected,
        _ => GitGatewayError.RepositoryNotAllowed,
    };
}
