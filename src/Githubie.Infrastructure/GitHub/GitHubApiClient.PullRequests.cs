using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;
using Githubie.Application.Credentials;
using Githubie.Application.GitHub;

namespace Githubie.Infrastructure.GitHub;

public sealed partial class GitHubApiClient
{
    public async Task<GitHubResult<IReadOnlyList<GitHubPullRequestInfo>>> ListPullRequestsAsync(
        string repositoryId, string owner, string repo, GitHubPullRequestState? state, string? source, string? destination, CancellationToken cancellationToken)
    {
        var query = new List<string> { $"per_page={PageSize}" };
        if (state is not null)
        {
            query.Add($"state={MapStateQuery(state.Value)}");
        }

        if (!string.IsNullOrWhiteSpace(source))
        {
            query.Add($"head={Uri.EscapeDataString($"{owner}:{source}")}");
        }

        if (!string.IsNullOrWhiteSpace(destination))
        {
            query.Add($"base={Uri.EscapeDataString(destination)}");
        }

        var path = $"repos/{owner}/{repo}/pulls?{string.Join('&', query)}";
        var page = await GetAllPagesAsync<PullRequestResponse>(repositoryId, path, cancellationToken);
        if (!page.IsSuccess)
        {
            return GitHubResult<IReadOnlyList<GitHubPullRequestInfo>>.FailureFrom(page);
        }

        var items = page.Value!.Where(IsValidPullRequest).Select(ToPullRequestInfo).ToArray();
        return GitHubResult<IReadOnlyList<GitHubPullRequestInfo>>.Success(items);
    }

    public async Task<GitHubResult<GitHubPullRequestInfo>> GetPullRequestAsync(string repositoryId, string owner, string repo, int number, CancellationToken cancellationToken)
    {
        var response = await SendAsync(repositoryId, HttpMethod.Get, $"repos/{owner}/{repo}/pulls/{number}", null, cancellationToken, notFoundError: GitHubError.PullRequestNotFound);
        if (!response.IsSuccess)
        {
            return GitHubResult<GitHubPullRequestInfo>.FailureFrom(response);
        }

        var body = await ReadAsync<PullRequestResponse>(response.Value!, cancellationToken);
        if (body is null || !IsValidPullRequest(body) || body.Number != number || !GitHubCommitSha.IsValid(body.Head?.Sha))
        {
            return GitHubResult<GitHubPullRequestInfo>.Failure(GitHubError.InvalidResponse);
        }

        return GitHubResult<GitHubPullRequestInfo>.Success(ToPullRequestInfo(body));
    }

    public async Task<GitHubResult<GitHubPullRequestDiff>> GetPullRequestDiffAsync(string repositoryId, string owner, string repo, int number, CancellationToken cancellationToken)
    {
        var stats = await SendAsync(repositoryId, HttpMethod.Get, $"repos/{owner}/{repo}/pulls/{number}", null, cancellationToken, notFoundError: GitHubError.PullRequestNotFound);
        if (!stats.IsSuccess)
        {
            return GitHubResult<GitHubPullRequestDiff>.FailureFrom(stats);
        }

        var statsBody = await ReadAsync<PullRequestResponse>(stats.Value!, cancellationToken);
        if (statsBody is null)
        {
            return GitHubResult<GitHubPullRequestDiff>.Failure(GitHubError.InvalidResponse);
        }

        var diffResponse = await SendAsync(
            repositoryId, HttpMethod.Get, $"repos/{owner}/{repo}/pulls/{number}", "application/vnd.github.v3.diff", cancellationToken, notFoundError: GitHubError.PullRequestNotFound);
        if (!diffResponse.IsSuccess)
        {
            return GitHubResult<GitHubPullRequestDiff>.FailureFrom(diffResponse);
        }

        var diffText = await diffResponse.Value!.Content.ReadAsStringAsync(cancellationToken);

        return GitHubResult<GitHubPullRequestDiff>.Success(new GitHubPullRequestDiff(
            diffText, statsBody.ChangedFiles ?? 0, statsBody.Additions ?? 0, statsBody.Deletions ?? 0));
    }

