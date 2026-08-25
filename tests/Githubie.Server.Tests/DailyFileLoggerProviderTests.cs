using FluentAssertions;
using Microsoft.Extensions.Logging;
using Xunit;

namespace Githubie.Server.Tests;

public sealed class DailyFileLoggerProviderTests
{
    [Fact]
    public void Logger_WritesDailyLogFile()
    {
        var directory = Path.Combine(Path.GetTempPath(), $"githubie-logger-{Guid.NewGuid():N}");
        try
        {
            using var provider = new DailyFileLoggerProvider(directory);
            provider.CreateLogger("test").LogInformation("hello");

            var path = Path.Combine(directory, $"githubie-{DateTime.UtcNow:yyyyMMdd}.log");
            File.ReadAllText(path).Should().Contain("hello");
        }
        finally
        {
            if (Directory.Exists(directory)) Directory.Delete(directory, true);
        }
    }

    [Fact]
    public void Logger_WriteFailure_DoesNotEscapeToCaller()
    {
        var file = Path.Combine(Path.GetTempPath(), $"githubie-logger-blocker-{Guid.NewGuid():N}");
        File.WriteAllText(file, "not a directory");
        try
        {
            using var provider = new DailyFileLoggerProvider(file);
            var act = () => provider.CreateLogger("test").LogInformation("must not throw");

            act.Should().NotThrow();
        }
        finally
        {
            File.Delete(file);
        }
    }
}
