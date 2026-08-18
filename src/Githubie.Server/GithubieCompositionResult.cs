using Microsoft.Extensions.DependencyInjection;

namespace Githubie.Server;

/// <summary>
/// Composition Rootの結果を表します。設定不正時は例外を投げず、エラー一覧を返します。
/// </summary>
public sealed record GithubieCompositionResult(ServiceProvider? Services, IReadOnlyList<string> Errors)
{
    public bool IsSuccess => Services is not null && Errors.Count == 0;

    public static GithubieCompositionResult Success(ServiceProvider services) => new(services, []);

    public static GithubieCompositionResult Failure(IReadOnlyList<string> errors) => new(null, errors);

    public static GithubieCompositionResult Failure(string error) => new(null, [error]);
}
