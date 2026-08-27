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
}

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
