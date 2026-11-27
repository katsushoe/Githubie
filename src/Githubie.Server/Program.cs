using Githubie.Server;

try
{
    var hostArguments = args.Contains("--user-host", StringComparer.OrdinalIgnoreCase) ? args : ["--user-host", .. args];
    return await UserHostApplication.RunAsync(hostArguments);
}
catch (OperationCanceledException) { return 0; }
catch (Exception exception)
{
    Console.Error.WriteLine($"Githubie could not start: {exception.GetType().Name}");
    return 1;
}