    public async Task<GitHubResult<GitHubPullRequestInfo>> CreatePullRequestAsync(
        string repositoryId, string owner, string repo, string source, string destination, GitHubPullRequestCreate request, CancellationToken cancellationToken)
    {
        var payload = new CreatePullRequestRequest(request.Title, request.Description, source, destination, request.Draft);
        var response = await SendAsync(repositoryId, HttpMethod.Post, $"repos/{owner}/{repo}/pulls", null, cancellationToken, jsonBody: payload);
        if (!response.IsSuccess)
        {
            return GitHubResult<GitHubPullRequestInfo>.FailureFrom(response);
        }

        var body = await ReadAsync<PullRequestResponse>(response.Value!, cancellationToken);
        if (body is null || !IsValidPullRequest(body))
        {
            return GitHubResult<GitHubPullRequestInfo>.Failure(GitHubError.InvalidResponse);
        }

        return GitHubResult<GitHubPullRequestInfo>.Success(ToPullRequestInfo(body));
    }

    public async Task<GitHubResult<GitHubPullRequestInfo>> MergePullRequestAsync(
        string repositoryId, string owner, string repo, GitHubPullRequestMerge request, CancellationToken cancellationToken)
    {
        try
        {
            var payload = new MergePullRequestRequest(request.CommitMessage,
                request.MergeMethod is null ? null : MapMergeMethod(request.MergeMethod.Value), request.ExpectedSourceCommitSha);
            var response = await SendAsync(
                repositoryId, HttpMethod.Put, $"repos/{owner}/{repo}/pulls/{request.Number}/merge", null, cancellationToken,
                jsonBody: payload, notFoundError: GitHubError.PullRequestNotFound,
                conflictError: request.ExpectedSourceCommitSha is null ? GitHubError.PullRequestNotMergeable : GitHubError.ExpectedSourceCommitMismatch);
            if (!response.IsSuccess)
            {
                if (response.Error is GitHubError.NetworkError or GitHubError.Timeout
                    || response.HttpStatus is >= 500 or 408)
                    return GitHubResult<GitHubPullRequestInfo>.Failure(GitHubError.PullRequestMergeResultUnknown, response.Diagnostic, response.HttpStatus);
                return GitHubResult<GitHubPullRequestInfo>.FailureFrom(response);
            }

            using var httpResponse = response.Value!;
            var proof = await ReadAsync<MergePullRequestResponse>(httpResponse, cancellationToken);
            if (proof?.Merged != true || !GitHubCommitSha.IsValid(proof.Sha))
                return GitHubResult<GitHubPullRequestInfo>.Failure(GitHubError.PullRequestMergeResultUnknown);
            var observed = await GetPullRequestAsync(repositoryId, owner, repo, request.Number, cancellationToken);
            if (!observed.IsSuccess || observed.Value is not { State: GitHubPullRequestState.Merged } final
                || final.Number != request.Number || !GitHubCommitSha.IsValid(final.SourceCommitSha)
                || !string.Equals(final.MergeCommitSha, proof.Sha, StringComparison.OrdinalIgnoreCase)
                || (request.ExpectedSourceCommitSha is not null && !string.Equals(final.SourceCommitSha,
                    request.ExpectedSourceCommitSha, StringComparison.OrdinalIgnoreCase)))
                return GitHubResult<GitHubPullRequestInfo>.Failure(GitHubError.PullRequestMergeResultUnknown, observed.Diagnostic, observed.HttpStatus);
            return observed;
        }
        catch (Exception exception) when (exception is OperationCanceledException or HttpRequestException or IOException or JsonException)
        {
            // 送信後のキャンセル・本文欠落を未実行扱いにせず、別GETでの照合を要求します。
            return GitHubResult<GitHubPullRequestInfo>.Failure(GitHubError.PullRequestMergeResultUnknown);
        }
    }

    public async Task<GitHubResult<GitHubPullRequestInfo>> UpdatePullRequestStateAsync(
        string repositoryId, string owner, string repo, int number, GitHubPullRequestState state, CancellationToken cancellationToken)
    {
        if (state is not (GitHubPullRequestState.Open or GitHubPullRequestState.Closed))
            return GitHubResult<GitHubPullRequestInfo>.Failure(GitHubError.PullRequestStateNotAllowed);
        var payload = new UpdatePullRequestRequest(state == GitHubPullRequestState.Open ? "open" : "closed");
        var response = await SendAsync(
            repositoryId, HttpMethod.Patch, $"repos/{owner}/{repo}/pulls/{number}", null, cancellationToken,
            jsonBody: payload, notFoundError: GitHubError.PullRequestNotFound,
            unprocessableError: GitHubError.PullRequestStateNotAllowed);
        if (!response.IsSuccess) return GitHubResult<GitHubPullRequestInfo>.FailureFrom(response);
        var body = await ReadAsync<PullRequestResponse>(response.Value!, cancellationToken);
        return body is not null && IsValidPullRequest(body)
            ? GitHubResult<GitHubPullRequestInfo>.Success(ToPullRequestInfo(body))
            : GitHubResult<GitHubPullRequestInfo>.Failure(GitHubError.InvalidResponse);
    }

