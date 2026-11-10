using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Githubie.Application.Configuration;
using Githubie.Application.Git;
using Githubie.Application.GitHub;
using Githubie.Application.Interactive;
using Githubie.Application.Repositories;

namespace Githubie.Application.Publishing;

/// <summary>Publishes one immutable commit without changing any existing remote ref, then creates a PR.</summary>
public sealed class PrPublicationService(RepositoryAllowlist allowlist, LocalPathValidator paths,
    IGitCommandClient git, IPrPublicationGit publicationGit, IGitHubApiClient api,
    IPrPublicationStore store, IInteractiveApprovalPrompt approval, IProviderExecutionGuard guard,
    GithubieOptions configuration, IPrPublicationDiagnostics? diagnostics = null) : IPrPublicationService
{
    private static bool IsSha(string? value) => value is { Length: 40 } && value.All(Uri.IsHexDigit);

    public static bool IsBranch(string? value) => value is { Length: > 0 and <= 255 } && value != "@"
        && !value.StartsWith('-') && !value.StartsWith("refs/", StringComparison.Ordinal)
        && !value.Contains("..", StringComparison.Ordinal) && !value.Contains("@{", StringComparison.Ordinal)
        && value.Split('/').All(part => part.Length > 0 && !part.StartsWith('.') && !part.EndsWith('.')
            && !part.EndsWith(".lock", StringComparison.OrdinalIgnoreCase))
        && value.All(c => !char.IsWhiteSpace(c) && !char.IsControl(c) && "~^:?*[\\".IndexOf(c) < 0);

    public async Task<PrPublicationResult> PublishAsync(string repository, PrPublishInput input, CancellationToken cancellationToken)
    {
        var info = new PrPublicationInfo(input.OperationId, input.SourceBranch, input.DestinationBranch, input.ExpectedSourceSha);
        PrPublicationResult result = new(info, "operation_not_started", "not_executed");
        string? binding = null;
        string? hash = null;
        bool journalStarted = false;
        bool publicationDispatched = false;
        bool prDispatched = false;
        try
        {
            if (!Guid.TryParseExact(input.OperationId, "D", out _) || !IsBranch(input.SourceBranch)
                || !IsBranch(input.DestinationBranch) || input.SourceBranch == input.DestinationBranch
                || !IsSha(input.ExpectedSourceSha) || string.IsNullOrWhiteSpace(input.Title) || input.Title.Length > 256
                || input.Title.Any(char.IsControl) || input.Description?.Length > 65536)
                return Fail(info, "publication_invalid", "not_executed");
            input = input with { OperationId = Guid.Parse(input.OperationId).ToString("D"), ExpectedSourceSha = input.ExpectedSourceSha.ToLowerInvariant() };
            info = new(input.OperationId, input.SourceBranch, input.DestinationBranch, input.ExpectedSourceSha);
            if (!RepositoryId.TryNormalize(repository, out repository) || !allowlist.TryGet(repository, out var options))
                return Fail(info, "repository_not_allowed", "not_executed");
            if (!paths.Validate(options).IsAllowed) return Fail(info, "local_path_invalid", "not_executed");
            binding = Binding(repository, options);
            hash = Hash(JsonSerializer.Serialize(input));
            await using var lease = await store.TryAcquireAsync(repository, cancellationToken);
            if (lease is null) return Fail(info, "operation_in_progress", "not_executed");
            var previous = await store.GetAsync(repository, input.OperationId, cancellationToken);
            if (previous is not null)
            {
                if (previous.Binding != binding || previous.RequestHash != hash)
                    return Fail(info, "operation_conflict", "not_executed");
                if (!previous.Result.Data.RequiresReconciliation) return previous.Result;
                // Replay only performs read-only reconciliation; it never resumes an external mutation.
                var reconciled = await GetStatusAsync(repository, input.OperationId, cancellationToken);
                if (!reconciled.Data.RequiresReconciliation && reconciled.Data.SourceSha == previous.Result.Data.SourceSha
                    && reconciled.Data.SourceBranch == previous.Result.Data.SourceBranch)
                    await SaveAsync(repository, input.OperationId, hash, binding, reconciled);
                return reconciled;
            }
            if (await store.HasUnresolvedAsync(repository, input.SourceBranch, cancellationToken))
                return Fail(info, "source_operation_unresolved", "not_executed");

            string? error = Policy(options, input);
            if (error is not null) return Fail(info, error, "not_executed");
            var remote = await ResolveRemoteAsync(options, cancellationToken);
            if (remote.Error is not null) return Fail(info, remote.Error, "not_executed");
            error = await ValidateLocalAsync(options, input, cancellationToken);
            if (error is not null) return Fail(info, error, "not_executed");
            var repositoryAccess = await api.GetRepositoryAsync(repository, options.GitHubOwner, options.GitHubRepo, cancellationToken);
            if (!repositoryAccess.IsSuccess) return Fail(info, ApiError(repositoryAccess.Error), "not_executed");
            var destination = await api.GetBranchAsync(repository, options.GitHubOwner, options.GitHubRepo, input.DestinationBranch, cancellationToken);
            if (!destination.IsSuccess) return Fail(info, ApiError(destination.Error), "not_executed");
            var observed = await ObserveRemoteAsync(repository, options, remote.Name!, input.SourceBranch, cancellationToken);
            if (observed.Error is not null) return Fail(info, observed.Error, "not_executed");
            if (observed.Sha is not null && !SameSha(observed.Sha, input.ExpectedSourceSha))
                return Fail(info with { ObservedRemoteSha = observed.Sha, ObservedAt = DateTimeOffset.UtcNow }, "remote_branch_conflict", "not_executed");

            var consent = await approval.RequestApprovalAsync(new ApprovalPromptRequest(
                "Githubie - Publish PR source", "Publish one branch commit and create a pull request",
                [repository, $"{input.SourceBranch} -> {input.DestinationBranch}", input.ExpectedSourceSha,
                 $"Operation: {input.OperationId}", "Existing remote refs will not be changed."]), TimeSpan.FromSeconds(120), cancellationToken);
            if (consent.Outcome != ApprovalOutcome.Approved)
                return Fail(info, consent.Outcome == ApprovalOutcome.Denied ? "approval_denied" : "approval_unavailable", "not_executed");

            // Revalidate policies, binding, local source, and effective push destination after approval.
            error = CurrentPolicy(repository, options, input, binding);
            if (error is not null) return Fail(info, error, "not_executed");
            error = await ValidateLocalAsync(options, input, cancellationToken);
            if (error is not null) return Fail(info, error, "not_executed");
            var rechecked = await ResolveRemoteAsync(options, cancellationToken);
            if (rechecked.Error is not null || rechecked.Name != remote.Name)
                return Fail(info, rechecked.Error ?? "provider_remote_mismatch", "not_executed");
            await guard.EnsureCurrentAsync(cancellationToken);
            observed = await ObserveRemoteAsync(repository, options, remote.Name!, input.SourceBranch, cancellationToken);
            if (observed.Error is not null) return Fail(info, observed.Error, "not_executed");
            if (observed.Sha is not null && !SameSha(observed.Sha, input.ExpectedSourceSha))
                return Fail(info, "remote_branch_conflict", "not_executed");

            if (observed.Sha is null)
            {
                result = Fail(info with { PublicationState = "unknown", Stage = "publishing" }, "publication_result_unknown", "unknown");
                await SaveAsync(repository, input.OperationId, hash, binding, result);
                journalStarted = true;
                await guard.EnsureCurrentAsync(cancellationToken);
                error = CurrentPolicy(repository, options, input, binding);
                if (error is not null)
                {
                    result = Fail(info, error, "not_executed");
                    await SaveAsync(repository, input.OperationId, hash, binding, result);
                    return result;
                }
                publicationDispatched = true;
                var pushed = await publicationGit.CreateBranchAtCommitAsync(options.LocalRoot, repository, remote.Name!,
                    input.SourceBranch, input.ExpectedSourceSha, cancellationToken);
                observed = await ObserveRemoteAsync(repository, options, remote.Name!, input.SourceBranch, cancellationToken);
                if (observed.Error is not null || observed.Sha is null)
                {
                    // A single absent-ref read cannot prove a failed transport did not create it later.
                    if (pushed.IsSuccess || pushed.Failure != GitCommandFailure.NotFound || observed.Error is not null)
                        return result;
                    result = Fail(info with { PublicationState = "failed", Stage = "publishing" }, "publication_failed");
                    await SaveAsync(repository, input.OperationId, hash, binding, result);
                    return result;
                }
                if (!SameSha(observed.Sha, input.ExpectedSourceSha))
                {
                    // Could be a concurrent writer after a successful creation. Never claim no effect.
                    result = Fail(result.Data with { ObservedRemoteSha = observed.Sha, ObservedAt = DateTimeOffset.UtcNow },
                        "remote_branch_conflict", "unknown");
                    await SaveAsync(repository, input.OperationId, hash, binding, result);
                    return result;
                }
                bool createdRef = pushed.IsSuccess && pushed.StandardOutput.Split('\n').Any(line => line.StartsWith("*\t", StringComparison.Ordinal));
                info = info with { PublicationState = createdRef ? "created" : "already_present" };
            }
            else info = info with { PublicationState = "already_present" };

            info = info with { ObservedRemoteSha = observed.Sha, ObservedAt = DateTimeOffset.UtcNow, Stage = "published" };
            result = Fail(info, "operation_interrupted");
            await SaveAsync(repository, input.OperationId, hash, binding, result);
            journalStarted = true;
            var existing = await FindPrAsync(repository, options, info, cancellationToken);
            if (existing.Error is not null)
            {
                result = Fail(info, existing.Error);
                await SaveAsync(repository, input.OperationId, hash, binding, result);
                return result;
            }
            if (existing.Pr is not null)
            {
                result = Complete(info, existing.Pr, "already_present");
                await SaveAsync(repository, input.OperationId, hash, binding, result);
                return result;
            }
            // Permission and ref checks are repeated immediately before the second external mutation.
            error = CurrentPolicy(repository, options, input, binding);
            if (error is not null) return await PersistFailure(error);
            error = await ValidateLocalAsync(options, input, cancellationToken);
            if (error is not null) return await PersistFailure(error);
            rechecked = await ResolveRemoteAsync(options, cancellationToken);
            if (rechecked.Error is not null || rechecked.Name != remote.Name)
                return await PersistFailure(rechecked.Error ?? "provider_remote_mismatch");
            observed = await ObserveRemoteAsync(repository, options, remote.Name!, input.SourceBranch, cancellationToken);
            if (observed.Error is not null || !SameSha(observed.Sha, input.ExpectedSourceSha))
                return await PersistFailure(observed.Error ?? "remote_branch_conflict");
            destination = await api.GetBranchAsync(repository, options.GitHubOwner, options.GitHubRepo, input.DestinationBranch, cancellationToken);
            if (!destination.IsSuccess) return await PersistFailure(ApiError(destination.Error));
            await guard.EnsureCurrentAsync(cancellationToken);
            result = Fail(info with { Stage = "creating_pr", PullRequestState = "unknown" }, "pull_request_result_unknown", "unknown");
            await SaveAsync(repository, input.OperationId, hash, binding, result);
            error = CurrentPolicy(repository, options, input, binding);
            if (error is not null) return await PersistFailure(error);
            prDispatched = true;
            var created = await api.CreatePullRequestAsync(repository, options.GitHubOwner, options.GitHubRepo,
                input.SourceBranch, input.DestinationBranch, new(input.Title, input.Description, input.Draft,
                    input.SourceBranch, input.DestinationBranch), cancellationToken);
            if (!created.IsSuccess)
            {
                // A malformed response may follow a successful POST. Never retry it automatically.
                bool unknown = created.Error is GitHubError.NetworkError or GitHubError.Timeout or GitHubError.InvalidResponse or GitHubError.ApiError;
                result = Fail(result.Data with { PullRequestState = unknown ? "unknown" : "failed" },
                    unknown ? "pull_request_result_unknown" : ApiError(created.Error), unknown ? "unknown" : "failed");
            }
            else if (!Matches(created.Value!, info, options))
                result = Fail(result.Data, "pull_request_head_mismatch", "unknown");
            else result = Complete(info, created.Value!, "created");
            await SaveAsync(repository, input.OperationId, hash, binding, result);
            return result;

            async Task<PrPublicationResult> PersistFailure(string code)
            {
                result = Fail(info, code);
                await SaveAsync(repository, input.OperationId, hash, binding, result);
                return result;
            }
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            diagnostics?.Failure("publish", exception.GetType().Name);
            // No raw exception/HTTP/credential text is returned. In-flight states survive cancellation/crashes.
            if (!journalStarted) return Fail(info, "publication_unavailable", "not_executed");
            if (result.Data.Stage == "publishing" && !publicationDispatched)
                result = Fail(result.Data with { PublicationState = "not_attempted", Stage = "validation" }, "operation_interrupted", "not_executed");
            if (result.Data.Stage == "creating_pr" && !prDispatched)
                result = Fail(result.Data with { PullRequestState = "not_attempted", Stage = "published" }, "operation_interrupted");
            if (!result.Data.RequiresReconciliation && result.Outcome != "not_executed")
                result = Fail(result.Data, "operation_interrupted");
            try { await SaveAsync(repository, input.OperationId, hash!, binding!, result); }
            catch (Exception writeException) when (writeException is not OutOfMemoryException)
            { diagnostics?.Failure("journal", writeException.GetType().Name); }
            return result;
        }
    }

    public async Task<PrPublicationResult> GetStatusAsync(string repository, string operationId, CancellationToken cancellationToken)
    {
        var info = new PrPublicationInfo(operationId, "", "", "");
        try
        {
            if (!Guid.TryParseExact(operationId, "D", out _) || !RepositoryId.TryNormalize(repository, out repository)
                || !allowlist.TryGet(repository, out var options)) return Fail(info, "publication_invalid", "not_executed");
            operationId = Guid.Parse(operationId).ToString("D");
            var record = await store.GetAsync(repository, operationId, cancellationToken);
            if (record is null) return Fail(info, "operation_not_found", "not_executed");
            if (record.Binding != Binding(repository, options)) return Fail(info, "authorization_context_changed", "not_executed");
            var result = record.Result;
            info = result.Data;
            if (info.OperationId != operationId || !IsBranch(info.SourceBranch) || !IsBranch(info.DestinationBranch) || !IsSha(info.SourceSha))
                return Fail(new(operationId, "", "", ""), "invalid_operation_record");
            if (!info.RequiresReconciliation) return result;
            if (!paths.Validate(options).IsAllowed) return Fail(info, "local_path_invalid");
            var remote = await ResolveRemoteAsync(options, cancellationToken);
            if (remote.Error is not null) return Fail(info, remote.Error, "unknown");
            await guard.EnsureCurrentAsync(cancellationToken);
            var observed = await ObserveRemoteAsync(repository, options, remote.Name!, info.SourceBranch, cancellationToken);
            if (observed.Error is not null) return Fail(info, observed.Error, "unknown");
            info = info with { ObservedRemoteSha = observed.Sha, ObservedAt = DateTimeOffset.UtcNow };
            if (!SameSha(observed.Sha, info.SourceSha)) return Fail(info, "publication_result_unknown", "unknown");
            info = info with { PublicationState = info.PublicationState == "unknown" ? "already_present" : info.PublicationState };
            if (info.PullRequestState == "unknown")
            {
                var found = await FindPrAsync(repository, options, info, cancellationToken);
                if (found.Pr is not null) return Complete(info, found.Pr, "already_present");
                return Fail(info, found.Error ?? "pull_request_result_unknown", "unknown");
            }
            // Publication was reconciled but no PR POST was recorded. No automatic continuation.
            return Fail(info, "operation_interrupted");
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            diagnostics?.Failure("status", exception.GetType().Name);
            return Fail(info, "status_unavailable", info.RequiresReconciliation ? "unknown" : "failed");
        }
    }

    private async Task<string?> ValidateLocalAsync(RepositoryOptions options, PrPublishInput input, CancellationToken ct)
    {
        if (!paths.Validate(options).IsAllowed) return "local_path_invalid";
        if (options.RequireCleanWorkingTree)
        {
            var status = await git.GetStatusAsync(options.LocalRoot, ct);
            if (!status.IsSuccess) return "git_failed";
            if (!string.IsNullOrWhiteSpace(status.StandardOutput)) return "working_tree_dirty";
        }
        var commit = await git.GetLocalCommitAsync(options.LocalRoot, "refs/heads/" + input.SourceBranch, ct);
        return !commit.IsSuccess ? "branch_not_found" : SameSha(commit.StandardOutput.Trim(), input.ExpectedSourceSha) ? null : "expected_head_mismatch";
    }

    private static string? Policy(RepositoryOptions o, PrPublishInput input) =>
        o.ProtectedBranches.Contains(input.SourceBranch, StringComparer.Ordinal) ? "protected_branch" :
        !o.PrPublicationSourceBranches.Contains(input.SourceBranch, StringComparer.Ordinal) ? "publication_source_not_allowed" :
        !o.PullRequestRoutes.Any(r => r.SourceBranch == input.SourceBranch && r.DestinationBranch == input.DestinationBranch)
            ? "pull_request_route_not_allowed" : null;

    private string? CurrentPolicy(string repository, RepositoryOptions expected, PrPublishInput input, string binding) =>
        !allowlist.TryGet(repository, out var current) || !Equals(expected, current) || Binding(repository, current) != binding
            ? "authorization_context_changed" : Policy(current, input);

    private async Task<(string? Name, string? Error)> ResolveRemoteAsync(RepositoryOptions o, CancellationToken ct)
    {
        string? name = GitRemoteSelection.Current ?? (string.IsNullOrWhiteSpace(o.Remote) ? null : o.Remote.Trim());
        if (name is null)
        {
            var all = await git.ListRemoteUrlsAsync(o.LocalRoot, ct);
            if (!all.IsSuccess) return (null, "provider_remote_not_found");
            var names = new List<string>();
            foreach (var line in all.StandardOutput.Split('\n', StringSplitOptions.RemoveEmptyEntries))
            {
                int space = line.IndexOf(' ');
                if (space < 0) continue;
                string key = line[..space];
                if (key.StartsWith("remote.", StringComparison.OrdinalIgnoreCase) && key.EndsWith(".url", StringComparison.OrdinalIgnoreCase)
                    && GitHubRemoteUrlValidator.IsExpectedRemote(line[(space + 1)..].Trim(), o.GitHubOwner, o.GitHubRepo))
                    names.Add(key[7..^4]);
            }
            var matches = names.Distinct(StringComparer.Ordinal).ToArray();
            if (matches.Length == 0) return (null, "provider_remote_not_found");
            name = matches.Length == 1 ? matches[0] : matches.Contains("github-origin-https", StringComparer.Ordinal) ? "github-origin-https" : null;
            if (name is null) return (null, "provider_remote_ambiguous");
        }
        if (name.Length > 255 || name.StartsWith('-') || name.Any(c => char.IsWhiteSpace(c) || char.IsControl(c)))
            return (null, "provider_remote_not_found");
        var fetch = await git.GetRemoteUrlAsync(o.LocalRoot, name, ct);
        if (!fetch.IsSuccess || !GitHubRemoteUrlValidator.IsExpectedRemote(fetch.StandardOutput.Trim(), o.GitHubOwner, o.GitHubRepo))
            return (null, "provider_remote_mismatch");
        var push = await publicationGit.GetPushUrlsAsync(o.LocalRoot, name, ct);
        var urls = push.StandardOutput.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (!push.IsSuccess || urls.Length != 1 || !GitHubRemoteUrlValidator.IsExpectedRemote(urls[0], o.GitHubOwner, o.GitHubRepo))
            return (null, "provider_remote_mismatch");
        return (name, null);
    }

    private async Task<(string? Sha, string? Error)> ObserveRemoteAsync(string repository, RepositoryOptions o, string remote, string branch, CancellationToken ct)
    {
        var response = await git.GetRemoteRefAsync(o.LocalRoot, repository, remote, "refs/heads/" + branch, ct);
        if (!response.IsSuccess) return (null, "remote_unavailable");
        if (string.IsNullOrWhiteSpace(response.StandardOutput)) return (null, null);
        var parts = response.StandardOutput.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
        return parts.Length == 2 && parts[1] == "refs/heads/" + branch && IsSha(parts[0])
            ? (parts[0], null) : (null, "provider_invalid_response");
    }

    private async Task<(GitHubPullRequestInfo? Pr, string? Error)> FindPrAsync(string repository, RepositoryOptions o, PrPublicationInfo info, CancellationToken ct)
    {
        var found = await api.ListPullRequestsAsync(repository, o.GitHubOwner, o.GitHubRepo, GitHubPullRequestState.Open,
            info.SourceBranch, info.DestinationBranch, ct);
        if (!found.IsSuccess) return (null, ApiError(found.Error));
        var matches = found.Value!.Where(pr => pr.Source == info.SourceBranch && pr.Destination == info.DestinationBranch).ToArray();
        if (matches.Length == 0) return (null, null);
        if (matches.Length != 1 || !Matches(matches[0], info, o)) return (null, "pull_request_head_mismatch");
        return (matches[0], null);
    }

    private static bool Matches(GitHubPullRequestInfo pr, PrPublicationInfo info, RepositoryOptions o) =>
        pr.Number > 0 && pr.State == GitHubPullRequestState.Open && pr.Source == info.SourceBranch
        && pr.Destination == info.DestinationBranch && SameSha(pr.SourceCommitSha, info.SourceSha)
        && string.Equals(pr.HeadRepository, o.GitHubOwner + "/" + o.GitHubRepo, StringComparison.OrdinalIgnoreCase)
        && string.Equals(pr.BaseRepository, o.GitHubOwner + "/" + o.GitHubRepo, StringComparison.OrdinalIgnoreCase)
        && Uri.TryCreate(pr.Url, UriKind.Absolute, out var url) && url.Scheme == "https" && url.Host == "github.com"
        && url.UserInfo.Length == 0 && url.IsDefaultPort
        && url.AbsolutePath.Equals($"/{o.GitHubOwner}/{o.GitHubRepo}/pull/{pr.Number}", StringComparison.OrdinalIgnoreCase);

    private string Binding(string repository, RepositoryOptions o)
    {
        var project = configuration.WindowsOwnerAuthentication ? null : configuration.ProviderAuthentication?.Projects.FirstOrDefault(p => p.Key.Equals(repository, StringComparison.OrdinalIgnoreCase)).Value;
        if (project == Guid.Empty) project = null;
        return Hash(JsonSerializer.Serialize(new { Repository = repository.ToLowerInvariant(), o.GitHubOwner, o.GitHubRepo,
            LocalRoot = Path.GetFullPath(o.LocalRoot), Owner = configuration.WindowsOwnerSid, Project = configuration.WindowsOwnerAuthentication ? null : project ?? o.MoyaiProjectId }));
    }

    private Task SaveAsync(string repository, string id, string hash, string binding, PrPublicationResult result) =>
        store.PutAsync(repository, id, new(hash, binding, result), CancellationToken.None);

    private static string Hash(string value) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value)));
    private static bool SameSha(string? a, string? b) => IsSha(a) && IsSha(b) && string.Equals(a, b, StringComparison.OrdinalIgnoreCase);
    private static PrPublicationResult Fail(PrPublicationInfo info, string error, string outcome = "failed") => new(info, error, outcome);
    private static PrPublicationResult Complete(PrPublicationInfo info, GitHubPullRequestInfo pr, string state) =>
        new(info with { PullRequestState = state, PullRequestNumber = pr.Number, PullRequestUrl = pr.Url,
            PullRequestHeadSha = pr.SourceCommitSha, Stage = "completed" }, null, "succeeded");

    private static string ApiError(GitHubError? error) => error switch
    {
        GitHubError.AuthenticationFailed => "authentication_failed",
        GitHubError.PermissionDenied => "permission_denied",
        GitHubError.TokenScopeMissing => "token_scope_missing",
        GitHubError.BranchNotFound => "branch_not_found",
        GitHubError.RateLimited or GitHubError.SecondaryRateLimited => "rate_limited",
        GitHubError.NetworkError => "network_error",
        GitHubError.Timeout => "timeout",
        _ => "provider_error",
    };
}
