using System.Text.Json.Serialization;

namespace Githubie.Application.Configuration;

/// <summary>
/// Githubie全体の設定を表します。
/// </summary>
public sealed record GithubieOptions(
    int McpPort,
    string McpPath,
    IReadOnlyDictionary<string, RepositoryOptions> Repositories)
{
    public const int DefaultMcpPort = 45460;
    public const string DefaultMcpPath = "/mcp";

    /// <summary>
    /// MoyaiからのRepository操作を検証するProvider Assertion設定です。
    /// 未設定の場合、GithubieはMoyaiなしの単体動作となりAssertionを検証しません。
    /// </summary>
    public ProviderAuthenticationOptions? ProviderAuthentication { get; init; }
}

/// <summary>
/// Provider Assertionの公開設定と管理者登録済みProject対応です。
/// `RequireAssertion`がfalseの場合、Moyai由来の目印（AuthorizationまたはX-Moyai-Operation-Id）を
/// 持たないローカル直接呼び出しは従来どおり受け付け、目印を持つ要求だけを厳密に検証します。
/// </summary>
public sealed record ProviderAuthenticationOptions(
    string Issuer,
    string TrustBundlePath,
    string ReplayDatabasePath,
    IReadOnlyDictionary<string, Guid> Projects,
    string ProtocolVersion = "1",
    int AssertionLifetimeSeconds = 120,
    int ClockSkewSeconds = 30,
    bool RequireAssertion = false);

/// <summary>
/// リポジトリ単位の設定を表します。
/// `GitHubOwner`/`GitHubRepo`はJsonNamingPolicy.SnakeCaseLowerの自動変換が"git_hub_owner"になり
/// 意図した"github_owner"と一致しないため、JsonPropertyNameで明示します。
/// </summary>
public sealed record RepositoryOptions(
    [property: JsonPropertyName("github_owner")] string GitHubOwner,
    [property: JsonPropertyName("github_repo")] string GitHubRepo,
    string LocalRoot,
    string Remote,
    string DevelopBranch,
    string MainBranch,
    IReadOnlyList<string> DirectPushBranches,
    IReadOnlyList<string> PullBranches,
    IReadOnlyList<string> ProtectedBranches,
    string TagTargetBranch,
    string TagPattern,
    string MergeMethod,
    bool RequireCleanWorkingTree)
{
    public string? CommitAuthorName { get; init; }

    public string? CommitAuthorEmail { get; init; }

    public IReadOnlyDictionary<string, WorkflowPolicyOptions> Workflows { get; init; }
        = new Dictionary<string, WorkflowPolicyOptions>(StringComparer.Ordinal);
}

public sealed record WorkflowPolicyOptions(
    IReadOnlyList<string> AllowedRefs,
    IReadOnlyDictionary<string, WorkflowInputPolicyOptions> Inputs,
    int MaxConcurrent = 1,
    int CorrelationTimeoutSeconds = 15);

public sealed record WorkflowInputPolicyOptions(
    string Type = "string",
    bool Required = false,
    int MaxLength = 256,
    bool Secret = false);
