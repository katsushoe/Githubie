using Githubie.Application.Configuration;
using Githubie.Application.Git;
using Githubie.Application.GitHub;
using Githubie.Application.Repositories;
using Githubie.Infrastructure.Configuration;
using Githubie.Server;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Moyai.ProviderAuthentication;

using System.Runtime.Versioning;
using Githubie.Application.Credentials;
using Githubie.Application.Interactive;

namespace Githubie.Server;

[SupportedOSPlatform("windows")]
internal static class HostRuntime
{
    public static async Task<int> RunAsync(string[] args, CancellationToken cancellationToken, GithubiePathLayout? isolatedUserLayout = null)
    {
        var binDirectory = AppContext.BaseDirectory;
        // 既定は単体動作モード。`--moyai`を指定した場合だけMoyai連携モード（Provider Assertion検証）で起動する。
        GithubieServerArguments serverArguments;
        try
        {
            serverArguments = GithubieServerArguments.Parse(args);
        }
        catch (ArgumentException ex)
        {
            Console.Error.WriteLine(ex.Message);
            return 1;
        }
        var userHost = args.Contains("--user-host", StringComparer.OrdinalIgnoreCase);
        var layout = userHost ? isolatedUserLayout ?? GithubiePathLayout.ForCurrentUser(binDirectory) : GithubiePathLayout.FromBinDirectory(binDirectory);
        UserProfileIdentity? identity = null;
        FileStream? profileLock = null;
        if (userHost)
        {
            identity = await new UserProfileStore(layout).InitializeAsync(cancellationToken);
            if (args.Contains("--autostart", StringComparer.OrdinalIgnoreCase)
                && !new UserAutostart(Path.Combine(layout.BinDirectory, "Githubie.Server.exe")).Initialize()) return 0;
            var settings = System.Text.Json.JsonSerializer.Deserialize<UserHostOptions>(
                await File.ReadAllTextAsync(Path.Combine(layout.ConfigDirectory, "host.json"), cancellationToken),
                new System.Text.Json.JsonSerializerOptions { UnmappedMemberHandling = System.Text.Json.Serialization.JsonUnmappedMemberHandling.Disallow })
                ?? throw new InvalidDataException("User host settings are missing.");
            serverArguments = serverArguments with { MoyaiIntegration = serverArguments.MoyaiIntegration || settings.MoyaiIntegration,
                DirectUnrestricted = serverArguments.DirectUnrestricted || settings.DirectUnrestricted };
            profileLock = new FileStream(Path.Combine(layout.DataDirectory, "host.lock"), FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
        }
        using var ownedProfileLock = profileLock;
        var configPath = serverArguments.ConfigPath ?? Path.Combine(layout.ConfigDirectory, "githubie.json");
        var readinessStore = new ServiceReadinessStore(layout.ServiceStatePath);
        await readinessStore.WriteInitializingAsync(CancellationToken.None);
        
        var composition = userHost
            ? await GithubieCompositionRoot.BuildForUserHostAsync(configPath, layout, serverArguments.MoyaiIntegration, serverArguments.DirectUnrestricted, cancellationToken)
            : await GithubieCompositionRoot.BuildAsync(configPath, binDirectory, serverArguments.MoyaiIntegration, serverArguments.DirectUnrestricted, cancellationToken);
        if (!composition.IsSuccess)
        {
            // サービス起動時は標準エラーが残らないため、失敗理由をservice-state.jsonへ残す。
            await readinessStore.WriteFailedAsync(
                $"service composition failed: {string.Join("; ", composition.Errors)}", CancellationToken.None);
            foreach (var error in composition.Errors)
            {
                Console.Error.WriteLine(error);
            }
        
            return 1;
        }
        
        await using var applicationServices = composition.Services!;
        var options = applicationServices.GetRequiredService<GithubieOptions>();
        
        var builder = WebApplication.CreateSlimBuilder(serverArguments.HostArguments);
        
        builder.Logging.AddProvider(new DailyFileLoggerProvider(layout.LogsDirectory));
        builder.Logging.AddFilter("Microsoft", LogLevel.Warning);
        builder.Logging.AddFilter("System", LogLevel.Warning);
        builder.Logging.AddFilter("ModelContextProtocol", LogLevel.Warning);
        if (!userHost) builder.Services.AddWindowsService(service => service.ServiceName = "Githubie");
        builder.Services.Configure<HostOptions>(host => host.ShutdownTimeout = TimeSpan.FromSeconds(5));
        
        builder.WebHost.ConfigureKestrel(server => server.ListenLocalhost(options.McpPort));
        
        var innerGitGateway = applicationServices.GetRequiredService<IGitGateway>();
        var innerGitHubGateway = applicationServices.GetRequiredService<IGitHubRepositoryGateway>();
        var innerRegistrationService = applicationServices.GetRequiredService<IRepositoryRegistrationService>();
        var innerManagementService = applicationServices.GetRequiredService<IRepositoryManagementService>();
        var repositoryAllowlist = applicationServices.GetRequiredService<RepositoryAllowlist>();
        var assertionExecutionContext = applicationServices.GetRequiredService<GithubieAssertionExecutionContext>();
        // 単体動作モードではCompositionがprovider_authenticationを外すため、Assertion検証を組み込まない。
        Es256AssertionValidator? assertionValidator = null;
        if (options.ProviderAuthentication is { } authentication)
        {
            var replay = new SqliteAssertionReplayCache(
                new SqliteAssertionReplayCacheOptions(authentication.ReplayDatabasePath, 5),
                TimeProvider.System);
            try
            {
                await replay.InitializeAsync(CancellationToken.None);
            }
            catch (Exception ex) when (ex is ProviderAuthenticationException or IOException or UnauthorizedAccessException)
            {
                await readinessStore.WriteFailedAsync("provider authentication initialization failed", CancellationToken.None);
                Console.Error.WriteLine($"provider authentication initialization failed: {ex.Message}");
                return 1;
            }
        
            assertionValidator = new Es256AssertionValidator(
                new FileAssertionTrustStore(authentication.TrustBundlePath),
                replay,
                new AssertionOptions(
                    authentication.Issuer,
                    authentication.AssertionLifetimeSeconds,
                    authentication.ClockSkewSeconds),
                GithubieAssertionPolicy.CreateCapability(),
                TimeProvider.System);
        }
        
        builder.Services.AddSingleton<IGithubieAuditLogger, GithubieAuditLogger>();
        builder.Services.AddSingleton<IGitGateway>(sp => new AuditedGitGateway(innerGitGateway, sp.GetRequiredService<IGithubieAuditLogger>()));
        builder.Services.AddSingleton<IGitHubRepositoryGateway>(sp => new AuditedGitHubRepositoryGateway(innerGitHubGateway, sp.GetRequiredService<IGithubieAuditLogger>()));
        builder.Services.AddSingleton<IRepositoryRegistrationService>(sp =>
            new AuditedRepositoryRegistrationService(innerRegistrationService, sp.GetRequiredService<IGithubieAuditLogger>()));
        builder.Services.AddSingleton<IRepositoryManagementService>(sp =>
            new AuditedRepositoryManagementService(innerManagementService, sp.GetRequiredService<IGithubieAuditLogger>()));
        builder.Services.AddSingleton(repositoryAllowlist);
        builder.Services.AddSingleton(options);
        if (assertionValidator is not null)
        {
            builder.Services.AddSingleton<IAssertionValidator>(assertionValidator);
            builder.Services.AddSingleton<IAssertionExecutionValidator>(assertionValidator);
        }
        builder.Services.AddSingleton(assertionExecutionContext);
        builder.Services.AddSingleton<GithubieMcpTools>();
        
        builder.Services
            .AddMcpServer(options => options.ServerInstructions = GithubieMcpPrompts.ServerInstructions)
            .WithHttpTransport(transport => transport.Stateless = true)
            .WithTools<GithubieMcpTools>(GithubieMcpJson.CreateOptions())
            .WithPrompts<GithubieMcpPrompts>(GithubieMcpJson.CreateOptions());
        
        await using var app = builder.Build();
        UserHostConnection? connection = null;
        var connections = userHost ? new UserHostConnectionStore(layout.DataDirectory,
            isolatedUserLayout is null ? null : Environment.ProcessPath) : null;
        if (userHost)
        {
            app.Use(async (context, next) =>
            {
                if (connection is null || !await UserHostRequestGate.IsAuthorizedAsync(context.Request,
                        connection.Credential, options.McpPort, assertionValidator is not null, context.RequestAborted, connection.Generation))
                {
                    context.Response.StatusCode = StatusCodes.Status403Forbidden;
                    context.Response.Headers[UserHostProtocol.ErrorHeader] = UserHostProtocol.OwnerRejected;
                    return;
                }
                await next();
            });
        }
        
        app.Use(async (context, next) =>
        {
            if (context.Request.Path.StartsWithSegments(options.McpPath))
            {
                var origin = context.Request.Headers.Origin.FirstOrDefault();
                if (!McpOriginValidator.IsAllowed(origin, options.McpPort))
                {
                    context.Response.StatusCode = StatusCodes.Status403Forbidden;
                    return;
                }
            }
        
            await next();
        });
        
        if (assertionValidator is not null)
        {
            app.UseMiddleware<GithubieAssertionMiddleware>();
        }
        
        app.MapMcp(options.McpPath);
        
        try
        {
            await app.StartAsync(cancellationToken);
            if (connections is not null && identity is not null)
                connection = await connections.PublishAsync(identity, new Uri($"http://127.0.0.1:{options.McpPort}{options.McpPath}"), cancellationToken);
            app.Logger.LogInformation(
                "Githubie started with integration_mode={IntegrationMode}, direct_connection={DirectConnection}.",
                GithubieIntegrationMode.IntegrationMode(options),
                GithubieIntegrationMode.DirectConnection(options));
            var unmappedRepositories = JsonGithubieOptionsLoader.FindUnmappedRepositories(options);
            if (unmappedRepositories.Length > 0)
            {
                app.Logger.LogWarning(
                    "Repositories without a Moyai Project UUID reject Moyai requests (auth_project_mismatch): {Repositories}.",
                    string.Join(", ", unmappedRepositories));
            }
            await readinessStore.WriteReadyAsync(CancellationToken.None);
        }
        catch (Exception ex) when (ex is IOException or InvalidOperationException)
        {
            await readinessStore.WriteFailedAsync($"service startup failed: {ex.GetType().Name}", CancellationToken.None);
            Console.Error.WriteLine($"service startup failed: {ex.Message}");
            return 1;
        }
        using var controlLifetime = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, app.Lifetime.ApplicationStopping);
        var credentialCommands = new UserHostCredentials(repositoryAllowlist,
            applicationServices.GetRequiredService<IInteractiveTokenPrompt>(), applicationServices.GetRequiredService<IApiTokenStore>(),
            app.Services.GetRequiredService<IGitHubRepositoryGateway>(), app.Services.GetRequiredService<ILogger<UserHostCredentials>>(),
            applicationServices.GetService<RepositoryMutationLock>());
        var controlTask = connection is null ? Task.Delay(Timeout.Infinite, controlLifetime.Token)
            : UserHostControlPipe.RunAsync(connection, (request, token) => request.Command is "status" or "stop"
                ? Task.FromResult(new UserHostControlResponse(true, "ready")) : credentialCommands.ExecuteAsync(request, token),
                app.Lifetime.StopApplication, controlLifetime.Token);
        try
        {
            var shutdown = app.WaitForShutdownAsync(cancellationToken);
            if (await Task.WhenAny(shutdown, controlTask) == controlTask && !controlLifetime.IsCancellationRequested)
                await controlTask;
            await shutdown;
        }
        finally
        {
            controlLifetime.Cancel();
            try { await controlTask; } catch (OperationCanceledException) when (controlLifetime.IsCancellationRequested) { }
            var previousConnection = connection;
            connection = null;
            try
            {
                if (connections is not null && previousConnection is not null) connections.Revoke(previousConnection);
            }
            finally
            {
                using var stopTimeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
                try { await app.StopAsync(stopTimeout.Token); }
                catch (OperationCanceledException) when (stopTimeout.IsCancellationRequested)
                { app.Logger.LogWarning("User host shutdown reached its deadline."); }
            }
        }
        return 0;
    }
}
