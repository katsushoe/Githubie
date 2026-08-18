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

        return GitHubResult<GitHubRepositoryInfo>.Success(new GitHubRepositoryInfo(owner, repo, body.DefaultBranch));
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
            repositoryId, HttpMethod.Get, $"repos/{owner}/{repo}/git/refs/tags/{Uri.EscapeDataString(tag)}", null, cancellationToken, notFoundError: GitHubError.TagInvalid);
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
        pr.HtmlUrl!);

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

    private sealed record RepositoryResponse([property: JsonPropertyName("default_branch")] string? DefaultBranch);

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

    private sealed record CreateTagObjectRequest(string Tag, string Message, string Object, string Type);

    private sealed record CreateTagObjectResponse(string? Sha);

    private sealed record CreateRefRequest(string Ref, string Sha);
}
