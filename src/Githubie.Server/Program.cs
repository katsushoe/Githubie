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

var binDirectory = AppContext.BaseDirectory;
var configPath = args.Length > 0 ? args[0] : Path.Combine(GithubiePathLayout.FromBinDirectory(binDirectory).ConfigDirectory, "githubie.json");

var composition = await GithubieCompositionRoot.BuildAsync(configPath, binDirectory, CancellationToken.None);
if (!composition.IsSuccess)
{
    foreach (var error in composition.Errors)
    {
        Console.Error.WriteLine(error);
    }

    return 1;
}

var applicationServices = composition.Services!;
var options = applicationServices.GetRequiredService<GithubieOptions>();
var layout = GithubiePathLayout.FromBinDirectory(binDirectory);

var builder = WebApplication.CreateSlimBuilder(args);

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

builder.Services.AddSingleton<IGithubieAuditLogger, GithubieAuditLogger>();
builder.Services.AddSingleton<IGitGateway>(sp => new AuditedGitGateway(innerGitGateway, sp.GetRequiredService<IGithubieAuditLogger>()));
builder.Services.AddSingleton<IGitHubRepositoryGateway>(sp => new AuditedGitHubRepositoryGateway(innerGitHubGateway, sp.GetRequiredService<IGithubieAuditLogger>()));
builder.Services.AddSingleton<IRepositoryRegistrationService>(sp =>
    new AuditedRepositoryRegistrationService(innerRegistrationService, sp.GetRequiredService<IGithubieAuditLogger>()));
builder.Services.AddSingleton<IRepositoryManagementService>(sp =>
    new AuditedRepositoryManagementService(innerManagementService, sp.GetRequiredService<IGithubieAuditLogger>()));
builder.Services.AddSingleton<GithubieMcpTools>();

builder.Services
    .AddMcpServer()
    .WithHttpTransport(transport => transport.Stateless = true)
    .WithTools<GithubieMcpTools>(GithubieMcpJson.CreateOptions());

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

app.MapMcp(options.McpPath);

await app.StartAsync();
await app.WaitForShutdownAsync();
return 0;
