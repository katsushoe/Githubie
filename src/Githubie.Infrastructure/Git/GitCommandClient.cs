using Githubie.Application.Git;

namespace Githubie.Infrastructure.Git;

/// <summary>
/// <see cref="IGitCommandClient"/>の実装です。許可された固定引数配列だけをGitへ渡します。
/// Agent入力をGit Argumentへ直接連結せず、remote名/branch名の前に`--`を置いてoption injectionを防ぎます。
/// </summary>
public sealed class GitCommandClient(IProcessExecutor processExecutor, string askPassExecutablePath) : IGitCommandClient
{
    private static readonly TimeSpan LocalTimeout = TimeSpan.FromSeconds(30);
    private static readonly TimeSpan NetworkTimeout = TimeSpan.FromSeconds(120);

    private readonly IProcessExecutor _processExecutor = processExecutor;
    private readonly string _askPassExecutablePath = askPassExecutablePath;

    public async Task<GitCommandResult> GetCurrentBranchAsync(string repositoryRoot, CancellationToken cancellationToken)
    {
        var result = await ExecuteLocalAsync(repositoryRoot, ["rev-parse", "--abbrev-ref", "HEAD"], cancellationToken);
        return result.IsSuccess
            ? result
            : await ExecuteLocalAsync(repositoryRoot, ["symbolic-ref", "--short", "HEAD"], cancellationToken);
    }

    public Task<GitCommandResult> CheckBranchNameAsync(string repositoryRoot, string branch, CancellationToken cancellationToken) =>
        ExecuteLocalAsync(repositoryRoot, ["check-ref-format", $"refs/heads/{branch}"], cancellationToken);

    public Task<GitCommandResult> SwitchBranchAsync(string repositoryRoot, string branch, CancellationToken cancellationToken) =>
        ExecuteLocalAsync(repositoryRoot, ["switch", "--", branch], cancellationToken);

    public async Task<GitCommandResult> GetHeadAsync(string repositoryRoot, CancellationToken cancellationToken)
    {
        var result = await ExecuteLocalAsync(repositoryRoot, ["rev-parse", "--verify", "--quiet", "HEAD"], cancellationToken);
        return IsUnbornHead(result) ? GitCommandResult.Success(string.Empty) : result;
    }

    public Task<GitCommandResult> GetRemoteHeadAsync(string repositoryRoot, string remote, string branch, CancellationToken cancellationToken) =>
        ExecuteLocalAsync(repositoryRoot, ["rev-parse", $"refs/remotes/{remote}/{branch}"], cancellationToken);

    public Task<GitCommandResult> GetAheadBehindAsync(string repositoryRoot, string remote, string branch, CancellationToken cancellationToken) =>
        ExecuteLocalAsync(repositoryRoot, ["rev-list", "--left-right", "--count", $"HEAD...refs/remotes/{remote}/{branch}"], cancellationToken);

    public Task<GitCommandResult> GetStatusAsync(string repositoryRoot, CancellationToken cancellationToken) =>
        ExecuteLocalAsync(repositoryRoot, ["status", "--porcelain"], cancellationToken);

    public async Task<GitCommandResult> GetDiffAsync(string repositoryRoot, CancellationToken cancellationToken)
    {
        var head = await GetHeadAsync(repositoryRoot, cancellationToken);
        if (!head.IsSuccess)
        {
            return head;
        }

        var arguments = head.StandardOutput.Length == 0
            ? new[] { "diff", "--no-ext-diff", "--binary", "--cached", "--" }
            : new[] { "diff", "--no-ext-diff", "--binary", "HEAD", "--" };
        return await ExecuteLocalAsync(repositoryRoot, arguments, cancellationToken);
    }

    public Task<GitCommandResult> AddAllAsync(string repositoryRoot, CancellationToken cancellationToken) =>
        ExecuteLocalAsync(repositoryRoot, ["add", "--all", "--"], cancellationToken);

    public Task<GitCommandResult> GetLocalConfigAsync(string repositoryRoot, string key, CancellationToken cancellationToken) =>
        ExecuteLocalAsync(repositoryRoot, ["config", "--local", "--get", key], cancellationToken);

    public Task<GitCommandResult> CommitAsync(string repositoryRoot, string message, string authorName, string authorEmail, CancellationToken cancellationToken) =>
        ExecuteLocalAsync(repositoryRoot,
            ["-c", $"user.name={authorName}", "-c", $"user.email={authorEmail}", "commit", "-m", message, "--"], cancellationToken);

