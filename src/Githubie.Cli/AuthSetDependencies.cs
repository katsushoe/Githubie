using Githubie.Application.Configuration;
using Githubie.Application.Credentials;
using Githubie.Application.Interactive;

namespace Githubie.Cli;

internal sealed record AuthSetDependencies(
    Func<string, CancellationToken, Task<RepositoryOptions?>> GetRepository,
    Func<TokenPromptRequest, CancellationToken, Task<char[]?>> RequestGui,
    Func<char[]> ReadConsole,
    Func<string, char[], ApiTokenStoreResult> Save);
