using Githubie.Application.Repositories;

namespace Githubie.Application.Git;

/// <summary>
/// Repository Allowlist・Local Path検証・Remote URL検証・Repository Policyを適用したうえで
/// <see cref="IGitCommandClient"/>を呼び出すアプリケーション層Gatewayです。
/// </summary>
public sealed class GitGateway(
    RepositoryAllowlist allowlist,
    LocalPathValidator localPathValidator,
    IGitCommandClient commandClient) : IGitGateway
{
    private readonly RepositoryAllowlist _allowlist = allowlist;
    private readonly LocalPathValidator _localPathValidator = localPathValidator;
    private readonly IGitCommandClient _commandClient = commandClient;

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

        var result = await _commandClient.PushAsync(root, repository, options.Remote, branch.StandardOutput, cancellationToken);
        if (result.IsSuccess)
        {
            return GitGatewayResult<Unit>.Success(Unit.Value);
        }

        return result.Failure == GitCommandFailure.Failed
            ? GitGatewayResult<Unit>.Failure(GitGatewayError.NothingToPush)
            : GitGatewayResult<Unit>.Failure(MapCommandFailure(result.Failure!.Value));
    }

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
