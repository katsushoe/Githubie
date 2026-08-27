using Githubie.Application.Configuration;
using Githubie.Application.Credentials;
using Githubie.Application.Git;
using Githubie.Application.GitHub;
using Githubie.Application.Repositories;
using Githubie.Infrastructure.Configuration;
using Githubie.Infrastructure.Credentials;
using Githubie.Infrastructure.Git;
using Githubie.Infrastructure.GitHub;
using Githubie.Application.Interactive;
using Githubie.Infrastructure.Interactive;
using Githubie.Infrastructure.Repositories;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Githubie.Server;

/// <summary>
/// 設定読込からサービスグラフ構築までを1メソッドで完結させるComposition Rootです。
/// 失敗時は例外を投げず<see cref="GithubieCompositionResult"/>としてエラーを返します。
/// </summary>
public static class GithubieCompositionRoot
{
    public static async Task<GithubieCompositionResult> BuildAsync(string configPath, string binDirectory, CancellationToken cancellationToken)
    {
        if (!File.Exists(configPath))
        {
            return GithubieCompositionResult.Failure($"config file not found: {configPath}");
        }

        GithubieOptions options;
        await using (var stream = File.OpenRead(configPath))
        {
            var loadResult = await new JsonGithubieOptionsLoader().LoadAsync(stream, cancellationToken);
            if (!loadResult.IsSuccess)
            {
                return GithubieCompositionResult.Failure(loadResult.Errors.Select(e => $"{e.Path}: {e.Message} ({e.Code})").ToArray());
            }

            options = loadResult.Options!;
        }

        if (!OperatingSystem.IsWindows())
        {
            return GithubieCompositionResult.Failure("Githubie requires Windows (DPAPI credential storage).");
        }

        var layout = GithubiePathLayout.FromBinDirectory(binDirectory);
        SqliteRepositoryConfigurationStore configurationStore;
        IReadOnlyDictionary<string, RepositoryOptions> repositories;
        try
        {
            configurationStore = new SqliteRepositoryConfigurationStore(layout.RepositoryDatabasePath);
            repositories = await configurationStore.InitializeAsync(options.Repositories, cancellationToken);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidDataException)
        {
            return GithubieCompositionResult.Failure($"repository database initialization failed: {ex.Message}");
        }

        options = options with { Repositories = repositories };
        var databaseErrors = JsonGithubieOptionsLoader.Validate(options);
        if (databaseErrors.Count > 0)
        {
            return GithubieCompositionResult.Failure(databaseErrors
                .Select(error => $"{error.Path}: {error.Message} ({error.Code})")
                .ToArray());
        }
        var askPassExecutablePath = Path.Combine(binDirectory, "Githubie.AskPass.exe");
        var approvalPromptExecutablePath = Path.Combine(binDirectory, "Githubie.ApprovalPrompt.exe");

        var services = new ServiceCollection();

        services.AddLogging(logging => logging.AddProvider(new DailyFileLoggerProvider(layout.LogsDirectory)));
        services.AddSingleton(options);
        services.AddSingleton<IGithubieOptionsLoader, JsonGithubieOptionsLoader>();
        services.AddSingleton<IApiTokenStore>(new DpapiFileTokenStore(layout.SecretsDirectory));
        services.AddSingleton(new RepositoryAllowlist(options.Repositories));
        services.AddSingleton<IRepositoryEnvironment, RepositoryEnvironment>();
        services.AddSingleton<LocalPathValidator>();
        services.AddSingleton<IProcessExecutor, ProcessExecutor>();
        services.AddSingleton<IGitCommandClient>(sp =>
            new GitCommandClient(sp.GetRequiredService<IProcessExecutor>(), askPassExecutablePath));
        services.AddSingleton<IInteractiveApprovalPrompt>(sp => CreateApprovalPrompt(sp, approvalPromptExecutablePath));
        services.AddSingleton<IRepositoryConfigurationStore>(configurationStore);
        services.AddSingleton<IRepositoryRegistrationService, RepositoryRegistrationService>();
        services.AddSingleton<IRepositoryManagementService, RepositoryManagementService>();
        services.AddSingleton<IGitGateway, GitGateway>();

        services.AddHttpClient("GitHub", client =>
        {
            client.BaseAddress = new Uri("https://api.github.com/");
            client.Timeout = TimeSpan.FromSeconds(30);
        });
        services.AddSingleton<IGitHubApiClient>(sp => new GitHubApiClient(
            sp.GetRequiredService<IHttpClientFactory>().CreateClient("GitHub"), sp.GetRequiredService<IApiTokenStore>()));
        services.AddSingleton<IGitHubRepositoryGateway, GitHubRepositoryGateway>();

        try
        {
            var provider = services.BuildServiceProvider(new ServiceProviderOptions { ValidateOnBuild = true, ValidateScopes = true });
            return GithubieCompositionResult.Success(provider);
        }
        catch (Exception ex) when (ex is InvalidOperationException or AggregateException)
        {
            return GithubieCompositionResult.Failure($"service composition failed: {ex.Message}");
        }
    }

#pragma warning disable CA1416
    private static IInteractiveApprovalPrompt CreateApprovalPrompt(IServiceProvider services, string executablePath) =>
        new WindowsInteractiveApprovalPrompt(
            executablePath,
            services.GetRequiredService<ILogger<WindowsInteractiveApprovalPrompt>>());
#pragma warning restore CA1416
}
