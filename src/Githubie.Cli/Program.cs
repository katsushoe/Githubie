using System.Text;
using Githubie.Cli;

Console.OutputEncoding = Encoding.UTF8;

return await CliApplication.RunAsync(args, Console.Out, Console.Error, CancellationToken.None);
