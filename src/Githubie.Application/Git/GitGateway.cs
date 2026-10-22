using System.Collections.Concurrent;
using Githubie.Application.Configuration;
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
    IInteractiveApprovalPrompt approvalPrompt,
    IProviderExecutionGuard? executionGuard = null) : IGitGateway
{
    private static readonly ConcurrentDictionary<string, SemaphoreSlim> StashLocks = new(StringComparer.OrdinalIgnoreCase);
    private const int MaximumStashPathCount = 128;
    private readonly RepositoryAllowlist _allowlist = allowlist;
    private readonly LocalPathValidator _localPathValidator = localPathValidator;
    private readonly IGitCommandClient _commandClient = commandClient;
    private readonly IInteractiveApprovalPrompt _approvalPrompt = approvalPrompt;
    private readonly IProviderExecutionGuard _executionGuard = executionGuard ?? NoOpProviderExecutionGuard.Instance;
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

        var remote = await ResolveRemoteAsync(options, cancellationToken);
        if (remote.Error is not null)
        {
            return GitGatewayResult<GitRepositoryStatus>.Failure(remote.Error.Value, remote.Diagnostic);
        }

        var remoteDevelopHead = await _commandClient.GetRemoteHeadAsync(root, remote.Name!, options.DevelopBranch, cancellationToken);
        var remoteMainHead = await _commandClient.GetRemoteHeadAsync(root, remote.Name!, options.MainBranch, cancellationToken);

        var aheadBehind = await _commandClient.GetAheadBehindAsync(root, remote.Name!, branch.StandardOutput, cancellationToken);
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
            WorkingTreeClean: status.StandardOutput.Length == 0,
            WorkingTreeChanges: ParseWorkingTreeChanges(status.StandardOutput));

        return GitGatewayResult<GitRepositoryStatus>.Success(snapshot);
    }

    public async Task<GitGatewayResult<GitBranchSwitchInfo>> SwitchBranchAsync(
        string repository, string branch, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(branch))
            return GitGatewayResult<GitBranchSwitchInfo>.Failure(GitGatewayError.InvalidRef);

        var resolved = Resolve(repository);
        if (resolved.Error is not null)
            return GitGatewayResult<GitBranchSwitchInfo>.Failure(resolved.Error.Value);

        var root = resolved.Options!.LocalRoot;
        var current = await _commandClient.GetCurrentBranchAsync(root, cancellationToken);
        if (!current.IsSuccess) return CreateCommandFailure<GitBranchSwitchInfo>(current);
        if (string.Equals(current.StandardOutput, branch, StringComparison.Ordinal))
            return GitGatewayResult<GitBranchSwitchInfo>.Success(new(current.StandardOutput, current.StandardOutput));

        var status = await _commandClient.GetStatusAsync(root, cancellationToken);
        if (!status.IsSuccess) return CreateCommandFailure<GitBranchSwitchInfo>(status);
        if (status.StandardOutput.Length > 0)
            return GitGatewayResult<GitBranchSwitchInfo>.Failure(GitGatewayError.WorkingTreeDirty);

        var validName = await _commandClient.CheckBranchNameAsync(root, branch, cancellationToken);
        if (!validName.IsSuccess)
        {
            if (validName.Failure == GitCommandFailure.Failed && validName.ExitCode == 1)
                return GitGatewayResult<GitBranchSwitchInfo>.Failure(GitGatewayError.InvalidRef);
            return CreateCommandFailure<GitBranchSwitchInfo>(validName);
        }

        var localRef = await _commandClient.GetLocalRefAsync(root, $"refs/heads/{branch}", cancellationToken);
        if (!localRef.IsSuccess)
        {
            if (localRef.Failure == GitCommandFailure.Failed)
                return GitGatewayResult<GitBranchSwitchInfo>.Failure(GitGatewayError.BranchNotFound);
            return CreateCommandFailure<GitBranchSwitchInfo>(localRef);
        }

        var switched = await _commandClient.SwitchBranchAsync(root, branch, cancellationToken);
        if (!switched.IsSuccess) return CreateCommandFailure<GitBranchSwitchInfo>(switched);

        var actual = await _commandClient.GetCurrentBranchAsync(root, cancellationToken);
        if (!actual.IsSuccess) return CreateCommandFailure<GitBranchSwitchInfo>(actual);
        if (!string.Equals(actual.StandardOutput, branch, StringComparison.Ordinal))
            return GitGatewayResult<GitBranchSwitchInfo>.Failure(GitGatewayError.GitFailed);

        return GitGatewayResult<GitBranchSwitchInfo>.Success(new(current.StandardOutput, actual.StandardOutput));
    }

    public async Task<GitGatewayResult<GitLocalBranchCreateInfo>> CreateLocalBranchAsync(
        string repository, string branch, string source, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(branch) || string.IsNullOrWhiteSpace(source))
            return GitGatewayResult<GitLocalBranchCreateInfo>.Failure(GitGatewayError.InvalidRef);
        var resolved = Resolve(repository);
        if (resolved.Error is not null)
            return GitGatewayResult<GitLocalBranchCreateInfo>.Failure(resolved.Error.Value);
        var root = resolved.Options!.LocalRoot;
        var validName = await _commandClient.CheckBranchNameAsync(root, branch, cancellationToken);
        if (!validName.IsSuccess)
        {
            if (validName.Failure == GitCommandFailure.Failed && validName.ExitCode == 1)
                return GitGatewayResult<GitLocalBranchCreateInfo>.Failure(GitGatewayError.InvalidRef);
            return CreateCommandFailure<GitLocalBranchCreateInfo>(validName);
        }
        var targetRef = $"refs/heads/{branch}";
        var existing = await _commandClient.GetLocalRefAsync(root, targetRef, cancellationToken);
        if (existing.IsSuccess)
            return GitGatewayResult<GitLocalBranchCreateInfo>.Failure(GitGatewayError.LocalBranchAlreadyExists);
        if (existing.Failure != GitCommandFailure.Failed)
            return CreateCommandFailure<GitLocalBranchCreateInfo>(existing);

        var status = await _commandClient.GetStatusAsync(root, cancellationToken);
        if (!status.IsSuccess) return CreateCommandFailure<GitLocalBranchCreateInfo>(status);
        if (HasUnmergedPaths(status.StandardOutput))
            return GitGatewayResult<GitLocalBranchCreateInfo>.Failure(GitGatewayError.UnresolvedConflicts);
        if (status.StandardOutput.Length > 0)
            return GitGatewayResult<GitLocalBranchCreateInfo>.Failure(GitGatewayError.WorkingTreeDirty);

        string sourceRef;
        if (IsFullSha(source)) sourceRef = source;
        else if (source.StartsWith("refs/", StringComparison.Ordinal) || source.StartsWith("-", StringComparison.Ordinal))
            return GitGatewayResult<GitLocalBranchCreateInfo>.Failure(GitGatewayError.InvalidRef);
        else
        {
            var validSourceName = await _commandClient.CheckBranchNameAsync(root, source, cancellationToken);
            if (!validSourceName.IsSuccess)
                return GitGatewayResult<GitLocalBranchCreateInfo>.Failure(GitGatewayError.InvalidRef);
            var localBranch = await _commandClient.GetLocalRefAsync(root, $"refs/heads/{source}", cancellationToken);
            sourceRef = localBranch.IsSuccess ? $"refs/heads/{source}" : $"refs/tags/{source}";
        }
        var sourceCommit = await _commandClient.GetLocalCommitAsync(root, sourceRef, cancellationToken);
        if (!sourceCommit.IsSuccess)
            return sourceCommit.Failure == GitCommandFailure.Failed
                ? GitGatewayResult<GitLocalBranchCreateInfo>.Failure(GitGatewayError.BranchSourceNotFound)
                : CreateCommandFailure<GitLocalBranchCreateInfo>(sourceCommit);
        var sha = sourceCommit.StandardOutput.Trim();
        var currentBefore = await _commandClient.GetCurrentBranchAsync(root, cancellationToken);
        if (!currentBefore.IsSuccess) return CreateCommandFailure<GitLocalBranchCreateInfo>(currentBefore);
        var headBefore = await _commandClient.GetHeadAsync(root, cancellationToken);
        if (!headBefore.IsSuccess) return CreateCommandFailure<GitLocalBranchCreateInfo>(headBefore);
        var created = await _commandClient.CreateLocalBranchAsync(root, branch, sha, cancellationToken);
        if (!created.IsSuccess) return CreateCommandFailure<GitLocalBranchCreateInfo>(created);
        var actualRef = await _commandClient.GetLocalRefAsync(root, targetRef, cancellationToken);
        var currentAfter = await _commandClient.GetCurrentBranchAsync(root, cancellationToken);
        var headAfter = await _commandClient.GetHeadAsync(root, cancellationToken);
        if (!actualRef.IsSuccess || !currentAfter.IsSuccess || !headAfter.IsSuccess
            || !string.Equals(actualRef.StandardOutput.Trim(), sha, StringComparison.OrdinalIgnoreCase)
            || !string.Equals(currentAfter.StandardOutput, currentBefore.StandardOutput, StringComparison.Ordinal)
            || !string.Equals(headAfter.StandardOutput.Trim(), headBefore.StandardOutput.Trim(), StringComparison.OrdinalIgnoreCase))
            return GitGatewayResult<GitLocalBranchCreateInfo>.Failure(GitGatewayError.GitFailed);
        return GitGatewayResult<GitLocalBranchCreateInfo>.Success(new(branch, source, currentAfter.StandardOutput, headAfter.StandardOutput.Trim()));
    }

    private static bool IsFullSha(string value) => value.Length == 40 && value.All(Uri.IsHexDigit);

    private static bool HasUnmergedPaths(string porcelainStatus) =>
        porcelainStatus.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries)
            .Any(line => line.Length >= 2 && (line[0] == 'U' || line[1] == 'U' || line[..2] == "AA" || line[..2] == "DD"));

    public async Task<GitGatewayResult<GitRepositoryDiff>> GetDiffAsync(string repository, CancellationToken cancellationToken)
    {
        var resolved = Resolve(repository);
        if (resolved.Error is not null)
        {
            return GitGatewayResult<GitRepositoryDiff>.Failure(resolved.Error.Value);
        }

        var result = await _commandClient.GetDiffAsync(resolved.Options!.LocalRoot, cancellationToken);
        return result.IsSuccess
            ? GitGatewayResult<GitRepositoryDiff>.Success(new(result.StandardOutput))
            : CreateCommandFailure<GitRepositoryDiff>(result);
    }

    public Task<GitGatewayResult<GitRepositoryCommit>> CommitAsync(
        string repository, string message, CancellationToken cancellationToken) =>
        CommitLocalAsync(repository, message, null, null, cancellationToken);

    public Task<GitGatewayResult<GitRepositoryCommit>> CommitSelectedAsync(
        string repository, string message, string expectedHead, string patch, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(message) || message.Length > 4096 || expectedHead is null || string.IsNullOrWhiteSpace(patch) || patch.Length > 4 * 1024 * 1024
            || (expectedHead.Length != 0 && (expectedHead.Length is < 40 or > 64 || !expectedHead.All(Uri.IsHexDigit))))
            return Task.FromResult(GitGatewayResult<GitRepositoryCommit>.Failure(GitGatewayError.InvalidSelectedCommitPatch));
        return CommitLocalAsync(repository, message, expectedHead, patch, cancellationToken);
    }

    private async Task<GitGatewayResult<GitRepositoryCommit>> CommitLocalAsync(
        string repository, string message, string? expectedHead, string? patch, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(message))
            return GitGatewayResult<GitRepositoryCommit>.Failure(GitGatewayError.GitFailed, "Commit message is required.");
        var resolved = Resolve(repository);
        if (resolved.Error is not null) return GitGatewayResult<GitRepositoryCommit>.Failure(resolved.Error.Value);
        var options = resolved.Options!;
        var branch = await _commandClient.GetCurrentBranchAsync(options.LocalRoot, cancellationToken);
        if (!branch.IsSuccess) return CreateCommandFailure<GitRepositoryCommit>(branch);
        var branchName = branch.StandardOutput.Trim();
        if (string.IsNullOrWhiteSpace(branchName) || branchName == "HEAD")
            return GitGatewayResult<GitRepositoryCommit>.Failure(GitGatewayError.DetachedHead);
        var head = await _commandClient.GetHeadAsync(options.LocalRoot, cancellationToken);
        if (!head.IsSuccess) return CreateCommandFailure<GitRepositoryCommit>(head);
        var capturedHead = head.StandardOutput.Trim();
        if (expectedHead is not null && !string.Equals(capturedHead, expectedHead, StringComparison.OrdinalIgnoreCase))
            return GitGatewayResult<GitRepositoryCommit>.Failure(GitGatewayError.ExpectedHeadMismatch);
        var status = await _commandClient.GetStatusAsync(options.LocalRoot, cancellationToken);
        if (!status.IsSuccess) return CreateCommandFailure<GitRepositoryCommit>(status);
        if (patch is null && status.StandardOutput.Length == 0)
            return GitGatewayResult<GitRepositoryCommit>.Failure(GitGatewayError.NothingToCommit);
        if (patch is not null && status.StandardOutput.Split('\n', StringSplitOptions.RemoveEmptyEntries)
            .Any(line => line.Length >= 2 && line[0] != ' ' && line[0] != '?'))
            return GitGatewayResult<GitRepositoryCommit>.Failure(GitGatewayError.StagedChangesPresent);
        var authorName = options.CommitAuthorName;
        var authorEmail = options.CommitAuthorEmail;
        if (!CommitAuthorIdentity.IsValid(authorName, authorEmail))
        {
            var localName = await _commandClient.GetLocalConfigAsync(options.LocalRoot, "user.name", cancellationToken);
            var localEmail = await _commandClient.GetLocalConfigAsync(options.LocalRoot, "user.email", cancellationToken);
            authorName = localName.IsSuccess ? localName.StandardOutput.Trim() : null;
            authorEmail = localEmail.IsSuccess ? localEmail.StandardOutput.Trim() : null;
            if (!CommitAuthorIdentity.IsValid(authorName, authorEmail))
                return GitGatewayResult<GitRepositoryCommit>.Failure(GitGatewayError.AuthorIdentityMissing);
        }
        var committed = await _commandClient.CommitCurrentBranchAsync(options.LocalRoot, message, authorName!, authorEmail!,
            branchName, capturedHead, patch, cancellationToken);
        if (!committed.IsSuccess)
        {
            if (patch is not null && committed.Failure == GitCommandFailure.Failed)
                return GitGatewayResult<GitRepositoryCommit>.Failure(GitGatewayError.InvalidSelectedCommitPatch, committed.StandardError, committed.ExitCode);
            return CreateCommandFailure<GitRepositoryCommit>(committed);
        }
        return GitGatewayResult<GitRepositoryCommit>.Success(new(committed.StandardOutput.Trim(), branchName));
    }

    public async Task<GitGatewayResult<GitStashEntry>> PushStashAsync(
        string repository, string message, IReadOnlyList<string>? paths, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(message) || message.Length > 256 || message.Contains('\n') || message.Contains('\r'))
            return GitGatewayResult<GitStashEntry>.Failure(GitGatewayError.InvalidStashMessage);
        if (!TryValidateStashPaths(paths, out string[] safePaths))
            return GitGatewayResult<GitStashEntry>.Failure(GitGatewayError.InvalidStashPath);
        var resolved = Resolve(repository);
        if (resolved.Error is not null) return GitGatewayResult<GitStashEntry>.Failure(resolved.Error.Value);
        var root = resolved.Options!.LocalRoot;
        return await WithStashLockAsync(root, async () =>
        {
            var before = await ReadStashesAsync(root, cancellationToken);
            if (!before.IsSuccess) return GitGatewayResult<GitStashEntry>.Failure(MapCommandFailure(before.Failure!.Value));
            var pushed = await _commandClient.PushStashAsync(root, message, safePaths, cancellationToken);
            if (!pushed.IsSuccess) return CreateCommandFailure<GitStashEntry>(pushed);
            var after = await ReadStashesAsync(root, cancellationToken);
            if (!after.IsSuccess) return GitGatewayResult<GitStashEntry>.Failure(MapCommandFailure(after.Failure!.Value));
            var created = after.Entries.FirstOrDefault(item => before.Entries.All(previous => previous.Id != item.Id));
            return created is null
                ? GitGatewayResult<GitStashEntry>.Failure(GitGatewayError.NothingToStash)
                : GitGatewayResult<GitStashEntry>.Success(created);
        }, cancellationToken);
    }

    public async Task<GitGatewayResult<IReadOnlyList<GitStashEntry>>> ListStashesAsync(
        string repository, CancellationToken cancellationToken)
    {
        var resolved = Resolve(repository);
        if (resolved.Error is not null) return GitGatewayResult<IReadOnlyList<GitStashEntry>>.Failure(resolved.Error.Value);
        return await WithStashLockAsync(resolved.Options!.LocalRoot, async () =>
        {
            var stashes = await ReadStashesAsync(resolved.Options.LocalRoot, cancellationToken);
            return stashes.IsSuccess
                ? GitGatewayResult<IReadOnlyList<GitStashEntry>>.Success(stashes.Entries)
                : GitGatewayResult<IReadOnlyList<GitStashEntry>>.Failure(MapCommandFailure(stashes.Failure!.Value));
        }, cancellationToken);
    }

    public async Task<GitGatewayResult<GitStashEntry>> ApplyStashAsync(
        string repository, string stashId, CancellationToken cancellationToken)
    {
        if (!IsStashId(stashId)) return GitGatewayResult<GitStashEntry>.Failure(GitGatewayError.InvalidStashReference);
        var resolved = Resolve(repository);
        if (resolved.Error is not null) return GitGatewayResult<GitStashEntry>.Failure(resolved.Error.Value);
        var root = resolved.Options!.LocalRoot;
        return await WithStashLockAsync(root, async () =>
        {
            var stashes = await ReadStashesAsync(root, cancellationToken);
            if (!stashes.IsSuccess) return GitGatewayResult<GitStashEntry>.Failure(MapCommandFailure(stashes.Failure!.Value));
            var stash = stashes.Entries.FirstOrDefault(item => string.Equals(item.Id, stashId, StringComparison.OrdinalIgnoreCase));
            if (stash is null) return GitGatewayResult<GitStashEntry>.Failure(GitGatewayError.InvalidStashReference);
            var applied = await _commandClient.ApplyStashAsync(root, stash.Id, cancellationToken);
            return applied.IsSuccess
                ? GitGatewayResult<GitStashEntry>.Success(stash)
                : CreateCommandFailure<GitStashEntry>(applied);
        }, cancellationToken);
    }

    public async Task<GitGatewayResult<GitStashEntry>> DropStashAsync(
        string repository, string stashId, CancellationToken cancellationToken)
    {
        if (!IsStashId(stashId)) return GitGatewayResult<GitStashEntry>.Failure(GitGatewayError.InvalidStashReference);
        var resolved = Resolve(repository);
        if (resolved.Error is not null) return GitGatewayResult<GitStashEntry>.Failure(resolved.Error.Value);
        var root = resolved.Options!.LocalRoot;
        return await WithStashLockAsync(root, async () =>
        {
            var stashes = await ReadStashesAsync(root, cancellationToken);
            if (!stashes.IsSuccess) return GitGatewayResult<GitStashEntry>.Failure(MapCommandFailure(stashes.Failure!.Value));
            var index = -1;
            for (var i = 0; i < stashes.Entries.Count; i++)
                if (string.Equals(stashes.Entries[i].Id, stashId, StringComparison.OrdinalIgnoreCase)) { index = i; break; }
            if (index < 0) return GitGatewayResult<GitStashEntry>.Failure(GitGatewayError.InvalidStashReference);
            var dropped = await _commandClient.DropStashAsync(root, $"stash@{{{index}}}", cancellationToken);
            return dropped.IsSuccess
                ? GitGatewayResult<GitStashEntry>.Success(stashes.Entries[index])
                : CreateCommandFailure<GitStashEntry>(dropped);
        }, cancellationToken);
    }

    private async Task<(bool IsSuccess, IReadOnlyList<GitStashEntry> Entries, GitCommandFailure? Failure)> ReadStashesAsync(
        string root, CancellationToken cancellationToken)
    {
        var result = await _commandClient.ListStashesAsync(root, cancellationToken);
        if (!result.IsSuccess) return (false, [], result.Failure);
        var entries = new List<GitStashEntry>();
        foreach (var line in result.StandardOutput.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries))
        {
            var fields = line.Split('\u001f', 2);
            if (fields.Length == 2 && IsStashId(fields[0])) entries.Add(new(fields[0], fields[1]));
        }
        return (true, entries, null);
    }

    private static bool IsStashId(string? value) => value is { Length: 40 } && value.All(Uri.IsHexDigit);

    private static async Task<T> WithStashLockAsync<T>(string repositoryRoot, Func<Task<T>> operation, CancellationToken cancellationToken)
    {
        SemaphoreSlim gate = StashLocks.GetOrAdd(Path.GetFullPath(repositoryRoot), static _ => new SemaphoreSlim(1, 1));
        await gate.WaitAsync(cancellationToken);
        try { return await operation(); }
        finally { gate.Release(); }
    }

    private static bool TryValidateStashPaths(IReadOnlyList<string>? paths, out string[] safePaths)
    {
        safePaths = [];
        if (paths is null) return true;
        if (paths.Count > MaximumStashPathCount) return false;
        var normalized = new List<string>();
        foreach (var path in paths)
        {
            if (string.IsNullOrWhiteSpace(path) || path.Any(char.IsControl)) return false;
            var item = path.Replace('\\', '/');
            var segments = item.Split('/');
            if (item.StartsWith("/", StringComparison.Ordinal) || item.Contains(':') ||
                segments.Any(segment => segment is "" or "." or ".." || string.Equals(segment, ".git", StringComparison.OrdinalIgnoreCase)))
                return false;
            normalized.Add(item);
        }
        safePaths = normalized.Distinct(StringComparer.Ordinal).ToArray();
        return true;
    }

    public async Task<GitGatewayResult<Unit>> FetchAsync(string repository, CancellationToken cancellationToken)
    {
        var resolved = Resolve(repository);
        if (resolved.Error is not null)
        {
            return GitGatewayResult<Unit>.Failure(resolved.Error.Value);
        }

        var remote = await ResolveRemoteAsync(resolved.Options!, cancellationToken);
        if (remote.Error is not null)
        {
            return GitGatewayResult<Unit>.Failure(remote.Error.Value, remote.Diagnostic);
        }

        var result = await _commandClient.FetchAsync(resolved.Options!.LocalRoot, repository, remote.Name!, cancellationToken);
        return result.IsSuccess
            ? GitGatewayResult<Unit>.Success(Unit.Value)
            : CreateCommandFailure<Unit>(result);
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

        var remote = await ResolveRemoteAsync(options, cancellationToken);
        if (remote.Error is not null)
        {
            return GitGatewayResult<Unit>.Failure(remote.Error.Value, remote.Diagnostic);
        }

        var result = await _commandClient.PullFastForwardOnlyAsync(options.LocalRoot, repository, remote.Name!, branch, cancellationToken);
        if (result.IsSuccess)
        {
            return GitGatewayResult<Unit>.Success(Unit.Value);
        }

        return CreateCommandFailure<Unit>(result, pull: true);
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
            return CreateCommandFailure<Unit>(branch);
        }

        var remote = await ResolveRemoteAsync(options, cancellationToken);
        if (remote.Error is not null)
        {
            return GitGatewayResult<Unit>.Failure(remote.Error.Value, remote.Diagnostic);
        }

        var policy = options.ToPolicy(repository);

        var status = await _commandClient.GetStatusAsync(root, cancellationToken);
        var workingTreeClean = status.IsSuccess && status.StandardOutput.Length == 0;

        var policyResult = policy.ValidatePush(branch.StandardOutput, workingTreeClean);
        if (!policyResult.IsAllowed)
        {
            return GitGatewayResult<Unit>.Failure(MapPolicyError(policyResult.ErrorCode!.Value));
        }

        var remoteRef = await _commandClient.GetRemoteRefAsync(
            root, repository, remote.Name!, $"refs/heads/{branch.StandardOutput}", cancellationToken);
        if (!remoteRef.IsSuccess)
        {
            return CreateCommandFailure<Unit>(remoteRef);
        }

        if (!string.IsNullOrWhiteSpace(remoteRef.StandardOutput))
        {
            var aheadBehind = await _commandClient.GetAheadBehindAsync(root, remote.Name!, branch.StandardOutput, cancellationToken);
            if (!aheadBehind.IsSuccess)
            {
                return CreateCommandFailure<Unit>(aheadBehind);
            }

            var (ahead, _) = ParseAheadBehind(aheadBehind.StandardOutput);
            if (ahead == 0)
            {
                return GitGatewayResult<Unit>.Failure(GitGatewayError.NothingToPush);
            }
        }

        var result = await _commandClient.PushAsync(root, repository, remote.Name!, branch.StandardOutput, cancellationToken);
        if (result.IsSuccess)
        {
            return GitGatewayResult<Unit>.Success(Unit.Value);
        }

        return CreateCommandFailure<Unit>(result);
    }

    public async Task<GitGatewayResult<Unit>> PushTagAsync(
        string repository, string tag, CancellationToken cancellationToken)
    {
        var resolved = Resolve(repository);
        if (resolved.Error is not null) return GitGatewayResult<Unit>.Failure(resolved.Error.Value);
        var options = resolved.Options!;
        var policy = options.ToPolicy(repository).ValidateTag(tag, options.TagTargetBranch);
        if (!policy.IsAllowed)
            return GitGatewayResult<Unit>.Failure(
                GitGatewayError.InvalidRef,
                "The tag name does not match the configured tag policy.");

        var remote = await ResolveRemoteAsync(options, cancellationToken);
        if (remote.Error is not null) return GitGatewayResult<Unit>.Failure(remote.Error.Value, remote.Diagnostic);

        var reference = $"refs/tags/{tag}";
        var local = await _commandClient.GetLocalRefAsync(options.LocalRoot, reference, cancellationToken);
        if (!local.IsSuccess)
            return GitGatewayResult<Unit>.Failure(
                GitGatewayError.InvalidRef,
                "The local tag ref does not exist.");
        var remoteTag = await _commandClient.GetRemoteRefAsync(
            options.LocalRoot, repository, remote.Name!, reference, cancellationToken);
        if (!remoteTag.IsSuccess) return CreateCommandFailure<Unit>(remoteTag);
        if (!string.IsNullOrWhiteSpace(remoteTag.StandardOutput))
        {
            var remoteSha = ParseLsRemoteSha(remoteTag.StandardOutput, reference);
            if (remoteSha is null)
                return GitGatewayResult<Unit>.Failure(
                    GitGatewayError.InvalidRef,
                    "The remote tag response could not be parsed.");
            if (!string.Equals(remoteSha, local.StandardOutput, StringComparison.OrdinalIgnoreCase))
                return GitGatewayResult<Unit>.Failure(
                    GitGatewayError.NonFastForward,
                    "The remote tag points to a different object; overwrite is not allowed.");
            return GitGatewayResult<Unit>.Success(Unit.Value);
        }

        var push = await _commandClient.PushTagAsync(
            options.LocalRoot, repository, remote.Name!, tag, cancellationToken);
        return push.IsSuccess
            ? GitGatewayResult<Unit>.Success(Unit.Value)
            : CreateCommandFailure<Unit>(push);
    }

    public async Task<GitGatewayResult<Unit>> PersistTagAsync(
        string repository, string tag, CancellationToken cancellationToken)
    {
        var resolved = Resolve(repository);
        if (resolved.Error is not null) return GitGatewayResult<Unit>.Failure(resolved.Error.Value);
        var options = resolved.Options!;
        var policy = options.ToPolicy(repository).ValidateTag(tag, options.TagTargetBranch);
        if (!policy.IsAllowed)
            return GitGatewayResult<Unit>.Failure(GitGatewayError.InvalidRef, "The tag name does not match the configured tag policy.");

        var remote = await ResolveRemoteAsync(options, cancellationToken);
        if (remote.Error is not null) return GitGatewayResult<Unit>.Failure(remote.Error.Value, remote.Diagnostic);

        var fetch = await _commandClient.FetchTagAsync(options.LocalRoot, repository, remote.Name!, tag, cancellationToken);
        return fetch.IsSuccess ? GitGatewayResult<Unit>.Success(Unit.Value) : CreateCommandFailure<Unit>(fetch);
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
        var remote = await ResolveRemoteAsync(options, cancellationToken);
        if (remote.Error is not null)
            return GitGatewayResult<GitHistoryRewriteResult>.Failure(remote.Error.Value, remote.Diagnostic);

        var plan = await BuildRewritePlanAsync(options.LocalRoot, repository, remote.Name!, refs, cancellationToken);
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

        var recheck = await BuildRewritePlanAsync(options.LocalRoot, repository, remote.Name!, refs, cancellationToken);
        if (recheck.Error is not null) return GitGatewayResult<GitHistoryRewriteResult>.Failure(recheck.Error.Value);
        if (recheck.Results!.Any(item => item.RejectionReason is not null))
            return GitGatewayResult<GitHistoryRewriteResult>.Failure(GitGatewayError.LeaseConflict);

        await _executionGuard.EnsureCurrentAsync(cancellationToken);
        var push = await _commandClient.PushHistoryRewriteAsync(options.LocalRoot, repository, remote.Name!, refs, cancellationToken);
        if (!push.IsSuccess)
        {
            var error = ClassifyHistoryRewriteFailure(push.StandardError);
            return GitGatewayResult<GitHistoryRewriteResult>.Failure(
                error,
                GitErrorDiagnostic.Sanitize(push.StandardError),
                push.ExitCode);
        }

        return GitGatewayResult<GitHistoryRewriteResult>.Success(new(
            false, "approved", recheck.Results!.Select(item => item with { Success = true }).ToArray()));
    }

    private sealed class NoOpProviderExecutionGuard : IProviderExecutionGuard
    {
        public static NoOpProviderExecutionGuard Instance { get; } = new();

        public Task EnsureCurrentAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
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

    /// <summary>
    /// Repository Provider Contract「Gitリモートの解決」に従い、操作に使うリモートを実行直前に決めます。
    /// 優先順は、Tool引数`remote` → 登録済みのリモート名 → 登録GitHub RepositoryとURLが一致するリモートの自動解決です。
    /// 指定名・登録名はURLを検証し、`origin`などへ暗黙に退避しません。
    /// </summary>
    private async Task<RemoteResolution> ResolveRemoteAsync(
        Configuration.RepositoryOptions options, CancellationToken cancellationToken)
    {
        var named = GitRemoteSelection.Current ?? (string.IsNullOrWhiteSpace(options.Remote) ? null : options.Remote.Trim());
        if (named is not null)
        {
            return await VerifyNamedRemoteAsync(options, named, cancellationToken);
        }

        var listed = await _commandClient.ListRemoteUrlsAsync(options.LocalRoot, cancellationToken);
        if (!listed.IsSuccess)
        {
            return RemoteResolution.Fail(MapCommandFailure(listed.Failure!.Value));
        }

        // Githubieは資格情報をAskPassで渡すため、HTTPSのリモートだけを候補にする（SSHは除外）。
        var matches = ParseRemoteUrls(listed.StandardOutput)
            .Where(remote => IsSupportedMatchingRemote(remote.Url, options))
            .Select(remote => remote.Name)
            .Distinct(StringComparer.Ordinal)
            .Order(StringComparer.Ordinal)
            .ToArray();
        if (matches.Length == 0)
        {
            return RemoteResolution.Fail(GitGatewayError.RemoteNotFound);
        }

        if (matches.Length == 1)
        {
            return RemoteResolution.Ok(matches[0]);
        }

        var conventional = matches.Where(name => string.Equals(name, ConventionalRemoteName, StringComparison.Ordinal)).ToArray();
        return conventional.Length == 1
            ? RemoteResolution.Ok(conventional[0])
            : RemoteResolution.Fail(GitGatewayError.RemoteAmbiguous, $"Matching remotes: {string.Join(", ", matches)}");
    }

    private async Task<RemoteResolution> VerifyNamedRemoteAsync(
        Configuration.RepositoryOptions options, string remote, CancellationToken cancellationToken)
    {
        if (!IsValidRemoteName(remote))
        {
            return RemoteResolution.Fail(GitGatewayError.RemoteNotFound);
        }

        var url = await _commandClient.GetRemoteUrlAsync(options.LocalRoot, remote, cancellationToken);
        if (!url.IsSuccess)
        {
            return RemoteResolution.Fail(url.Failure == GitCommandFailure.Failed
                ? GitGatewayError.RemoteNotFound
                : MapCommandFailure(url.Failure!.Value));
        }

        var value = url.StandardOutput.Trim();
        if (value.Length == 0)
        {
            return RemoteResolution.Fail(GitGatewayError.RemoteNotFound);
        }

        if (GitHubRemoteUrlValidator.IsSshRemote(value))
        {
            return RemoteResolution.Fail(GitGatewayError.RemoteHttpsRequired);
        }

        return GitHubRemoteUrlValidator.IsExpectedRemote(value, options.GitHubOwner, options.GitHubRepo)
            ? RemoteResolution.Ok(remote)
            : RemoteResolution.Fail(GitGatewayError.RemoteMismatch);
    }

    private static bool IsSupportedMatchingRemote(string url, Configuration.RepositoryOptions options) =>
        !string.IsNullOrWhiteSpace(url)
        && !GitHubRemoteUrlValidator.IsSshRemote(url)
        && GitHubRemoteUrlValidator.IsExpectedRemote(url, options.GitHubOwner, options.GitHubRepo);

    private static IEnumerable<(string Name, string Url)> ParseRemoteUrls(string output)
    {
        foreach (var line in output.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var separator = line.IndexOf(' ');
            if (separator <= 0) continue;
            var key = line[..separator];
            if (!key.StartsWith("remote.", StringComparison.OrdinalIgnoreCase)
                || !key.EndsWith(".url", StringComparison.OrdinalIgnoreCase)
                || key.Length <= "remote..url".Length)
            {
                continue;
            }

            yield return (key["remote.".Length..^".url".Length], line[(separator + 1)..].Trim());
        }
    }

    private static bool IsValidRemoteName(string remote) =>
        remote.Length is > 0 and <= 255
        && !remote.StartsWith('-')
        && !remote.Any(character => char.IsWhiteSpace(character) || char.IsControl(character));

    /// <summary>命名規則`<ホスト>-origin-<接続方式>`のうち、GithubieがサポートするHTTPSの名前です。</summary>
    private const string ConventionalRemoteName = "github-origin-https";

    private sealed record RemoteResolution(string? Name, GitGatewayError? Error, string? Diagnostic)
    {
        public static RemoteResolution Ok(string name) => new(name, null, null);

        public static RemoteResolution Fail(GitGatewayError error, string? diagnostic = null) => new(null, error, diagnostic);
    }

    private (Configuration.RepositoryOptions? Options, GitGatewayError? Error) Resolve(string repository)
    {
        if (!RepositoryId.TryNormalize(repository, out repository))
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
        GitCommandFailure.DetachedHead => GitGatewayError.DetachedHead,
        GitCommandFailure.BranchChanged => GitGatewayError.CurrentBranchChanged,
        GitCommandFailure.HeadChanged => GitGatewayError.ExpectedHeadMismatch,
        GitCommandFailure.RepositoryBusy => GitGatewayError.RepositoryBusy,
        GitCommandFailure.CommitResultUnknown => GitGatewayError.CommitResultUnknown,
        GitCommandFailure.StagedChangesPresent => GitGatewayError.StagedChangesPresent,
        GitCommandFailure.NothingToCommit => GitGatewayError.NothingToCommit,
        GitCommandFailure.CommitInProgress => GitGatewayError.CommitInProgress,
        GitCommandFailure.AtomicNotSupported => GitGatewayError.AtomicNotSupported,
        GitCommandFailure.IndexUpdateFailed => GitGatewayError.GitFailed,
        _ => GitGatewayError.GitFailed,
    };

    private static GitGatewayError ClassifyCommandFailure(GitCommandResult result, bool pull = false)
    {
        if (result.Failure != GitCommandFailure.Failed)
            return MapCommandFailure(result.Failure!.Value);

        return ClassifyGitFailure(result.StandardError, pull);
    }

    private static GitGatewayResult<T> CreateCommandFailure<T>(GitCommandResult result, bool pull = false) =>
        GitGatewayResult<T>.Failure(
            ClassifyCommandFailure(result, pull),
            GitErrorDiagnostic.Sanitize(result.StandardError),
            result.ExitCode);

    private static GitGatewayError ClassifyGitFailure(string standardError, bool pull = false)
    {
        if (ContainsAny(standardError, "authentication failed", "could not read username", "invalid username or password"))
            return GitGatewayError.AuthenticationFailed;
        if (ContainsAny(standardError, "permission denied", "write access to repository not granted", "requested url returned error: 403"))
            return GitGatewayError.PermissionDenied;
        if (ContainsAny(standardError, "non-fast-forward", "fetch first", "[rejected]"))
            return GitGatewayError.NonFastForward;
        if (pull && ContainsAny(standardError, "not possible to fast-forward", "diverging branches"))
            return GitGatewayError.NonFastForward;
        if (ContainsAny(standardError, "could not resolve host", "failed to connect", "connection timed out", "connection reset", "network is unreachable", "tls", "ssl"))
            return GitGatewayError.NetworkError;
        if (ContainsAny(standardError, "repository not found", "remote repository not found", "does not appear to be a git repository", "couldn't find remote ref"))
            return GitGatewayError.RemoteUnavailable;
        return GitGatewayError.GitFailed;
    }

    private static GitGatewayError ClassifyHistoryRewriteFailure(string standardError)
    {
        if (ContainsAny(standardError, "stale info", "force-with-lease", "cannot lock ref", "fetch first"))
            return GitGatewayError.LeaseConflict;
        if (ContainsAny(standardError, "does not support --atomic", "atomic push failed", "atomic pushes are not supported"))
            return GitGatewayError.AtomicNotSupported;
        if (ContainsAny(standardError, "protected branch", "repository rule", "ruleset", "gh006", "gh013"))
            return GitGatewayError.BranchProtectionDenied;
        if (ContainsAny(standardError, "refusing to allow a personal access token to create or update workflow", "workflow scope"))
            return GitGatewayError.WorkflowPermissionDenied;
        if (ContainsAny(standardError, "authentication failed", "bad credentials", "could not read username"))
            return GitGatewayError.AuthenticationFailed;
        if (ContainsAny(standardError, "personal access token", "write access to repository not granted", "requested url returned error: 403"))
            return GitGatewayError.TokenPermissionDenied;
        if (ContainsAny(standardError, "permission denied"))
            return GitGatewayError.PermissionDenied;
        return GitGatewayError.GitFailed;
    }

    private static IReadOnlyList<GitWorkingTreeChange> ParseWorkingTreeChanges(string output) =>
        output.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries)
            .Take(20)
            .Select(line => line.Length >= 4
                ? new GitWorkingTreeChange(line[..2], line[3..])
                : new GitWorkingTreeChange("??", "(unparseable)"))
            .ToArray();

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
