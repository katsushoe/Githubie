using Githubie.Application.Credentials;

namespace Githubie.Application.Git;

/// <summary>
/// Githubie.AskPass実行ファイルから呼び出される応答ロジックです。
/// Gitが渡す"Username for ..."/"Password for ..."プロンプトを判定し、Usernameには固定値、
/// PasswordにはPersonal Access Tokenを返します。
/// </summary>
public sealed class GitAskPassResponder(IApiTokenStore tokenStore)
{
    private readonly IApiTokenStore _tokenStore = tokenStore;

    public AskPassResponse Respond(string repositoryId, string prompt)
    {
        if (!Application.Repositories.RepositoryId.IsValid(repositoryId))
        {
            return AskPassResponse.Failure("invalid repository id");
        }

        if (prompt.StartsWith("Username", StringComparison.OrdinalIgnoreCase))
        {
            return AskPassResponse.Success(GitAskPassProtocol.FixedUsername);
        }

        if (prompt.StartsWith("Password", StringComparison.OrdinalIgnoreCase))
        {
            var read = _tokenStore.Read(repositoryId);
            if (!read.IsSuccess || read.Token is null)
            {
                return AskPassResponse.Failure("token not found");
            }

            return AskPassResponse.Success(new string(read.Token));
        }

        return AskPassResponse.Failure("unsupported prompt");
    }
}

/// <summary>
/// AskPass応答結果を表します。
/// </summary>
public sealed record AskPassResponse(bool IsSuccess, string? Value, string? Error)
{
    public static AskPassResponse Success(string value) => new(true, value, null);

    public static AskPassResponse Failure(string error) => new(false, null, error);
}
