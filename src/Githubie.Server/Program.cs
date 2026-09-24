using Githubie.Application.Configuration;
using Githubie.Application.Git;
using Githubie.Application.GitHub;
using Githubie.Application.Repositories;
using Githubie.Server;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Moyai.ProviderAuthentication;

var binDirectory = AppContext.BaseDirectory;
// 既定は単体動作モード。`--moyai`を指定した場合だけMoyai連携モード（Provider Assertion検証）で起動する。
var serverArguments = GithubieServerArguments.Parse(args);
var configPath = serverArguments.ConfigPath
    ?? Path.Combine(GithubiePathLayout.FromBinDirectory(binDirectory).ConfigDirectory, "githubie.json");
var layout = GithubiePathLayout.FromBinDirectory(binDirectory);
var readinessStore = new ServiceReadinessStore(layout.ServiceStatePath);
await readinessStore.WriteInitializingAsync(CancellationToken.None);

var composition = await GithubieCompositionRoot.BuildAsync(
    configPath, binDirectory, serverArguments.MoyaiIntegration, CancellationToken.None);
if (!composition.IsSuccess)
{
    await readinessStore.WriteFailedAsync("service composition failed", CancellationToken.None);
    foreach (var error in composition.Errors)
    {
        Console.Error.WriteLine(error);
    }

    return 1;
}

var applicationServices = composition.Services!;
var options = applicationServices.GetRequiredService<GithubieOptions>();

var builder = WebApplication.CreateSlimBuilder(serverArguments.HostArguments);

builder.Logging.AddProvider(new DailyFileLoggerProvider(layout.LogsDirectory));
builder.Logging.AddFilter("Microsoft", LogLevel.Warning);
builder.Logging.AddFilter("System", LogLevel.Warning);
builder.Logging.AddFilter("ModelContextProtocol", LogLevel.Warning);
builder.Services.AddWindowsService(service => service.ServiceName = "Githubie");

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

var app = builder.Build();

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
    await app.StartAsync();
    app.Logger.LogInformation(
        "Githubie started in {Mode} mode.",
        assertionValidator is null ? "standalone" : "Moyai integration");
    await readinessStore.WriteReadyAsync(CancellationToken.None);
}
catch (Exception ex) when (ex is IOException or InvalidOperationException)
{
    await readinessStore.WriteFailedAsync($"service startup failed: {ex.GetType().Name}", CancellationToken.None);
    Console.Error.WriteLine($"service startup failed: {ex.Message}");
    return 1;
}
await app.WaitForShutdownAsync();
return 0;