    public async Task<GitHubResult<IReadOnlyList<GitHubPullRequestComment>>> ListPullRequestCommentsAsync(
        string repositoryId, string owner, string repo, int number, CancellationToken cancellationToken)
    {
        var page = await GetAllPagesAsync<IssueCommentResponse>(
            repositoryId, $"repos/{owner}/{repo}/issues/{number}/comments", cancellationToken);
        if (!page.IsSuccess) return GitHubResult<IReadOnlyList<GitHubPullRequestComment>>.FailureFrom(page);
        var comments = page.Value!.Where(IsValidComment).Select(ToPullRequestComment).ToArray();
        return GitHubResult<IReadOnlyList<GitHubPullRequestComment>>.Success(comments);
    }

    public async Task<GitHubResult<GitHubPullRequestComment>> CreatePullRequestCommentAsync(
        string repositoryId, string owner, string repo, int number, string body, CancellationToken cancellationToken)
    {
        var response = await SendAsync(
            repositoryId, HttpMethod.Post, $"repos/{owner}/{repo}/issues/{number}/comments", null, cancellationToken,
            jsonBody: new CreateIssueCommentRequest(body), notFoundError: GitHubError.PullRequestNotFound,
            unprocessableError: GitHubError.PullRequestCommentInvalid);
        if (!response.IsSuccess) return GitHubResult<GitHubPullRequestComment>.FailureFrom(response);
        var item = await ReadAsync<IssueCommentResponse>(response.Value!, cancellationToken);
        return item is not null && IsValidComment(item)
            ? GitHubResult<GitHubPullRequestComment>.Success(ToPullRequestComment(item))
            : GitHubResult<GitHubPullRequestComment>.Failure(GitHubError.InvalidResponse);
    }

    public async Task<GitHubResult<GitHubPullRequestReview>> CreatePullRequestReviewAsync(
        string repositoryId, string owner, string repo, int number, GitHubPullRequestReviewAction action,
        string? body, CancellationToken cancellationToken)
    {
        var reviewEvent = action == GitHubPullRequestReviewAction.Approve ? "APPROVE" : "REQUEST_CHANGES";
        var response = await SendAsync(
            repositoryId, HttpMethod.Post, $"repos/{owner}/{repo}/pulls/{number}/reviews", null, cancellationToken,
            jsonBody: new CreatePullRequestReviewRequest(body, reviewEvent),
            notFoundError: GitHubError.PullRequestNotFound,
            unprocessableError: GitHubError.PullRequestReviewInvalid);
        if (!response.IsSuccess) return GitHubResult<GitHubPullRequestReview>.FailureFrom(response);
        var review = await ReadAsync<PullRequestReviewResponse>(response.Value!, cancellationToken);
        return review is not null && IsValidReview(review)
            ? GitHubResult<GitHubPullRequestReview>.Success(ToPullRequestReview(review))
            : GitHubResult<GitHubPullRequestReview>.Failure(GitHubError.InvalidResponse);
    }

    private sealed record PullRequestResponse(
        int Number,
        string? Title,
        string? Body,
        string? State,
        bool? Merged,
        PullRequestRef? Head,
        [property: JsonPropertyName("base")] PullRequestRef? Base,
        UserRef? User,
        [property: JsonPropertyName("merge_commit_sha")] string? MergeCommitSha,
        bool? Mergeable,
        [property: JsonPropertyName("mergeable_state")] string? MergeableState,
        [property: JsonPropertyName("created_at")] DateTimeOffset CreatedAt,
        [property: JsonPropertyName("updated_at")] DateTimeOffset UpdatedAt,
        [property: JsonPropertyName("html_url")] string? HtmlUrl,
        [property: JsonPropertyName("changed_files")] int? ChangedFiles,
        int? Additions,
        int? Deletions,
        bool? Draft);

