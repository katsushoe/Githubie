using System.ComponentModel;
using ModelContextProtocol.Server;

namespace Githubie.Server;

/// <summary>
/// Githubieの目的と安全な利用手順をMCP Clientへ提供します。
/// </summary>
[McpServerPromptType]
public sealed class GithubieMcpPrompts
{
    /// <summary>
    /// MCP初期化応答でClientへ通知するServer Instructionsです。
    /// </summary>
    public const string ServerInstructions = """
        Githubie is a policy-enforcing gateway for operating allowlisted local Git repositories and GitHub.com from an MCP client. It exposes typed operations instead of arbitrary Git commands, repository URLs, or credentials.

        Pass the configured Githubie repository ID to every repository-scoped tool. In every conversation, call list_projects before github_push and select the intended repository ID from its candidates. Start other read-only work with github_repository_status, then use the narrowest tool that satisfies the request. Use github_fetch before comparing remote state, and use github_pull only for an allowed branch when a fast-forward update is intended. Use github_push for ordinary development-branch publication; protected branches reject direct pushes.

        Treat tools marked destructive as state-changing. Obtain explicit user intent before creating, updating, closing, merging, deleting, dispatching, pushing, or rewriting history. For github_history_rewrite, call it with dry_run=true first, show the plan, preserve a recovery ref, and proceed only after explicit approval. Repository registration, policy changes, renaming, unregistration, and history rewriting can also require approval in the interactive Windows session.

        Never request or expose a Personal Access Token through MCP. Repository registration can open a separate foreground token dialog after approval; credentials can also be stored with the Githubie CLI. Do not bypass Githubie with arbitrary git commands or direct GitHub API calls when operating a repository managed by this server.

        Use the githubie_usage prompt for a concise workflow and tool-selection guide.
        """;

    /// <summary>
    /// Githubieの目的、基本手順、安全条件を説明するPromptを返します。
    /// </summary>
    /// <returns>Agent向けの利用ガイドです。</returns>
    [McpServerPrompt(Name = "githubie_usage")]
    [Description("Githubieの目的、基本的な使い方、Tool選択、安全条件を示します。")]
    public static string GetUsageGuide() => """
        Use Githubie as the controlled boundary between the MCP client, an allowlisted local Git repository, and GitHub.com.

        Recommended workflow:
        1. Call list_projects to discover the configured Githubie repository IDs, then select the intended candidate. Do not pass a path or GitHub URL where a repository ID is required.
        2. Call github_repository_status to inspect the current branch, local and remote heads, ahead/behind counts, and working-tree changes.
        3. For remote synchronization, call github_fetch first. Call github_pull only when a fast-forward update of an allowed branch is intended.
        4. Use the dedicated typed tools for branches, pull requests, reviews, comments, tags, releases, repository descriptions, and GitHub Actions. Prefer read-only list/get/diff tools before mutations.
        5. In every conversation, call list_projects again immediately before github_push and verify the selected repository ID. Use github_push only after local validation and only when the user intends to publish committed changes. If the repository is not registered, the push error includes registered candidates. Direct pushes to protected branches are rejected.
        6. Use github_history_rewrite only for an explicitly requested correction: dry-run first, review every old/new SHA, save a recovery ref, then obtain approval for the real operation.

        Githubie intentionally does not expose arbitrary Git commands, arbitrary repository URLs, or authentication secrets. Repository registration opens an optional foreground token dialog after approval; skipped or failed token storage does not roll back registration. Credentials can also be configured with the Githubie CLI. Never ask the user to provide a Personal Access Token in chat or in a tool argument.
        """;
}
