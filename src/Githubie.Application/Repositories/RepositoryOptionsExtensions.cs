using Githubie.Application.Configuration;
using Githubie.Domain;

namespace Githubie.Application.Repositories;

/// <summary>設定値からDomain層のRepositoryPolicyを組み立てます。</summary>
public static class RepositoryOptionsExtensions
{
    public static RepositoryPolicy ToPolicy(this RepositoryOptions options, string repositoryId) => new(
        RepositoryId: repositoryId,
        DevelopBranch: options.DevelopBranch,
        MainBranch: options.MainBranch,
        DirectPushBranches: new HashSet<string>(options.DirectPushBranches, StringComparer.Ordinal),
        PullBranches: new HashSet<string>(options.PullBranches, StringComparer.Ordinal),
        PullRequestRoutes: new HashSet<PullRequestRoute>(options.PullRequestRoutes),
        ProtectedBranches: new HashSet<string>(options.ProtectedBranches, StringComparer.Ordinal),
        TagTargetBranch: options.TagTargetBranch,
        TagPattern: options.TagPattern,
        RequireCleanWorkingTree: options.RequireCleanWorkingTree);
}
