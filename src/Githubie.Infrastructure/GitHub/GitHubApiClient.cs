using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;
using Githubie.Application.Credentials;
using Githubie.Application.GitHub;

namespace Githubie.Infrastructure.GitHub;

/// <summary>
/// GitHub REST API (`https://api.github.com/`) を呼び出す<see cref="IGitHubApiClient"/>実装です。
/// 認証はFine-grained Personal Access TokenによるBearer方式、APIバージョンは`X-GitHub-Api-Version`で固定します。
/// Tokenは<see cref="IApiTokenStore"/>から都度読み出し、使用後にメモリ上から消去します。
/// </summary>
public sealed class GitHubApiClient(HttpClient httpClient, IApiTokenStore tokenStore) : IGitHubApiClient
{
    private const string ApiVersion = "2022-11-28";
    private const int PageSize = 100;
    private const int MaxPages = 100;

    // GitHubのPUT/POST bodyでは省略可能フィールドへ明示nullを送ると422を返す場合があるため、
    // 未設定プロパティはJSONへ含めない(例: merge_method未指定時のPRマージ)。
    private static readonly JsonSerializerOptions RequestSerializerOptions = new()
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
    };

    private readonly HttpClient _httpClient = httpClient;
    private readonly IApiTokenStore _tokenStore = tokenStore;

    public async Task<GitHubResult<GitHubRepositoryInfo>> GetRepositoryAsync(string repositoryId, string owner, string repo, CancellationToken cancellationToken)
    {
        var response = await SendAsync(repositoryId, HttpMethod.Get, $"repos/{owner}/{repo}", null, cancellationToken);
        if (!response.IsSuccess)
        {
            return GitHubResult<GitHubRepositoryInfo>.Failure(response.Error!.Value);
        }

        var body = await ReadAsync<RepositoryResponse>(response.Value!, cancellationToken);
        if (body is null || string.IsNullOrEmpty(body.DefaultBranch))
        {
            return GitHubResult<GitHubRepositoryInfo>.Failure(GitHubError.InvalidResponse);
        }

        return GitHubResult<GitHubRepositoryInfo>.Success(new GitHubRepositoryInfo(owner, repo, body.DefaultBranch, body.Description));
    }

    public async Task<GitHubResult<GitHubRepositoryInfo>> UpdateRepositoryDescriptionAsync(
        string repositoryId, string owner, string repo, string description, CancellationToken cancellationToken)
    {
        var response = await SendAsync(
            repositoryId, HttpMethod.Patch, $"repos/{owner}/{repo}", null, cancellationToken,
            jsonBody: new RepositoryDescriptionUpdate(description),
            unprocessableError: GitHubError.RepositoryDescriptionInvalid);
        if (!response.IsSuccess)
        {
            var error = response.Error == GitHubError.PermissionDenied ? GitHubError.TokenScopeMissing : response.Error!.Value;
            return GitHubResult<GitHubRepositoryInfo>.Failure(error);
        }

        var body = await ReadAsync<RepositoryResponse>(response.Value!, cancellationToken);
        if (body is null || string.IsNullOrEmpty(body.DefaultBranch))
            return GitHubResult<GitHubRepositoryInfo>.Failure(GitHubError.InvalidResponse);

        return GitHubResult<GitHubRepositoryInfo>.Success(new GitHubRepositoryInfo(owner, repo, body.DefaultBranch, body.Description));
    }

    public async Task<GitHubResult<bool>> DispatchWorkflowAsync(
        string repositoryId, string owner, string repo, GitHubWorkflowDispatchRequest request, CancellationToken cancellationToken)
    {
        var response = await SendAsync(
            repositoryId, HttpMethod.Post,
            $"repos/{owner}/{repo}/actions/workflows/{Uri.EscapeDataString(request.Workflow)}/dispatches",
            null, cancellationToken, jsonBody: new WorkflowDispatchBody(request.Ref, request.Inputs),
            notFoundError: GitHubError.WorkflowNotAllowed,
            unprocessableError: GitHubError.WorkflowInputInvalid);
        if (!response.IsSuccess) return GitHubResult<bool>.Failure(response.Error!.Value);
        response.Value!.Dispose();
        return GitHubResult<bool>.Success(true);
    }

    public async Task<GitHubResult<GitHubWorkflowRunInfo>> GetWorkflowRunAsync(
        string repositoryId, string owner, string repo, long runId, CancellationToken cancellationToken)
    {
        if (runId <= 0) return GitHubResult<GitHubWorkflowRunInfo>.Failure(GitHubError.WorkflowRunNotFound);
        var response = await SendAsync(
            repositoryId, HttpMethod.Get, $"repos/{owner}/{repo}/actions/runs/{runId}", null, cancellationToken,
            notFoundError: GitHubError.WorkflowRunNotFound);
        if (!response.IsSuccess) return GitHubResult<GitHubWorkflowRunInfo>.Failure(response.Error!.Value);
        var body = await ReadAsync<WorkflowRunResponse>(response.Value!, cancellationToken);
        return body is null || !IsValidWorkflowRun(body)
            ? GitHubResult<GitHubWorkflowRunInfo>.Failure(GitHubError.InvalidResponse)
            : GitHubResult<GitHubWorkflowRunInfo>.Success(ToWorkflowRunInfo(body));
    }

    public async Task<GitHubResult<IReadOnlyList<GitHubWorkflowRunInfo>>> ListWorkflowRunsAsync(
        string repositoryId, string owner, string repo, string? workflow, string? branch,
        string? eventName, string? status, int limit, CancellationToken cancellationToken)
    {
        var path = workflow is null
            ? $"repos/{owner}/{repo}/actions/runs"
            : $"repos/{owner}/{repo}/actions/workflows/{Uri.EscapeDataString(workflow)}/runs";
        var query = new List<string> { $"per_page={Math.Clamp(limit, 1, 100)}" };
        if (branch is not null) query.Add($"branch={Uri.EscapeDataString(branch)}");
        if (eventName is not null) query.Add($"event={Uri.EscapeDataString(eventName)}");
        if (status is not null) query.Add($"status={Uri.EscapeDataString(status)}");
        var response = await SendAsync(repositoryId, HttpMethod.Get, $"{path}?{string.Join('&', query)}", null, cancellationToken,
            notFoundError: GitHubError.WorkflowNotAllowed);
        if (!response.IsSuccess) return GitHubResult<IReadOnlyList<GitHubWorkflowRunInfo>>.Failure(response.Error!.Value);
        var body = await ReadAsync<WorkflowRunsResponse>(response.Value!, cancellationToken);
        if (body?.WorkflowRuns is null) return GitHubResult<IReadOnlyList<GitHubWorkflowRunInfo>>.Failure(GitHubError.InvalidResponse);
        var runs = body.WorkflowRuns.Where(IsValidWorkflowRun).Take(limit).Select(ToWorkflowRunInfo).ToArray();
        return GitHubResult<IReadOnlyList<GitHubWorkflowRunInfo>>.Success(runs);
    }

    public async Task<GitHubResult<IReadOnlyList<GitHubBranchInfo>>> ListBranchesAsync(string repositoryId, string owner, string repo, CancellationToken cancellationToken)
    {
        var page = await GetAllPagesAsync<BranchResponse>(repositoryId, $"repos/{owner}/{repo}/branches", cancellationToken);
        if (!page.IsSuccess)
        {
            return GitHubResult<IReadOnlyList<GitHubBranchInfo>>.Failure(page.Error!.Value);
        }

        var items = page.Value!
            .Where(IsValidBranch)
            .Select(b => new GitHubBranchInfo(b.Name!, b.Commit!.Sha!, b.Protected))
            .ToArray();

        return GitHubResult<IReadOnlyList<GitHubBranchInfo>>.Success(items);
    }

    public async Task<GitHubResult<GitHubBranchInfo>> GetBranchAsync(string repositoryId, string owner, string repo, string branch, CancellationToken cancellationToken)
    {
        var response = await SendAsync(
            repositoryId, HttpMethod.Get, $"repos/{owner}/{repo}/branches/{Uri.EscapeDataString(branch)}", null, cancellationToken,
            notFoundError: GitHubError.BranchNotFound);
        if (!response.IsSuccess)
        {
            return GitHubResult<GitHubBranchInfo>.Failure(response.Error!.Value);
        }

        var body = await ReadAsync<BranchResponse>(response.Value!, cancellationToken);
        if (body is null || !IsValidBranch(body))
        {
            return GitHubResult<GitHubBranchInfo>.Failure(GitHubError.InvalidResponse);
        }

        return GitHubResult<GitHubBranchInfo>.Success(new GitHubBranchInfo(body.Name!, body.Commit!.Sha!, body.Protected));
    }

    public async Task<GitHubResult<GitHubBranchInfo>> CreateBranchAsync(
        string repositoryId, string owner, string repo, string branch, string sourceSha, CancellationToken cancellationToken)
    {
        var payload = new CreateRefRequest($"refs/heads/{branch}", sourceSha);
        var response = await SendAsync(
            repositoryId, HttpMethod.Post, $"repos/{owner}/{repo}/git/refs", null, cancellationToken,
            jsonBody: payload, unprocessableError: GitHubError.BranchAlreadyExists);
        if (!response.IsSuccess) return GitHubResult<GitHubBranchInfo>.Failure(response.Error!.Value);

        var body = await ReadAsync<GitRefResponse>(response.Value!, cancellationToken);
        return string.IsNullOrWhiteSpace(body?.Object?.Sha)
            ? GitHubResult<GitHubBranchInfo>.Failure(GitHubError.InvalidResponse)
            : GitHubResult<GitHubBranchInfo>.Success(new(branch, body.Object.Sha, false));
    }

    public async Task<GitHubResult<bool>> DeleteBranchAsync(
        string repositoryId, string owner, string repo, string branch, CancellationToken cancellationToken)
    {
        var response = await SendAsync(
            repositoryId, HttpMethod.Delete, $"repos/{owner}/{repo}/git/refs/heads/{Uri.EscapeDataString(branch)}", null,
            cancellationToken, notFoundError: GitHubError.BranchNotFound);
        if (!response.IsSuccess) return GitHubResult<bool>.Failure(response.Error!.Value);
        response.Value!.Dispose();
        return GitHubResult<bool>.Success(true);
    }

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
            return GitHubResult<IReadOnlyList<GitHubPullRequestInfo>>.Failure(page.Error!.Value);
        }

        var items = page.Value!.Where(IsValidPullRequest).Select(ToPullRequestInfo).ToArray();
        return GitHubResult<IReadOnlyList<GitHubPullRequestInfo>>.Success(items);
    }

    public async Task<GitHubResult<GitHubPullRequestInfo>> GetPullRequestAsync(string repositoryId, string owner, string repo, int number, CancellationToken cancellationToken)
    {
        var response = await SendAsync(repositoryId, HttpMethod.Get, $"repos/{owner}/{repo}/pulls/{number}", null, cancellationToken, notFoundError: GitHubError.PullRequestNotFound);
        if (!response.IsSuccess)
        {
            return GitHubResult<GitHubPullRequestInfo>.Failure(response.Error!.Value);
        }

        var body = await ReadAsync<PullRequestResponse>(response.Value!, cancellationToken);
        if (body is null || !IsValidPullRequest(body))
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
            return GitHubResult<GitHubPullRequestDiff>.Failure(stats.Error!.Value);
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
            return GitHubResult<GitHubPullRequestDiff>.Failure(diffResponse.Error!.Value);
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
            return GitHubResult<GitHubPullRequestInfo>.Failure(response.Error!.Value);
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
        var payload = new MergePullRequestRequest(request.CommitMessage, request.MergeMethod is null ? null : MapMergeMethod(request.MergeMethod.Value));
        var response = await SendAsync(
            repositoryId, HttpMethod.Put, $"repos/{owner}/{repo}/pulls/{request.Number}/merge", null, cancellationToken,
            jsonBody: payload, notFoundError: GitHubError.PullRequestNotFound, conflictError: GitHubError.PullRequestNotMergeable);
        if (!response.IsSuccess)
        {
            return GitHubResult<GitHubPullRequestInfo>.Failure(response.Error!.Value);
        }

        return await GetPullRequestAsync(repositoryId, owner, repo, request.Number, cancellationToken);
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
        if (!response.IsSuccess) return GitHubResult<GitHubPullRequestInfo>.Failure(response.Error!.Value);
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
        if (!page.IsSuccess) return GitHubResult<IReadOnlyList<GitHubPullRequestComment>>.Failure(page.Error!.Value);
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
        if (!response.IsSuccess) return GitHubResult<GitHubPullRequestComment>.Failure(response.Error!.Value);
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
        if (!response.IsSuccess) return GitHubResult<GitHubPullRequestReview>.Failure(response.Error!.Value);
        var review = await ReadAsync<PullRequestReviewResponse>(response.Value!, cancellationToken);
        return review is not null && IsValidReview(review)
            ? GitHubResult<GitHubPullRequestReview>.Success(ToPullRequestReview(review))
            : GitHubResult<GitHubPullRequestReview>.Failure(GitHubError.InvalidResponse);
    }

    public async Task<GitHubResult<IReadOnlyList<GitHubTagInfo>>> ListTagsAsync(string repositoryId, string owner, string repo, CancellationToken cancellationToken)
    {
        var page = await GetAllPagesAsync<TagResponse>(repositoryId, $"repos/{owner}/{repo}/tags", cancellationToken);
        if (!page.IsSuccess)
        {
            return GitHubResult<IReadOnlyList<GitHubTagInfo>>.Failure(page.Error!.Value);
        }

        var items = page.Value!
            .Where(t => !string.IsNullOrEmpty(t.Name) && t.Commit?.Sha is not null)
            .Select(t => new GitHubTagInfo(t.Name!, t.Commit!.Sha!, null, null, null))
            .ToArray();

        return GitHubResult<IReadOnlyList<GitHubTagInfo>>.Success(items);
    }

    public async Task<GitHubResult<GitHubTagInfo>> GetTagAsync(string repositoryId, string owner, string repo, string tag, CancellationToken cancellationToken)
    {
        var refResponse = await SendAsync(
            repositoryId, HttpMethod.Get, $"repos/{owner}/{repo}/git/refs/tags/{Uri.EscapeDataString(tag)}", null, cancellationToken, notFoundError: GitHubError.TagNotFound);
        if (!refResponse.IsSuccess)
        {
            return GitHubResult<GitHubTagInfo>.Failure(refResponse.Error!.Value);
        }

        var refBody = await ReadAsync<GitRefResponse>(refResponse.Value!, cancellationToken);
        if (refBody?.Object?.Sha is null)
        {
            return GitHubResult<GitHubTagInfo>.Failure(GitHubError.InvalidResponse);
        }

        if (!string.Equals(refBody.Object.Type, "tag", StringComparison.Ordinal))
        {
            return GitHubResult<GitHubTagInfo>.Success(new GitHubTagInfo(tag, refBody.Object.Sha, null, null, null));
        }

        var tagObjectResponse = await SendAsync(repositoryId, HttpMethod.Get, $"repos/{owner}/{repo}/git/tags/{refBody.Object.Sha}", null, cancellationToken);
        if (!tagObjectResponse.IsSuccess)
        {
            return GitHubResult<GitHubTagInfo>.Failure(tagObjectResponse.Error!.Value);
        }

        var tagObjectBody = await ReadAsync<GitTagObjectResponse>(tagObjectResponse.Value!, cancellationToken);
        if (tagObjectBody?.Object?.Sha is null)
        {
            return GitHubResult<GitHubTagInfo>.Failure(GitHubError.InvalidResponse);
        }

        return GitHubResult<GitHubTagInfo>.Success(new GitHubTagInfo(
            tag, tagObjectBody.Object.Sha, tagObjectBody.Message, tagObjectBody.Tagger?.Name, tagObjectBody.Tagger?.Date));
    }

    public async Task<GitHubResult<GitHubTagInfo>> CreateTagAsync(string repositoryId, string owner, string repo, GitHubTagCreate request, CancellationToken cancellationToken)
    {
        var tagObjectPayload = new CreateTagObjectRequest(request.Tag, request.Message ?? request.Tag, request.TargetCommitSha, "commit");
        var tagObjectResponse = await SendAsync(repositoryId, HttpMethod.Post, $"repos/{owner}/{repo}/git/tags", null, cancellationToken, jsonBody: tagObjectPayload);
        if (!tagObjectResponse.IsSuccess)
        {
            return GitHubResult<GitHubTagInfo>.Failure(tagObjectResponse.Error!.Value);
        }

        var tagObjectBody = await ReadAsync<CreateTagObjectResponse>(tagObjectResponse.Value!, cancellationToken);
        if (string.IsNullOrEmpty(tagObjectBody?.Sha))
        {
            return GitHubResult<GitHubTagInfo>.Failure(GitHubError.InvalidResponse);
        }

        var refPayload = new CreateRefRequest($"refs/tags/{request.Tag}", tagObjectBody.Sha);
        var refResponse = await SendAsync(
            repositoryId, HttpMethod.Post, $"repos/{owner}/{repo}/git/refs", null, cancellationToken,
            jsonBody: refPayload, unprocessableError: GitHubError.TagAlreadyExists);
        if (!refResponse.IsSuccess)
        {
            return GitHubResult<GitHubTagInfo>.Failure(refResponse.Error!.Value);
        }

        return GitHubResult<GitHubTagInfo>.Success(new GitHubTagInfo(
            request.Tag, request.TargetCommitSha, request.Message, null, null));
    }

    public async Task<GitHubResult<bool>> DeleteTagAsync(string repositoryId, string owner, string repo, string tag, CancellationToken cancellationToken)
    {
        var response = await SendAsync(
            repositoryId, HttpMethod.Delete, $"repos/{owner}/{repo}/git/refs/tags/{Uri.EscapeDataString(tag)}", null,
            cancellationToken, notFoundError: GitHubError.TagNotFound);
        if (!response.IsSuccess) return GitHubResult<bool>.Failure(response.Error!.Value);
        response.Value!.Dispose();
        return GitHubResult<bool>.Success(true);
    }

    public async Task<GitHubResult<IReadOnlyList<GitHubReleaseInfo>>> ListReleasesAsync(
        string repositoryId, string owner, string repo, CancellationToken cancellationToken)
    {
        var page = await GetAllPagesAsync<ReleaseResponse>(repositoryId, $"repos/{owner}/{repo}/releases", cancellationToken);
        if (!page.IsSuccess) return GitHubResult<IReadOnlyList<GitHubReleaseInfo>>.Failure(page.Error!.Value);
        var releases = page.Value!;
        if (releases.Any(release => !IsValidRelease(release)))
            return GitHubResult<IReadOnlyList<GitHubReleaseInfo>>.Failure(GitHubError.InvalidResponse);
        return GitHubResult<IReadOnlyList<GitHubReleaseInfo>>.Success(releases.Select(ToReleaseInfo).ToArray());
    }

    public async Task<GitHubResult<GitHubReleaseInfo>> GetReleaseAsync(
        string repositoryId, string owner, string repo, string tag, CancellationToken cancellationToken)
    {
        var response = await SendAsync(
            repositoryId, HttpMethod.Get, $"repos/{owner}/{repo}/releases/tags/{Uri.EscapeDataString(tag)}", null,
            cancellationToken, notFoundError: GitHubError.ReleaseNotFound);
        if (!response.IsSuccess) return GitHubResult<GitHubReleaseInfo>.Failure(response.Error!.Value);
        var release = await ReadAsync<ReleaseResponse>(response.Value!, cancellationToken);
        return IsValidRelease(release)
            ? GitHubResult<GitHubReleaseInfo>.Success(ToReleaseInfo(release!))
            : GitHubResult<GitHubReleaseInfo>.Failure(GitHubError.InvalidResponse);
    }

    public async Task<GitHubResult<GitHubReleaseInfo>> UpdateReleaseAsync(
        string repositoryId, string owner, string repo, long releaseId, GitHubReleaseUpdate request, CancellationToken cancellationToken)
    {
        if (releaseId <= 0) return GitHubResult<GitHubReleaseInfo>.Failure(GitHubError.ReleaseNotFound);
        var response = await SendAsync(
            repositoryId, HttpMethod.Patch, $"repos/{owner}/{repo}/releases/{releaseId}", null, cancellationToken,
            jsonBody: request, notFoundError: GitHubError.ReleaseNotFound);
        if (!response.IsSuccess) return GitHubResult<GitHubReleaseInfo>.Failure(response.Error!.Value);
        var release = await ReadAsync<ReleaseResponse>(response.Value!, cancellationToken);
        return IsValidRelease(release)
            ? GitHubResult<GitHubReleaseInfo>.Success(ToReleaseInfo(release!))
            : GitHubResult<GitHubReleaseInfo>.Failure(GitHubError.InvalidResponse);
    }

    public async Task<GitHubResult<GitHubReleaseInfo>> UploadReleaseAssetsAsync(
        string repositoryId, string owner, string repo, string localRoot, GitHubReleaseAssetUpload request, CancellationToken cancellationToken)
    {
        var assets = ValidateReleaseAssets(localRoot, request.Assets);
        if (assets.Error is not null) return GitHubResult<GitHubReleaseInfo>.Failure(assets.Error.Value);
        var currentResponse = await SendAsync(
            repositoryId, HttpMethod.Get, $"repos/{owner}/{repo}/releases/{request.ReleaseId}", null, cancellationToken,
            notFoundError: GitHubError.ReleaseNotFound);
        if (!currentResponse.IsSuccess) return GitHubResult<GitHubReleaseInfo>.Failure(currentResponse.Error!.Value);
        var release = await ReadAsync<ReleaseResponse>(currentResponse.Value!, cancellationToken);
        if (!IsValidRelease(release)) return GitHubResult<GitHubReleaseInfo>.Failure(GitHubError.InvalidResponse);

        foreach (var path in assets.Paths!)
        {
            var existing = release!.Assets?.FirstOrDefault(asset =>
                string.Equals(asset.Name, Path.GetFileName(path), StringComparison.OrdinalIgnoreCase));
            if (existing is not null && !request.ReplaceExisting)
                return GitHubResult<GitHubReleaseInfo>.Failure(GitHubError.ReleaseAssetAlreadyExists);
            if (existing is not null)
            {
                var deleted = await SendAsync(
                    repositoryId, HttpMethod.Delete, $"repos/{owner}/{repo}/releases/assets/{existing.Id}", null,
                    cancellationToken, notFoundError: GitHubError.ReleaseAssetNotFound);
                if (!deleted.IsSuccess) return GitHubResult<GitHubReleaseInfo>.Failure(deleted.Error!.Value);
                deleted.Value!.Dispose();
            }
            var upload = await UploadReleaseAssetAsync(repositoryId, owner, repo, release.UploadUrl!, path, cancellationToken);
            if (!upload.IsSuccess) return GitHubResult<GitHubReleaseInfo>.Failure(upload.Error!.Value);
        }

        return await GetReleaseAsync(repositoryId, owner, repo, release!.TagName!, cancellationToken);
    }

    public async Task<GitHubResult<GitHubReleaseInfo>> CreateReleaseAsync(
        string repositoryId,
        string owner,
        string repo,
        string localRoot,
        GitHubReleaseCreate request,
        CancellationToken cancellationToken)
    {
        var assets = ValidateReleaseAssets(localRoot, request.Assets);
        if (assets.Error is not null) return GitHubResult<GitHubReleaseInfo>.Failure(assets.Error.Value);

        ReleaseResponse? release = null;
        var existingResponse = await SendAsync(
            repositoryId, HttpMethod.Get, $"repos/{owner}/{repo}/releases/tags/{Uri.EscapeDataString(request.Tag)}", null,
            cancellationToken, notFoundError: GitHubError.ReleaseNotFound);
        if (existingResponse.IsSuccess)
        {
            release = await ReadAsync<ReleaseResponse>(existingResponse.Value!, cancellationToken);
            if (!IsValidRelease(release)) return GitHubResult<GitHubReleaseInfo>.Failure(GitHubError.InvalidResponse);
            if (!release!.Draft || !string.Equals(release.Name, request.Name, StringComparison.Ordinal) ||
                release.Prerelease != request.Prerelease)
                return GitHubResult<GitHubReleaseInfo>.Failure(GitHubError.ReleaseAlreadyExists);
        }
        else if (existingResponse.Error != GitHubError.ReleaseNotFound)
        {
            return GitHubResult<GitHubReleaseInfo>.Failure(existingResponse.Error!.Value);
        }

        if (release is null)
        {
            var createPayload = new CreateReleaseRequest(request.Tag, request.Name, request.Body, true, request.Prerelease);
            var created = await SendAsync(
                repositoryId, HttpMethod.Post, $"repos/{owner}/{repo}/releases", null, cancellationToken,
                jsonBody: createPayload, unprocessableError: GitHubError.ReleaseAlreadyExists);
            if (!created.IsSuccess) return GitHubResult<GitHubReleaseInfo>.Failure(created.Error!.Value);
            release = await ReadAsync<ReleaseResponse>(created.Value!, cancellationToken);
            if (!IsValidRelease(release)) return GitHubResult<GitHubReleaseInfo>.Failure(GitHubError.InvalidResponse);
        }

        var uploadedAssets = release!.Assets?.Select(asset =>
            new GitHubReleaseAssetInfo(asset.Name!, asset.Size, asset.BrowserDownloadUrl!, asset.Id)).ToList() ?? [];
        foreach (var path in assets.Paths!)
        {
            if (uploadedAssets.Any(asset => string.Equals(asset.Name, Path.GetFileName(path), StringComparison.OrdinalIgnoreCase)))
                continue;
            var upload = await UploadReleaseAssetAsync(repositoryId, owner, repo, release!.UploadUrl!, path, cancellationToken);
            if (!upload.IsSuccess) return GitHubResult<GitHubReleaseInfo>.Failure(upload.Error!.Value);
            uploadedAssets.Add(upload.Value!);
        }

        if (!request.Draft)
        {
            var publish = await SendAsync(
                repositoryId, HttpMethod.Patch, $"repos/{owner}/{repo}/releases/{release!.Id}", null, cancellationToken,
                jsonBody: new PublishReleaseRequest(false));
            if (!publish.IsSuccess) return GitHubResult<GitHubReleaseInfo>.Failure(publish.Error!.Value);
            var published = await ReadAsync<ReleaseResponse>(publish.Value!, cancellationToken);
            if (!IsValidRelease(published)) return GitHubResult<GitHubReleaseInfo>.Failure(GitHubError.InvalidResponse);
            release = published;
        }

        return GitHubResult<GitHubReleaseInfo>.Success(new(
            release!.Id, release.TagName!, release.Name!, release.Draft, release.Prerelease, release.HtmlUrl!, uploadedAssets));
    }

    private async Task<GitHubResult<GitHubReleaseAssetInfo>> UploadReleaseAssetAsync(
        string repositoryId, string owner, string repo, string uploadTemplate, string path, CancellationToken cancellationToken)
    {
        var templateIndex = uploadTemplate.IndexOf('{', StringComparison.Ordinal);
        var baseUrl = templateIndex >= 0 ? uploadTemplate[..templateIndex] : uploadTemplate;
        if (!Uri.TryCreate(baseUrl, UriKind.Absolute, out var uploadUri) ||
            uploadUri.Scheme != Uri.UriSchemeHttps || uploadUri.Host != "uploads.github.com" ||
            !uploadUri.AbsolutePath.StartsWith($"/repos/{owner}/{repo}/releases/", StringComparison.Ordinal))
            return GitHubResult<GitHubReleaseAssetInfo>.Failure(GitHubError.InvalidResponse);

        var name = Path.GetFileName(path);
        var builder = new UriBuilder(uploadUri) { Query = $"name={Uri.EscapeDataString(name)}" };
        await using var stream = File.OpenRead(path);
        using var content = new StreamContent(stream);
        content.Headers.ContentType = new MediaTypeHeaderValue(GetAssetContentType(name));
        var response = await SendAsync(
            repositoryId, HttpMethod.Post, builder.Uri.ToString(), null, cancellationToken,
            rawContent: content, unprocessableError: GitHubError.ReleaseUploadFailed);
        if (!response.IsSuccess) return GitHubResult<GitHubReleaseAssetInfo>.Failure(response.Error!.Value);
        var body = await ReadAsync<ReleaseAssetResponse>(response.Value!, cancellationToken);
        return body is null || string.IsNullOrWhiteSpace(body.Name) || string.IsNullOrWhiteSpace(body.BrowserDownloadUrl)
            ? GitHubResult<GitHubReleaseAssetInfo>.Failure(GitHubError.InvalidResponse)
            : GitHubResult<GitHubReleaseAssetInfo>.Success(new(body.Name, body.Size, body.BrowserDownloadUrl, body.Id));
    }

    private static (IReadOnlyList<string>? Paths, GitHubError? Error) ValidateReleaseAssets(
        string localRoot, IReadOnlyList<string> requested)
    {
        if (requested.Count is < 1 or > 10) return (null, GitHubError.ReleaseAssetInvalid);
        var root = Path.GetFullPath(localRoot).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        var paths = new List<string>(requested.Count);
        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var requestedPath in requested)
        {
            string path;
            try { path = Path.GetFullPath(requestedPath); }
            catch (Exception exception) when (exception is ArgumentException or NotSupportedException or PathTooLongException)
            { return (null, GitHubError.ReleaseAssetInvalid); }
            var extension = Path.GetExtension(path).ToLowerInvariant();
            var fileName = Path.GetFileName(path);
            var allowed = extension is ".msi" or ".zip" or ".sha256" or ".ps1" ||
                string.Equals(fileName, "SHA256SUMS.txt", StringComparison.OrdinalIgnoreCase);
            if (!path.StartsWith(root, StringComparison.OrdinalIgnoreCase) || !allowed || !names.Add(fileName))
                return (null, GitHubError.ReleaseAssetInvalid);
            if (!File.Exists(path)) return (null, GitHubError.ReleaseAssetNotFound);
            paths.Add(path);
        }
        return (paths, null);
    }

    private static string GetAssetContentType(string name) => Path.GetExtension(name).ToLowerInvariant() switch
    {
        ".msi" => "application/x-msi",
        ".zip" => "application/zip",
        _ => "text/plain",
    };

    private static bool IsValidRelease(ReleaseResponse? release) => release is not null && release.Id > 0 &&
        !string.IsNullOrWhiteSpace(release.TagName) && !string.IsNullOrWhiteSpace(release.Name) &&
        !string.IsNullOrWhiteSpace(release.UploadUrl) && !string.IsNullOrWhiteSpace(release.HtmlUrl);

    private static GitHubReleaseInfo ToReleaseInfo(ReleaseResponse release) => new(
        release.Id, release.TagName!, release.Name!, release.Draft, release.Prerelease, release.HtmlUrl!,
        release.Assets?.Where(asset => asset.Id > 0 && !string.IsNullOrWhiteSpace(asset.Name) &&
            !string.IsNullOrWhiteSpace(asset.BrowserDownloadUrl))
            .Select(asset => new GitHubReleaseAssetInfo(asset.Name!, asset.Size, asset.BrowserDownloadUrl!, asset.Id)).ToArray() ?? []);

    private async Task<GitHubResult<IReadOnlyList<T>>> GetAllPagesAsync<T>(string repositoryId, string firstPagePath, CancellationToken cancellationToken)
    {
        var results = new List<T>();
        var path = firstPagePath.Contains('?') ? $"{firstPagePath}&per_page={PageSize}" : $"{firstPagePath}?per_page={PageSize}";

        for (var pageCount = 0; pageCount < MaxPages; pageCount++)
        {
            var response = await SendAsync(repositoryId, HttpMethod.Get, path, null, cancellationToken);
            if (!response.IsSuccess)
            {
                return GitHubResult<IReadOnlyList<T>>.Failure(response.Error!.Value);
            }

            var pageItems = await ReadAsync<T[]>(response.Value!, cancellationToken);
            if (pageItems is null)
            {
                return GitHubResult<IReadOnlyList<T>>.Failure(GitHubError.InvalidResponse);
            }

            results.AddRange(pageItems);

            var next = GetNextLink(response.Value!);
            if (next is null)
            {
                break;
            }

            path = next;
        }

        return GitHubResult<IReadOnlyList<T>>.Success(results);
    }

    private static string? GetNextLink(HttpResponseMessage response)
    {
        if (!response.Headers.TryGetValues("Link", out var values))
        {
            return null;
        }

        var linkHeader = values.FirstOrDefault();
        if (string.IsNullOrEmpty(linkHeader))
        {
            return null;
        }

        foreach (var part in linkHeader.Split(','))
        {
            var segments = part.Split(';');
            if (segments.Length < 2)
            {
                continue;
            }

            var relSegment = segments[1].Trim();
            if (!relSegment.Equals("rel=\"next\"", StringComparison.Ordinal))
            {
                continue;
            }

            var urlSegment = segments[0].Trim().Trim('<', '>');
            var uri = new Uri(urlSegment);
            return uri.PathAndQuery.TrimStart('/');
        }

        return null;
    }

    private async Task<GitHubResult<HttpResponseMessage>> SendAsync(
        string repositoryId,
        HttpMethod method,
        string relativePath,
        string? acceptOverride,
        CancellationToken cancellationToken,
        object? jsonBody = null,
        HttpContent? rawContent = null,
        GitHubError notFoundError = GitHubError.RepositoryNotFound,
        GitHubError conflictError = GitHubError.ApiError,
        GitHubError unprocessableError = GitHubError.ApiError)
    {
        var tokenRead = _tokenStore.Read(repositoryId);
        if (!tokenRead.IsSuccess || tokenRead.Token is null)
        {
            return GitHubResult<HttpResponseMessage>.Failure(GitHubError.AuthenticationFailed);
        }

        using var request = new HttpRequestMessage(method, relativePath);
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue(acceptOverride ?? "application/vnd.github+json"));
        request.Headers.TryAddWithoutValidation("X-GitHub-Api-Version", ApiVersion);
        request.Headers.UserAgent.Add(new ProductInfoHeaderValue("Githubie", "0.1"));

        var tokenChars = tokenRead.Token;
        try
        {
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", new string(tokenChars));

            if (jsonBody is not null)
            {
                request.Content = JsonContent.Create(jsonBody, jsonBody.GetType(), options: RequestSerializerOptions);
            }
            else if (rawContent is not null)
            {
                request.Content = rawContent;
            }

            HttpResponseMessage response;
            try
            {
                response = await _httpClient.SendAsync(request, cancellationToken);
            }
            catch (HttpRequestException)
            {
                return GitHubResult<HttpResponseMessage>.Failure(GitHubError.NetworkError);
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                return GitHubResult<HttpResponseMessage>.Failure(GitHubError.Timeout);
            }

            if (response.IsSuccessStatusCode)
            {
                return GitHubResult<HttpResponseMessage>.Success(response);
            }

            var error = MapStatusCode(response, notFoundError, conflictError, unprocessableError);
            response.Dispose();
            return GitHubResult<HttpResponseMessage>.Failure(error);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(System.Runtime.InteropServices.MemoryMarshal.AsBytes(tokenChars.AsSpan()));
        }
    }

    private static GitHubError MapStatusCode(HttpResponseMessage response, GitHubError notFoundError, GitHubError conflictError, GitHubError unprocessableError)
    {
        if (response.StatusCode == HttpStatusCode.Unauthorized)
        {
            return GitHubError.AuthenticationFailed;
        }

        if (response.StatusCode == HttpStatusCode.Forbidden)
        {
            if (response.Headers.TryGetValues("x-ratelimit-remaining", out var remaining) && remaining.FirstOrDefault() == "0")
            {
                return GitHubError.RateLimited;
            }

            return response.Headers.RetryAfter is not null ? GitHubError.SecondaryRateLimited : GitHubError.PermissionDenied;
        }

        if (response.StatusCode == HttpStatusCode.NotFound)
        {
            return notFoundError;
        }

        if (response.StatusCode == HttpStatusCode.Conflict)
        {
            return conflictError;
        }

        if (response.StatusCode == HttpStatusCode.MethodNotAllowed)
        {
            // GitHubのPR merge APIは、mergeableでない場合405 Method Not Allowedを返す。
            return GitHubError.PullRequestNotMergeable;
        }

        if (response.StatusCode == HttpStatusCode.UnprocessableEntity)
        {
            return unprocessableError;
        }

        if ((int)response.StatusCode == 429)
        {
            return GitHubError.SecondaryRateLimited;
        }

        return GitHubError.ApiError;
    }

    private static async Task<T?> ReadAsync<T>(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        try
        {
            using (response)
            {
                return await response.Content.ReadFromJsonAsync<T>(cancellationToken);
            }
        }
        catch (System.Text.Json.JsonException)
        {
            return default;
        }
    }

    private static bool IsValidBranch(BranchResponse branch) =>
        !string.IsNullOrEmpty(branch.Name) && !string.IsNullOrEmpty(branch.Commit?.Sha);

    private static bool IsValidWorkflowRun(WorkflowRunResponse run) =>
        run.Id > 0 && !string.IsNullOrEmpty(run.Name) && !string.IsNullOrEmpty(run.HeadBranch)
        && !string.IsNullOrEmpty(run.HeadSha) && !string.IsNullOrEmpty(run.Event)
        && !string.IsNullOrEmpty(run.Status) && !string.IsNullOrEmpty(run.Actor?.Login)
        && !string.IsNullOrEmpty(run.HtmlUrl);

    private static GitHubWorkflowRunInfo ToWorkflowRunInfo(WorkflowRunResponse run) => new(
        run.Id, run.Name!, run.HeadBranch!, run.HeadSha!, run.Event!, run.Status!, run.Conclusion,
        run.Actor!.Login!, run.CreatedAt, run.UpdatedAt, run.HtmlUrl!);

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
        IsRetryableMergeability(pr.Mergeable, pr.MergeableState) ? 2 : null);

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

    private static bool IsValidComment(IssueCommentResponse comment) =>
        comment.Id > 0 && comment.Body is not null && comment.User?.Login is not null && comment.HtmlUrl is not null;

    private static GitHubPullRequestComment ToPullRequestComment(IssueCommentResponse comment) => new(
        comment.Id, comment.Body!, comment.User!.Login!, comment.CreatedAt, comment.UpdatedAt, comment.HtmlUrl!);

    private static bool IsValidReview(PullRequestReviewResponse review) =>
        review.Id > 0 && review.User?.Login is not null && review.State is not null &&
        review.SubmittedAt is not null && review.CommitId is not null && review.HtmlUrl is not null;

    private static GitHubPullRequestReview ToPullRequestReview(PullRequestReviewResponse review) => new(
        review.Id, review.Body, review.User!.Login!, review.State!, review.SubmittedAt!.Value,
        review.CommitId!, review.HtmlUrl!);

    private sealed record RepositoryResponse(
        [property: JsonPropertyName("default_branch")] string? DefaultBranch,
        string? Description);

    private sealed record RepositoryDescriptionUpdate(string Description);

    private sealed record WorkflowDispatchBody(string Ref, IReadOnlyDictionary<string, string> Inputs);

    private sealed record WorkflowRunsResponse(
        [property: JsonPropertyName("workflow_runs")] IReadOnlyList<WorkflowRunResponse>? WorkflowRuns);

    private sealed record WorkflowRunResponse(
        long Id,
        string? Name,
        [property: JsonPropertyName("head_branch")] string? HeadBranch,
        [property: JsonPropertyName("head_sha")] string? HeadSha,
        string? Event,
        string? Status,
        string? Conclusion,
        UserRef? Actor,
        [property: JsonPropertyName("created_at")] DateTimeOffset CreatedAt,
        [property: JsonPropertyName("updated_at")] DateTimeOffset UpdatedAt,
        [property: JsonPropertyName("html_url")] string? HtmlUrl);

    private sealed record BranchResponse(string? Name, CommitRef? Commit, bool Protected);

    private sealed record CommitRef(string? Sha);

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
        int? Deletions);

    private sealed record PullRequestRef(string? Ref);

    private sealed record UserRef(string? Login);

    private sealed record TagResponse(string? Name, CommitRef? Commit);

    private sealed record GitRefResponse(GitRefObject? Object);

    private sealed record GitRefObject(string? Sha, string? Type);

    private sealed record GitTagObjectResponse(string? Sha, string? Message, GitTaggerResponse? Tagger, GitRefObject? Object);

    private sealed record GitTaggerResponse(string? Name, DateTimeOffset? Date);

    private sealed record CreatePullRequestRequest(string Title, string? Body, string Head, [property: JsonPropertyName("base")] string BaseBranch, bool Draft);

    private sealed record MergePullRequestRequest([property: JsonPropertyName("commit_message")] string? CommitMessage, [property: JsonPropertyName("merge_method")] string? MergeMethod);

    private sealed record UpdatePullRequestRequest(string State);

    private sealed record CreateIssueCommentRequest(string Body);

    private sealed record IssueCommentResponse(
        long Id,
        string? Body,
        UserRef? User,
        [property: JsonPropertyName("created_at")] DateTimeOffset CreatedAt,
        [property: JsonPropertyName("updated_at")] DateTimeOffset UpdatedAt,
        [property: JsonPropertyName("html_url")] string? HtmlUrl);

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

    private sealed record CreateTagObjectRequest(string Tag, string Message, string Object, string Type);

    private sealed record CreateTagObjectResponse(string? Sha);

    private sealed record CreateRefRequest(string Ref, string Sha);

    private sealed record CreateReleaseRequest(
        [property: JsonPropertyName("tag_name")] string TagName,
        string Name,
        string? Body,
        bool Draft,
        bool Prerelease);

    private sealed record PublishReleaseRequest(bool Draft);

    private sealed record ReleaseResponse(
        long Id,
        [property: JsonPropertyName("tag_name")] string? TagName,
        string? Name,
        bool Draft,
        bool Prerelease,
        [property: JsonPropertyName("html_url")] string? HtmlUrl,
        [property: JsonPropertyName("upload_url")] string? UploadUrl,
        IReadOnlyList<ReleaseAssetResponse>? Assets = null);

    private sealed record ReleaseAssetResponse(
        long Id,
        string? Name,
        long Size,
        [property: JsonPropertyName("browser_download_url")] string? BrowserDownloadUrl);
}
