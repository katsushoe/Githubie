using Githubie.Application.Git;

var repositoryId = Environment.GetEnvironmentVariable(GitAskPassProtocol.RepositoryIdVariable);
var prompt = args.Length > 0 ? args[0] : string.Empty;

if (string.IsNullOrWhiteSpace(repositoryId) || !OperatingSystem.IsWindows())
{
    return 1;
}

var tokenStore = Githubie.Infrastructure.Credentials.GitAskPassTokenStoreFactory.Create(
    AppContext.BaseDirectory,
    Environment.GetEnvironmentVariable(GitAskPassProtocol.UserCredentialDatabaseVariable));
if (tokenStore is null)
{
    return 1;
}

var responder = new GitAskPassResponder(tokenStore);

var response = responder.Respond(repositoryId, prompt);
if (!response.IsSuccess)
{
    return 1;
}

Console.Out.Write(response.Value);
return 0;
