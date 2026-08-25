using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using Githubie.Application.Configuration;
using Githubie.Application.Repositories;

namespace Githubie.Infrastructure.Configuration;

/// <summary>
/// `githubie.json`を読み書きします。プロパティ名はsnake_caseで統一します。
/// </summary>
public sealed class JsonGithubieOptionsLoader : IGithubieOptionsLoader
{
    private static readonly string[] AllowedMergeMethods = ["merge", "squash", "rebase"];

    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
        ReadCommentHandling = JsonCommentHandling.Disallow,
        AllowTrailingCommas = false,
    };

    private static readonly JsonSerializerOptions WriterOptions = new(SerializerOptions)
    {
        WriteIndented = true,
    };

    public async Task<ConfigurationLoadResult> LoadAsync(Stream stream, CancellationToken cancellationToken)
    {
        GithubieOptions? options;
        try
        {
            options = await JsonSerializer.DeserializeAsync<GithubieOptions>(stream, SerializerOptions, cancellationToken);
        }
        catch (JsonException ex)
        {
            return ConfigurationLoadResult.Failure(new ConfigurationError(ConfigurationErrorCode.InvalidJson, "$", ex.Message));
        }

        if (options is null)
        {
            return ConfigurationLoadResult.Failure(new ConfigurationError(ConfigurationErrorCode.InvalidJson, "$", "root value must be a JSON object."));
        }

        var errors = ValidateValues(options);
        return errors.Count == 0 ? ConfigurationLoadResult.Success(options) : ConfigurationLoadResult.Failure(errors);
    }

    public Task SaveAsync(GithubieOptions options, Stream stream, CancellationToken cancellationToken) =>
        JsonSerializer.SerializeAsync(stream, options, WriterOptions, cancellationToken);

    private static List<ConfigurationError> ValidateValues(GithubieOptions options)
    {
        var errors = new List<ConfigurationError>();

        if (options.McpPort is < 1 or > 65535)
        {
            errors.Add(new ConfigurationError(ConfigurationErrorCode.InvalidMcpPort, "$.mcp_port", "mcp_port must be between 1 and 65535."));
        }

        if (string.IsNullOrWhiteSpace(options.McpPath) || !options.McpPath.StartsWith('/'))
        {
            errors.Add(new ConfigurationError(ConfigurationErrorCode.InvalidMcpPath, "$.mcp_path", "mcp_path must start with '/'."));
        }

        foreach (var (repositoryId, repository) in options.Repositories)
        {
            var path = $"$.repositories.{repositoryId}";

            if (!RepositoryId.IsValid(repositoryId))
            {
                errors.Add(new ConfigurationError(ConfigurationErrorCode.InvalidRepositoryId, path, "repository id must match ^[A-Za-z0-9._-]+$ and be at most 128 characters."));
            }

            if (string.IsNullOrWhiteSpace(repository.GitHubOwner))
            {
                errors.Add(new ConfigurationError(ConfigurationErrorCode.InvalidGitHubOwner, $"{path}.github_owner", "github_owner must not be empty."));
            }

            if (string.IsNullOrWhiteSpace(repository.GitHubRepo))
            {
                errors.Add(new ConfigurationError(ConfigurationErrorCode.InvalidGitHubRepo, $"{path}.github_repo", "github_repo must not be empty."));
            }

            if (string.IsNullOrWhiteSpace(repository.LocalRoot))
            {
                errors.Add(new ConfigurationError(ConfigurationErrorCode.InvalidLocalRoot, $"{path}.local_root", "local_root must not be empty."));
            }

            if (string.IsNullOrWhiteSpace(repository.DevelopBranch) || string.IsNullOrWhiteSpace(repository.MainBranch))
            {
                errors.Add(new ConfigurationError(ConfigurationErrorCode.InvalidBranchName, path, "develop_branch and main_branch must not be empty."));
            }

            if (!IsValidRegex(repository.TagPattern))
            {
                errors.Add(new ConfigurationError(ConfigurationErrorCode.InvalidTagPattern, $"{path}.tag_pattern", "tag_pattern must be a valid regular expression."));
            }

            if (!AllowedMergeMethods.Contains(repository.MergeMethod, StringComparer.Ordinal))
            {
                errors.Add(new ConfigurationError(ConfigurationErrorCode.InvalidMergeMethod, $"{path}.merge_method", "merge_method must be one of: merge, squash, rebase."));
            }

            foreach (var (workflow, policy) in repository.Workflows)
            {
                var workflowPath = $"{path}.workflows.{workflow}";
                if (string.IsNullOrWhiteSpace(workflow) || policy.AllowedRefs.Count == 0
                    || policy.MaxConcurrent is < 1 or > 10
                    || policy.CorrelationTimeoutSeconds is < 1 or > 120
                    || policy.AllowedRefs.Any(string.IsNullOrWhiteSpace)
                    || policy.Inputs.Any(input => string.IsNullOrWhiteSpace(input.Key)
                        || input.Value.Type is not ("string" or "boolean" or "integer")
                        || input.Value.MaxLength is < 1 or > 4096))
                {
                    errors.Add(new ConfigurationError(ConfigurationErrorCode.InvalidWorkflowPolicy, workflowPath,
                        "workflow policy, refs, concurrency, timeout, or input schema is invalid."));
                }
            }
        }

        return errors;
    }

    private static bool IsValidRegex(string pattern)
    {
        try
        {
            _ = new Regex(pattern, RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1));
            return true;
        }
        catch (ArgumentException)
        {
            return false;
        }
    }
}
