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

    public async Task<GitGatewayResult<GitRepositoryCommit>> CommitAsync(
        string repository, string message, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(message))
        {
            return GitGatewayResult<GitRepositoryCommit>.Failure(GitGatewayError.GitFailed, "Commit message is required.");
        }

        var resolved = Resolve(repository);
        if (resolved.Error is not null)
        {
            return GitGatewayResult<GitRepositoryCommit>.Failure(resolved.Error.Value);
        }

        var options = resolved.Options!;
        var branch = await _commandClient.GetCurrentBranchAsync(options.LocalRoot, cancellationToken);
        if (!branch.IsSuccess)
        {
            return CreateCommandFailure<GitRepositoryCommit>(branch);
        }
        if (options.ProtectedBranches.Contains(branch.StandardOutput, StringComparer.Ordinal))
        {
            return GitGatewayResult<GitRepositoryCommit>.Failure(GitGatewayError.ProtectedBranch);
        }
        if (!options.DirectPushBranches.Contains(branch.StandardOutput, StringComparer.Ordinal))
        {
            return GitGatewayResult<GitRepositoryCommit>.Failure(GitGatewayError.BranchNotAllowed);
        }

        var status = await _commandClient.GetStatusAsync(options.LocalRoot, cancellationToken);
        if (!status.IsSuccess)
        {
            return CreateCommandFailure<GitRepositoryCommit>(status);
        }
        if (status.StandardOutput.Length == 0)
        {
            return GitGatewayResult<GitRepositoryCommit>.Failure(GitGatewayError.NothingToCommit);
        }

        var authorName = options.CommitAuthorName;
        var authorEmail = options.CommitAuthorEmail;
        if (!CommitAuthorIdentity.IsValid(authorName, authorEmail))
        {
            var localName = await _commandClient.GetLocalConfigAsync(options.LocalRoot, "user.name", cancellationToken);
            var localEmail = await _commandClient.GetLocalConfigAsync(options.LocalRoot, "user.email", cancellationToken);
            authorName = localName.IsSuccess ? localName.StandardOutput.Trim() : null;
            authorEmail = localEmail.IsSuccess ? localEmail.StandardOutput.Trim() : null;
            if (!CommitAuthorIdentity.IsValid(authorName, authorEmail))
            {
                return GitGatewayResult<GitRepositoryCommit>.Failure(GitGatewayError.AuthorIdentityMissing);
            }
        }

        var add = await _commandClient.AddAllAsync(options.LocalRoot, cancellationToken);
        if (!add.IsSuccess)
        {
            return CreateCommandFailure<GitRepositoryCommit>(add);
        }

        var commit = await _commandClient.CommitAsync(options.LocalRoot, message, authorName!, authorEmail!, cancellationToken);
        if (!commit.IsSuccess)
        {
            return CreateCommandFailure<GitRepositoryCommit>(commit);
        }
        var head = await _commandClient.GetHeadAsync(options.LocalRoot, cancellationToken);
        return head.IsSuccess
            ? GitGatewayResult<GitRepositoryCommit>.Success(new(head.StandardOutput))
            : CreateCommandFailure<GitRepositoryCommit>(head);
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
