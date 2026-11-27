using Githubie.Application.Git;

namespace Githubie.Infrastructure.Git;

internal delegate Task<GitCommandResult> LocalGitCommand(
    IReadOnlyList<string> arguments, CancellationToken cancellationToken,
    IReadOnlyDictionary<string, string>? environment);

/// <summary>現在branchのrefとindexを、他のcheckoutやcommitと競合させずに更新します。</summary>
internal sealed class CurrentBranchCommit(LocalGitCommand execute)
{
    public async Task<GitCommandResult> ExecuteAsync(string message, string authorName, string authorEmail,
        string expectedBranch, string expectedHead, string? patch, CancellationToken cancellationToken)
    {
        if (!OperatingSystem.IsWindows()) return Fail(GitCommandFailure.AtomicNotSupported);
        var directory = await execute(["rev-parse", "--absolute-git-dir"], cancellationToken, null);
        if (!directory.IsSuccess) return directory;
        var gitDirectory = directory.StandardOutput.Trim();
        var headPath = Path.Combine(gitDirectory, "HEAD");
        var indexPath = Path.Combine(gitDirectory, "index");
        var indexLockPath = indexPath + ".lock";
        var temporaryIndex = Path.Combine(gitDirectory, $"githubie-commit-{Guid.NewGuid():N}.index");
        var patchPath = temporaryIndex + ".patch";
        FileStream? indexLock = null;
        FileStream? headGuard = null;
        var ownsIndexLock = false;
        try
        {
            // Windowsの共有モードでHEADの書換え/置換を禁止し、Git自身のHEAD.lock取得は妨げません。
            headGuard = new FileStream(headPath, FileMode.Open, FileAccess.Read, FileShare.Read);
            indexLock = new FileStream(indexLockPath, FileMode.CreateNew, FileAccess.Write, FileShare.None);
            ownsIndexLock = true;
            var verified = await VerifyAsync(expectedBranch, expectedHead, cancellationToken);
            if (!verified.IsSuccess) return verified;
            if (new[] { "MERGE_HEAD", "CHERRY_PICK_HEAD", "REVERT_HEAD", "rebase-merge", "rebase-apply" }
                .Any(name => File.Exists(Path.Combine(gitDirectory, name)) || Directory.Exists(Path.Combine(gitDirectory, name))))
                return Fail(GitCommandFailure.CommitInProgress);

            if (patch is not null)
            {
                var staged = await execute(["diff", "--cached", "--quiet", "--"], cancellationToken, null);
                if (!staged.IsSuccess)
                    return staged.ExitCode == 1 ? Fail(GitCommandFailure.StagedChangesPresent) : staged;
            }
            var environment = new Dictionary<string, string> { ["GIT_INDEX_FILE"] = temporaryIndex };
            if (patch is null && File.Exists(indexPath)) File.Copy(indexPath, temporaryIndex);
            else
            {
                var initialized = await execute(expectedHead.Length == 0
                    ? ["read-tree", "--empty"] : ["read-tree", expectedHead], cancellationToken, environment);
                if (!initialized.IsSuccess) return initialized;
            }
            GitCommandResult prepared;
            if (patch is null)
                prepared = await execute(["add", "--all", "--"], cancellationToken, environment);
            else
            {
                await File.WriteAllTextAsync(patchPath, patch.EndsWith('\n') ? patch : patch + "\n",
                    new System.Text.UTF8Encoding(false), cancellationToken);
                var checkedPatch = await execute(["apply", "--cached", "--check", patchPath], cancellationToken, environment);
                if (!checkedPatch.IsSuccess) return checkedPatch;
                prepared = await execute(["apply", "--cached", patchPath], cancellationToken, environment);
            }
            if (!prepared.IsSuccess) return prepared;
            var tree = await execute(["write-tree"], cancellationToken, environment);
            if (!tree.IsSuccess) return tree;
            if (expectedHead.Length > 0)
            {
                var oldTree = await execute(["rev-parse", $"{expectedHead}^{{tree}}"], cancellationToken, null);
                if (!oldTree.IsSuccess) return oldTree;
                if (tree.StandardOutput == oldTree.StandardOutput) return Fail(GitCommandFailure.NothingToCommit);
            }
            List<string> arguments = ["-c", $"user.name={authorName}", "-c", $"user.email={authorEmail}", "commit-tree", tree.StandardOutput.Trim()];
            if (expectedHead.Length > 0) arguments.AddRange(["-p", expectedHead]);
            arguments.AddRange(["-m", message]);
            var commit = await execute(arguments, cancellationToken, environment);
            if (!commit.IsSuccess) return commit;
            var commitSha = commit.StandardOutput.Trim();
            if (commitSha.Length is not (40 or 64) || !commitSha.All(Uri.IsHexDigit))
                return Fail(GitCommandFailure.Failed);
            verified = await VerifyAsync(expectedBranch, expectedHead, cancellationToken);
            if (!verified.IsSuccess) return verified;

            // ref更新前にindexを準備し、取消可能な処理をすべて済ませます。
            await indexLock.WriteAsync(await File.ReadAllBytesAsync(temporaryIndex, cancellationToken), cancellationToken);
            await indexLock.FlushAsync(cancellationToken);
            indexLock.Flush(flushToDisk: true);
            cancellationToken.ThrowIfCancellationRequested();
            var reference = $"refs/heads/{expectedBranch}";
            var updated = await execute(["-c", $"user.name={authorName}", "-c", $"user.email={authorEmail}",
                "update-ref", "-m", "commit: " + message, reference, commitSha, expectedHead], CancellationToken.None, null);
            if (!updated.IsSuccess)
            {
                var observed = await execute(["rev-parse", "--verify", "--quiet", reference], CancellationToken.None, null);
                if (!observed.IsSuccess && !(expectedHead.Length == 0 && observed.ExitCode == 1))
                    return Fail(GitCommandFailure.CommitResultUnknown);
                if (!string.Equals(observed.StandardOutput.Trim(), commitSha, StringComparison.OrdinalIgnoreCase))
                {
                    var state = await VerifyAsync(expectedBranch, expectedHead, CancellationToken.None);
                    if (!state.IsSuccess) return state;
                    return updated.StandardError.Contains("cannot lock", StringComparison.OrdinalIgnoreCase)
                        ? Fail(GitCommandFailure.RepositoryBusy) : updated;
                }
            }
            indexLock.Dispose();
            indexLock = null;
            try
            {
                File.Move(indexLockPath, indexPath, overwrite: true);
                ownsIndexLock = false;
                return GitCommandResult.Success(commitSha);
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                // indexは未変更。自分が作ったrefだけをCASで戻し、他操作を上書きしません。
                string[] rollbackArguments = expectedHead.Length == 0
                    ? ["update-ref", "-d", reference, commitSha]
                    : ["update-ref", reference, expectedHead, commitSha];
                var rollback = await execute(new[] { "-c", $"user.name={authorName}", "-c", $"user.email={authorEmail}" }
                    .Concat(rollbackArguments).ToArray(), CancellationToken.None, null);
                return rollback.IsSuccess ? Fail(GitCommandFailure.IndexUpdateFailed) : Fail(GitCommandFailure.CommitResultUnknown);
            }
        }
        catch (OperationCanceledException) { return Fail(GitCommandFailure.Cancelled); }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return Fail(GitCommandFailure.RepositoryBusy);
        }
        finally
        {
            indexLock?.Dispose();
            if (ownsIndexLock) File.Delete(indexLockPath);
            headGuard?.Dispose();
            File.Delete(temporaryIndex);
            File.Delete(temporaryIndex + ".lock");
            File.Delete(patchPath);
        }
    }

    private async Task<GitCommandResult> VerifyAsync(string branch, string head, CancellationToken cancellationToken)
    {
        var current = await execute(["symbolic-ref", "--quiet", "HEAD"], cancellationToken, null);
        if (!current.IsSuccess) return current.ExitCode == 1 ? Fail(GitCommandFailure.DetachedHead) : current;
        if (current.StandardOutput.Trim() != $"refs/heads/{branch}") return Fail(GitCommandFailure.BranchChanged);
        var actualHead = await execute(["rev-parse", "--verify", "--quiet", "HEAD"], cancellationToken, null);
        if (!actualHead.IsSuccess && !(head.Length == 0 && actualHead.ExitCode == 1)) return actualHead;
        return string.Equals(actualHead.StandardOutput.Trim(), head, StringComparison.OrdinalIgnoreCase)
            ? GitCommandResult.Success(string.Empty) : Fail(GitCommandFailure.HeadChanged);
    }

    private static GitCommandResult Fail(GitCommandFailure failure) => GitCommandResult.Failed(failure);
}