    public async Task<GitCommandResult> CommitSelectedAsync(string repositoryRoot, string message, string authorName, string authorEmail, string expectedHead, string patch, CancellationToken cancellationToken)
    {
        string index = Path.Combine(Path.GetTempPath(), $"githubie-selected-commit-{Guid.NewGuid():N}.index");
        string patchFile = Path.Combine(Path.GetTempPath(), $"githubie-selected-commit-{Guid.NewGuid():N}.patch");
        Dictionary<string, string> environment = GitEnvironmentSanitizer.BuildBaseEnvironment();
        environment["GIT_INDEX_FILE"] = index;
        await File.WriteAllTextAsync(patchFile, patch, new System.Text.UTF8Encoding(false), cancellationToken).ConfigureAwait(false);
        try
        {
            GitCommandResult initialized = await ExecuteLocalAsync(repositoryRoot,
                string.IsNullOrEmpty(expectedHead) ? ["read-tree", "--empty"] : ["read-tree", expectedHead], cancellationToken, environment).ConfigureAwait(false);
            if (!initialized.IsSuccess) return initialized;
            GitCommandResult checkedPatch = await ExecuteLocalAsync(repositoryRoot, ["apply", "--cached", "--check", patchFile], cancellationToken, environment).ConfigureAwait(false);
            if (!checkedPatch.IsSuccess) return checkedPatch;
            GitCommandResult applied = await ExecuteLocalAsync(repositoryRoot, ["apply", "--cached", patchFile], cancellationToken, environment).ConfigureAwait(false);
            if (!applied.IsSuccess) return applied;
            GitCommandResult tree = await ExecuteLocalAsync(repositoryRoot, ["write-tree"], cancellationToken, environment).ConfigureAwait(false);
            if (!tree.IsSuccess) return tree;
            List<string> commitArguments = ["-c", $"user.name={authorName}", "-c", $"user.email={authorEmail}", "commit-tree", tree.StandardOutput.Trim()];
            if (!string.IsNullOrEmpty(expectedHead)) commitArguments.AddRange(["-p", expectedHead]);
            commitArguments.AddRange(["-m", message]);
            GitCommandResult commit = await ExecuteLocalAsync(repositoryRoot, commitArguments, cancellationToken, environment).ConfigureAwait(false);
            if (!commit.IsSuccess) return commit;
            string commitSha = commit.StandardOutput.Trim();
            GitCommandResult updateRef = await ExecuteLocalAsync(repositoryRoot,
                ["update-ref", "-m", $"commit: {message}", "HEAD", commitSha, expectedHead], cancellationToken).ConfigureAwait(false);
            if (!updateRef.IsSuccess)
            {
                if (updateRef.StandardError.Contains("expected", StringComparison.OrdinalIgnoreCase) ||
                    updateRef.StandardError.Contains("cannot lock ref", StringComparison.OrdinalIgnoreCase) ||
                    updateRef.StandardError.Contains("already exists", StringComparison.OrdinalIgnoreCase))
                    return GitCommandResult.Failed(GitCommandFailure.Failed, standardError: "HEAD changed before selected commit.", exitCode: updateRef.ExitCode);
                return updateRef;
            }
            return await ExecuteLocalAsync(repositoryRoot, ["read-tree", "HEAD"], cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            File.Delete(patchFile);
            if (File.Exists(index)) File.Delete(index);
            string lockPath = index + ".lock";
            if (File.Exists(lockPath)) File.Delete(lockPath);
        }
    }

    public Task<GitCommandResult> PushStashAsync(
        string repositoryRoot, string message, IReadOnlyList<string> paths, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(paths);
        List<string> arguments = ["stash", "push", "--include-untracked", "--message", message];
        if (paths.Count > 0)
        {
            arguments.Add("--");
            arguments.AddRange(paths.Select(path => $":(literal){path}"));
        }
        return ExecuteLocalAsync(repositoryRoot, arguments, cancellationToken);
    }

    public Task<GitCommandResult> ListStashesAsync(string repositoryRoot, CancellationToken cancellationToken) =>
        ExecuteLocalAsync(repositoryRoot, ["stash", "list", "--format=%H%x1f%gs"], cancellationToken);

    public Task<GitCommandResult> ApplyStashAsync(
        string repositoryRoot, string stashId, CancellationToken cancellationToken) =>
        ExecuteLocalAsync(repositoryRoot, ["stash", "apply", "--index", stashId], cancellationToken);

    public Task<GitCommandResult> DropStashAsync(
        string repositoryRoot, string selector, CancellationToken cancellationToken) =>
        ExecuteLocalAsync(repositoryRoot, ["stash", "drop", selector], cancellationToken);

    public Task<GitCommandResult> GetRemoteUrlAsync(string repositoryRoot, string remote, CancellationToken cancellationToken) =>
        ExecuteLocalAsync(repositoryRoot, ["remote", "get-url", "--", remote], cancellationToken);

    public async Task<GitCommandResult> ListRemoteUrlsAsync(string repositoryRoot, CancellationToken cancellationToken)
    {
        var result = await ExecuteLocalAsync(
            repositoryRoot, ["config", "--get-regexp", @"^remote\..*\.url$"], cancellationToken);
        // 一致する設定がない場合、git configは終了コード1で何も出力しない。リモートなしとして扱う。
        return !result.IsSuccess && result.Failure == GitCommandFailure.Failed && result.ExitCode == 1
            && string.IsNullOrWhiteSpace(result.StandardError)
            ? GitCommandResult.Success(string.Empty)
            : result;
    }

    public Task<GitCommandResult> AddRemoteAsync(string repositoryRoot, string remote, string url, CancellationToken cancellationToken) =>
        ExecuteLocalAsync(repositoryRoot, ["remote", "add", "--", remote, url], cancellationToken);

    public Task<GitCommandResult> FetchAsync(string repositoryRoot, string repositoryId, string remote, CancellationToken cancellationToken) =>
        ExecuteNetworkAsync(repositoryRoot, repositoryId, ["fetch", "--", remote], cancellationToken);

    public Task<GitCommandResult> PullFastForwardOnlyAsync(string repositoryRoot, string repositoryId, string remote, string branch, CancellationToken cancellationToken) =>
        ExecuteNetworkAsync(repositoryRoot, repositoryId, ["pull", "--ff-only", "--", remote, branch], cancellationToken);

    public Task<GitCommandResult> PushAsync(string repositoryRoot, string repositoryId, string remote, string branch, CancellationToken cancellationToken) =>
        ExecuteNetworkAsync(repositoryRoot, repositoryId, ["push", "--", remote, branch], cancellationToken);

    public Task<GitCommandResult> PushTagAsync(string repositoryRoot, string repositoryId, string remote, string tag, CancellationToken cancellationToken) =>
        ExecuteNetworkAsync(repositoryRoot, repositoryId, ["push", "--", remote, $"refs/tags/{tag}:refs/tags/{tag}"], cancellationToken);

    public Task<GitCommandResult> FetchTagAsync(string repositoryRoot, string repositoryId, string remote, string tag, CancellationToken cancellationToken) =>
        ExecuteNetworkAsync(repositoryRoot, repositoryId, ["fetch", "--force", "--no-tags", "--", remote, $"refs/tags/{tag}:refs/tags/{tag}"], cancellationToken);

    public Task<GitCommandResult> GetLocalRefAsync(string repositoryRoot, string reference, CancellationToken cancellationToken) =>
        ExecuteLocalAsync(repositoryRoot, ["rev-parse", "--verify", reference], cancellationToken);

    public Task<GitCommandResult> GetRemoteRefAsync(string repositoryRoot, string repositoryId, string remote, string reference, CancellationToken cancellationToken) =>
        ExecuteNetworkAsync(repositoryRoot, repositoryId, ["ls-remote", "--refs", "--", remote, reference], cancellationToken);

    public Task<GitCommandResult> PushHistoryRewriteAsync(
        string repositoryRoot,
        string repositoryId,
        string remote,
        IReadOnlyList<GitHistoryRewriteRef> refs,
        CancellationToken cancellationToken)
    {
        var arguments = new List<string> { "push", "--atomic" };
        foreach (var item in refs)
        {
            arguments.Add($"--force-with-lease={item.Ref}:{item.ExpectedRemoteSha}");
        }
        arguments.Add("--");
        arguments.Add(remote);
        foreach (var item in refs)
        {
            arguments.Add($"{item.NewLocalSha}:{item.Ref}");
        }
        return ExecuteNetworkAsync(repositoryRoot, repositoryId, arguments, cancellationToken);
    }

    private Task<GitCommandResult> ExecuteLocalAsync(string repositoryRoot, IReadOnlyList<string> gitArguments, CancellationToken cancellationToken, IReadOnlyDictionary<string, string>? environmentOverrides = null)
    {
        var arguments = new List<string> { "-c", $"safe.directory={NormalizeSafeDirectory(repositoryRoot)}" };
        arguments.AddRange(gitArguments);
        var environment = GitEnvironmentSanitizer.BuildBaseEnvironment();
        if (environmentOverrides is not null)
            foreach (var (key, value) in environmentOverrides) environment[key] = value;
        return _processExecutor.ExecuteAsync(repositoryRoot, "git", arguments, environment, LocalTimeout, cancellationToken);
    }

    private Task<GitCommandResult> ExecuteNetworkAsync(string repositoryRoot, string repositoryId, IReadOnlyList<string> gitArguments, CancellationToken cancellationToken)
    {
        var arguments = new List<string>
        {
            "-c", $"safe.directory={NormalizeSafeDirectory(repositoryRoot)}",
            "-c", "credential.helper=",
        };
        arguments.AddRange(gitArguments);

        var environment = GitEnvironmentSanitizer.BuildBaseEnvironment();
        foreach (var (key, value) in GitAskPassProtocol.CreateEnvironment(_askPassExecutablePath, repositoryId))
        {
            environment[key] = value;
        }

        return _processExecutor.ExecuteAsync(repositoryRoot, "git", arguments, environment, NetworkTimeout, cancellationToken);
    }

    private static string NormalizeSafeDirectory(string repositoryRoot) => repositoryRoot.Replace('\\', '/');

    private static bool IsUnbornHead(GitCommandResult result) =>
        !result.IsSuccess &&
        result.Failure == GitCommandFailure.Failed &&
        result.ExitCode == 1 &&
        result.StandardOutput.Length == 0 &&
        result.StandardError.Length == 0;
}
