using System.Text;
using Githubie.Application.Git;

namespace Githubie.Infrastructure.Git;

/// <summary>
/// 現在branchへ取り込み元をmergeします。merge結果は<c>git merge-tree --write-tree</c>で作業ツリーに触れずに計算し、
/// 衝突・不一致・解消漏れのときは作業ツリー、index、HEADを一切変更せずに拒否します。pushは行いません。
/// </summary>
internal sealed class CurrentBranchMerge(LocalGitCommand execute)
{
    private static readonly string[] InProgressMarkers =
        ["MERGE_HEAD", "CHERRY_PICK_HEAD", "REVERT_HEAD", "rebase-merge", "rebase-apply"];

    public async Task<GitMergeCommandOutcome> ExecuteAsync(GitMergeRequest request, CancellationToken cancellationToken)
    {
        if (!OperatingSystem.IsWindows()) return Failed(GitCommandFailure.AtomicNotSupported);
        var directory = await execute(["rev-parse", "--absolute-git-dir"], cancellationToken, null);
        if (!directory.IsSuccess) return GitMergeCommandOutcome.Failed(directory);
        var gitDirectory = directory.StandardOutput.Trim();

        // 作業ツリーの確認はindex lockの取得前に行います（statusはindexの更新を試みるため）。
        // lock取得後の変更は、作業ツリー更新時の read-tree -m -u の最新性検査が拒否します。
        var verified = await VerifyAsync(request.Branch, request.ExpectedHead, cancellationToken);
        if (!verified.IsSuccess) return GitMergeCommandOutcome.Failed(verified);
        var status = await execute(["status", "--porcelain", "--untracked-files=all", "--ignore-submodules=none"], cancellationToken, null);
        if (!status.IsSuccess) return GitMergeCommandOutcome.Failed(status);
        if (status.StandardOutput.Trim().Length > 0) return GitMergeCommandOutcome.Rejected(GitMergeRejection.WorkingTreeDirty);

        var headPath = Path.Combine(gitDirectory, "HEAD");
        var indexPath = Path.Combine(gitDirectory, "index");
        var indexLockPath = indexPath + ".lock";
        var token = Guid.NewGuid().ToString("N");
        var resolutionIndex = Path.Combine(gitDirectory, $"githubie-merge-{token}.resolve.index");
        var worktreeIndex = Path.Combine(gitDirectory, $"githubie-merge-{token}.worktree.index");
        var contentPath = Path.Combine(gitDirectory, $"githubie-merge-{token}.content");
        FileStream? indexLock = null;
        FileStream? headGuard = null;
        var ownsIndexLock = false;
        try
        {
            headGuard = new FileStream(headPath, FileMode.Open, FileAccess.Read, FileShare.Read);
            indexLock = new FileStream(indexLockPath, FileMode.CreateNew, FileAccess.Write, FileShare.None);
            ownsIndexLock = true;
            verified = await VerifyAsync(request.Branch, request.ExpectedHead, cancellationToken);
            if (!verified.IsSuccess) return GitMergeCommandOutcome.Failed(verified);
            if (InProgressMarkers.Any(name => File.Exists(Path.Combine(gitDirectory, name)) || Directory.Exists(Path.Combine(gitDirectory, name))))
                return Failed(GitCommandFailure.CommitInProgress);

            var source = await execute(["rev-parse", "--verify", "--quiet", $"{request.SourceRef}^{{commit}}"], cancellationToken, null);
            if (!source.IsSuccess)
                return source.Failure == GitCommandFailure.Failed
                    ? GitMergeCommandOutcome.Rejected(GitMergeRejection.SourceNotFound)
                    : GitMergeCommandOutcome.Failed(source);
            var sourceSha = source.StandardOutput.Trim().ToLowerInvariant();
            if (!string.Equals(sourceSha, request.ExpectedSourceSha, StringComparison.OrdinalIgnoreCase))
                return GitMergeCommandOutcome.Rejected(GitMergeRejection.ExpectedSourceMismatch);

            var head = request.ExpectedHead;
            var alreadyMerged = await IsAncestorAsync(sourceSha, head, cancellationToken);
            if (alreadyMerged.Command is not null) return GitMergeCommandOutcome.Failed(alreadyMerged.Command);
            if (alreadyMerged.Value) return GitMergeCommandOutcome.Rejected(GitMergeRejection.NothingToMerge);
            var fastForwardable = await IsAncestorAsync(head, sourceSha, cancellationToken);
            if (fastForwardable.Command is not null) return GitMergeCommandOutcome.Failed(fastForwardable.Command);
            if (request.FastForward == GitMergeFastForwardMode.Only && !fastForwardable.Value)
                return GitMergeCommandOutcome.Rejected(GitMergeRejection.FastForwardNotPossible);

            string newCommit;
            IReadOnlyList<string> parents;
            IReadOnlyList<string> resolvedPaths = [];
            var fastForward = fastForwardable.Value && request.FastForward != GitMergeFastForwardMode.Never;
            if (fastForward)
            {
                if (request.Resolutions.Count > 0) return GitMergeCommandOutcome.Rejected(GitMergeRejection.ResolutionInvalid);
                newCommit = sourceSha;
                var sourceParents = await execute(["rev-list", "--parents", "-n", "1", sourceSha], cancellationToken, null);
                if (!sourceParents.IsSuccess) return GitMergeCommandOutcome.Failed(sourceParents);
                parents = sourceParents.StandardOutput.Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries).Skip(1).ToArray();
            }
            else
            {
                var merged = await execute(["merge-tree", "--write-tree", "--no-messages", "-z", "--name-only", head, sourceSha], cancellationToken, null);
                if (!merged.IsSuccess && !(merged.Failure == GitCommandFailure.Failed && merged.ExitCode == 1))
                    return GitMergeCommandOutcome.Failed(merged);
                var fields = merged.StandardOutput.Split('\0');
                var tree = fields[0].Trim();
                if (!IsObjectId(tree)) return Failed(GitCommandFailure.Failed);
                var conflicts = fields.Skip(1).Where(field => field.Length > 0).Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToArray();
                if (merged.IsSuccess != (conflicts.Length == 0)) return Failed(GitCommandFailure.Failed);

                if (conflicts.Length > 0)
                {
                    var conflictSet = conflicts.ToHashSet(StringComparer.Ordinal);
                    if (request.Resolutions.Any(item => !conflictSet.Contains(item.Path)))
                        return GitMergeCommandOutcome.Rejected(GitMergeRejection.ResolutionInvalid, conflicts);
                    var unresolved = conflicts.Where(path => request.Resolutions.All(item => item.Path != path)).ToArray();
                    if (unresolved.Length > 0) return GitMergeCommandOutcome.Rejected(GitMergeRejection.Conflict, unresolved);
                    var resolvedTree = await ResolveAsync(tree, head, sourceSha, request.Resolutions, resolutionIndex, contentPath, cancellationToken);
                    if (resolvedTree.Failure is not null) return resolvedTree.Failure;
                    tree = resolvedTree.Tree!;
                    resolvedPaths = conflicts;
                }
                else if (request.Resolutions.Count > 0)
                {
                    return GitMergeCommandOutcome.Rejected(GitMergeRejection.ResolutionInvalid);
                }

                var commit = await execute(["-c", $"user.name={request.AuthorName}", "-c", $"user.email={request.AuthorEmail}",
                    "commit-tree", tree, "-p", head, "-p", sourceSha, "-m", request.Message], cancellationToken, null);
                if (!commit.IsSuccess) return GitMergeCommandOutcome.Failed(commit);
                newCommit = commit.StandardOutput.Trim().ToLowerInvariant();
                if (!IsObjectId(newCommit)) return Failed(GitCommandFailure.Failed);
                parents = [head, sourceSha];
            }

            verified = await VerifyAsync(request.Branch, head, cancellationToken);
            if (!verified.IsSuccess) return GitMergeCommandOutcome.Failed(verified);

            // 作業ツリーは専用indexで2-tree mergeとして更新します。cleanな作業ツリーへの適用で、
            // 実行前と異なるファイルがあればGitが拒否し、何も書き換えません。
            File.Copy(indexPath, worktreeIndex);
            var worktreeEnvironment = new Dictionary<string, string> { ["GIT_INDEX_FILE"] = worktreeIndex };
            var checkout = await execute(["read-tree", "-m", "-u", head, newCommit], cancellationToken, worktreeEnvironment);
            if (!checkout.IsSuccess) return GitMergeCommandOutcome.Failed(checkout);

            await indexLock.WriteAsync(await File.ReadAllBytesAsync(worktreeIndex, CancellationToken.None), CancellationToken.None);
            await indexLock.FlushAsync(CancellationToken.None);
            indexLock.Flush(flushToDisk: true);
            var reference = $"refs/heads/{request.Branch}";
            var updated = await execute(["-c", $"user.name={request.AuthorName}", "-c", $"user.email={request.AuthorEmail}",
                "update-ref", "-m", (fastForward ? "merge (fast-forward): " : "merge: ") + FirstLine(request.Message),
                reference, newCommit, head], CancellationToken.None, null);
            if (!updated.IsSuccess)
            {
                var observed = await execute(["rev-parse", "--verify", "--quiet", reference], CancellationToken.None, null);
                if (!observed.IsSuccess) return Failed(GitCommandFailure.CommitResultUnknown);
                if (!string.Equals(observed.StandardOutput.Trim(), newCommit, StringComparison.OrdinalIgnoreCase))
                {
                    // refは未更新。作業ツリーを実行前へ戻します。
                    var restored = await execute(["read-tree", "-m", "-u", newCommit, head], CancellationToken.None, worktreeEnvironment);
                    if (!restored.IsSuccess) return Failed(GitCommandFailure.CommitResultUnknown);
                    return updated.StandardError.Contains("cannot lock", StringComparison.OrdinalIgnoreCase)
                        ? Failed(GitCommandFailure.RepositoryBusy) : Failed(GitCommandFailure.HeadChanged);
                }
            }

            indexLock.Dispose();
            indexLock = null;
            try
            {
                File.Move(indexLockPath, indexPath, overwrite: true);
                ownsIndexLock = false;
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                // indexは未変更。自分が更新したrefだけをCASで戻し、作業ツリーも戻します。
                var rollback = await execute(["-c", $"user.name={request.AuthorName}", "-c", $"user.email={request.AuthorEmail}",
                    "update-ref", reference, head, newCommit], CancellationToken.None, null);
                var restored = rollback.IsSuccess
                    ? await execute(["read-tree", "-m", "-u", newCommit, head], CancellationToken.None, worktreeEnvironment)
                    : rollback;
                return restored.IsSuccess ? Failed(GitCommandFailure.IndexUpdateFailed) : Failed(GitCommandFailure.CommitResultUnknown);
            }

            return GitMergeCommandOutcome.Succeeded(new(request.Branch, newCommit, parents, fastForward, head, sourceSha, resolvedPaths));
        }
        catch (OperationCanceledException) { return Failed(GitCommandFailure.Cancelled); }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return Failed(GitCommandFailure.RepositoryBusy);
        }
        finally
        {
            indexLock?.Dispose();
            if (ownsIndexLock) File.Delete(indexLockPath);
            headGuard?.Dispose();
            foreach (var path in new[] { resolutionIndex, resolutionIndex + ".lock", worktreeIndex, worktreeIndex + ".lock", contentPath })
                File.Delete(path);
        }
    }

    /// <summary>merge-treeの結果treeから、明示された衝突pathだけを置き換えたtreeを作ります。</summary>
    private async Task<(string? Tree, GitMergeCommandOutcome? Failure)> ResolveAsync(string mergedTree, string head, string source,
        IReadOnlyList<GitMergeResolution> resolutions, string indexPath, string contentPath, CancellationToken cancellationToken)
    {
        var environment = new Dictionary<string, string> { ["GIT_INDEX_FILE"] = indexPath };
        var loaded = await execute(["read-tree", mergedTree], cancellationToken, environment);
        if (!loaded.IsSuccess) return (null, GitMergeCommandOutcome.Failed(loaded));
        foreach (var resolution in resolutions)
        {
            string? mode;
            string? blob;
            if (resolution.Choice == GitMergeResolution.FullContent)
            {
                await File.WriteAllTextAsync(contentPath, resolution.Content!, new UTF8Encoding(false), cancellationToken);
                var hashed = await execute(["hash-object", "-w", $"--path={resolution.Path}", "--", contentPath], cancellationToken, null);
                if (!hashed.IsSuccess) return (null, GitMergeCommandOutcome.Failed(hashed));
                blob = hashed.StandardOutput.Trim();
                mode = (await ReadEntryAsync(head, resolution.Path, cancellationToken)).Mode
                    ?? (await ReadEntryAsync(source, resolution.Path, cancellationToken)).Mode ?? "100644";
            }
            else
            {
                var side = resolution.Choice == GitMergeResolution.Current ? head : source;
                var entry = await ReadEntryAsync(side, resolution.Path, cancellationToken);
                if (entry.Failure is not null) return (null, entry.Failure);
                (mode, blob) = (entry.Mode, entry.Blob);
            }

            var applied = mode is null
                ? await execute(["update-index", "--force-remove", "--", resolution.Path], cancellationToken, environment)
                : await execute(["update-index", "--add", "--cacheinfo", mode, blob!, resolution.Path], cancellationToken, environment);
            if (!applied.IsSuccess) return (null, GitMergeCommandOutcome.Failed(applied));
        }
        var tree = await execute(["write-tree"], cancellationToken, environment);
        if (!tree.IsSuccess) return (null, GitMergeCommandOutcome.Failed(tree));
        var value = tree.StandardOutput.Trim();
        return IsObjectId(value) ? (value, null) : (null, Failed(GitCommandFailure.Failed));
    }

    /// <summary>指定commitのpathのblobとmodeを返します。pathが存在しない場合はmode/blobともnullです。</summary>
    private async Task<(string? Mode, string? Blob, GitMergeCommandOutcome? Failure)> ReadEntryAsync(
        string commit, string path, CancellationToken cancellationToken)
    {
        var listed = await execute(["ls-tree", "-z", "--full-tree", commit, "--", path], cancellationToken, null);
        if (!listed.IsSuccess) return (null, null, GitMergeCommandOutcome.Failed(listed));
        foreach (var record in listed.StandardOutput.Split('\0', StringSplitOptions.RemoveEmptyEntries))
        {
            var tab = record.IndexOf('\t');
            if (tab <= 0 || record[(tab + 1)..] != path) continue;
            var parts = record[..tab].Split(' ');
            if (parts.Length == 3 && parts[1] == "blob" && IsObjectId(parts[2])) return (parts[0], parts[2], null);
            return (null, null, Failed(GitCommandFailure.Failed));
        }
        return (null, null, null);
    }

    private async Task<(bool Value, GitCommandResult? Command)> IsAncestorAsync(string ancestor, string descendant, CancellationToken cancellationToken)
    {
        var result = await execute(["merge-base", "--is-ancestor", ancestor, descendant], cancellationToken, null);
        if (result.IsSuccess) return (true, null);
        return result.Failure == GitCommandFailure.Failed && result.ExitCode == 1 ? (false, null) : (false, result);
    }

    private async Task<GitCommandResult> VerifyAsync(string branch, string head, CancellationToken cancellationToken)
    {
        var current = await execute(["symbolic-ref", "--quiet", "HEAD"], cancellationToken, null);
        if (!current.IsSuccess) return current.ExitCode == 1 ? GitCommandResult.Failed(GitCommandFailure.DetachedHead) : current;
        if (current.StandardOutput.Trim() != $"refs/heads/{branch}") return GitCommandResult.Failed(GitCommandFailure.BranchChanged);
        var actualHead = await execute(["rev-parse", "--verify", "--quiet", "HEAD"], cancellationToken, null);
        if (!actualHead.IsSuccess) return actualHead;
        return string.Equals(actualHead.StandardOutput.Trim(), head, StringComparison.OrdinalIgnoreCase)
            ? GitCommandResult.Success(string.Empty) : GitCommandResult.Failed(GitCommandFailure.HeadChanged);
    }

    private static string FirstLine(string message)
    {
        var line = message.Split('\n', 2)[0].Trim();
        return line.Length > 200 ? line[..200] : line;
    }

    private static bool IsObjectId(string value) => value.Length is 40 or 64 && value.All(Uri.IsHexDigit);

    private static GitMergeCommandOutcome Failed(GitCommandFailure failure) => GitMergeCommandOutcome.Failed(GitCommandResult.Failed(failure));
}
