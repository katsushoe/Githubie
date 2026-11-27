using System.Text;
using Githubie.Cli;

Console.OutputEncoding = Encoding.UTF8;

try { return await CliApplication.RunAsync(args, Console.Out, Console.Error, CancellationToken.None); }
catch (OperationCanceledException) { Console.Error.WriteLine("CANCELLED"); return 1; }
catch (Exception exception)
{
    Console.Error.WriteLine($"Githubie CLI failed ({exception.GetType().Name}). Check host logs.");
    return 1;
}