    private sealed record PullRequestRef(string? Ref, string? Sha, RepositoryRef? Repo);
    private sealed record RepositoryRef([property: JsonPropertyName("full_name")] string? FullName);

    private sealed record CreatePullRequestRequest(string Title, string? Body, string Head, [property: JsonPropertyName("base")] string BaseBranch, bool Draft);

    private sealed record MergePullRequestRequest(
        [property: JsonPropertyName("commit_message")] string? CommitMessage,
        [property: JsonPropertyName("merge_method")] string? MergeMethod,
        string? Sha);

    private sealed record MergePullRequestResponse(bool? Merged, string? Sha);

    private sealed record UpdatePullRequestRequest(string State);

    private sealed record CreatePullRequestReviewRequest(
        string? Body,
        [property: JsonPropertyName("event")] string EventName);

    private sealed record PullRequestReviewResponse(
        long Id,
        string? Body,
        UserRef? User,
        string? State,
        [property: JsonPropertyName("submitted_at")] DateTimeOffset? SubmittedAt,
        [property: JsonPropertyName("commit_id")] string? CommitId,
        [property: JsonPropertyName("html_url")] string? HtmlUrl);

    private static bool IsValidPullRequest(PullRequestResponse pr) =>
        pr.Number > 0
        && !string.IsNullOrEmpty(pr.Title)
        && !string.IsNullOrEmpty(pr.State)
        && !string.IsNullOrEmpty(pr.Head?.Ref)
        && !string.IsNullOrEmpty(pr.Base?.Ref)
        && !string.IsNullOrEmpty(pr.User?.Login)
        && !string.IsNullOrEmpty(pr.HtmlUrl);

    private static GitHubPullRequestInfo ToPullRequestInfo(PullRequestResponse pr) => new(
        pr.Number,
        pr.Title!,
        pr.Body,
        pr.Merged == true ? GitHubPullRequestState.Merged : string.Equals(pr.State, "open", StringComparison.Ordinal) ? GitHubPullRequestState.Open : GitHubPullRequestState.Closed,
        pr.Head!.Ref!,
        pr.Base!.Ref!,
        pr.User!.Login!,
        pr.MergeCommitSha,
        pr.Mergeable,
        pr.CreatedAt,
        pr.UpdatedAt,
        pr.HtmlUrl!,
        ClassifyMergeability(pr.Mergeable, pr.MergeableState),
        IsRetryableMergeability(pr.Mergeable, pr.MergeableState) ? 2 : null,
        pr.Head!.Sha,
        pr.Draft,
        pr.Head.Repo?.FullName, pr.Base.Repo?.FullName);


    private static string ClassifyMergeability(bool? mergeable, string? state)
    {
        if (mergeable is null || string.Equals(state, "unknown", StringComparison.OrdinalIgnoreCase))
            return GitHubMergeabilityStatus.CalculatingRetryable;
        if (string.Equals(state, "dirty", StringComparison.OrdinalIgnoreCase))
            return GitHubMergeabilityStatus.Conflicting;
        if (state is not null && (state.Equals("blocked", StringComparison.OrdinalIgnoreCase)
            || state.Equals("behind", StringComparison.OrdinalIgnoreCase)
            || state.Equals("unstable", StringComparison.OrdinalIgnoreCase)
            || state.Equals("draft", StringComparison.OrdinalIgnoreCase)))
            return GitHubMergeabilityStatus.Blocked;
        if (mergeable == true)
            return GitHubMergeabilityStatus.Mergeable;
        return GitHubMergeabilityStatus.UnknownRetryable;
    }

    private static bool IsRetryableMergeability(bool? mergeable, string? state) =>
        ClassifyMergeability(mergeable, state) is
            GitHubMergeabilityStatus.CalculatingRetryable or GitHubMergeabilityStatus.UnknownRetryable;

    private static string MapStateQuery(GitHubPullRequestState state) => state switch
    {
        GitHubPullRequestState.Open => "open",
        GitHubPullRequestState.Closed or GitHubPullRequestState.Merged => "closed",
        _ => "all",
    };

    private static string MapMergeMethod(GitHubMergeMethod method) => method switch
    {
        GitHubMergeMethod.Squash => "squash",
        GitHubMergeMethod.Rebase => "rebase",
        _ => "merge",
    };

}
