using FluentAssertions;
using Githubie.Infrastructure.Git;
using Xunit;

namespace Githubie.Infrastructure.Tests;

public sealed class ProcessExecutorTests
{
    [Fact]
    public async Task ExecuteAsync_OutputStartsWithSpace_PreservesLeadingWhitespace()
    {
        var executor = new ProcessExecutor();

        var result = await executor.ExecuteAsync(
            Environment.CurrentDirectory,
            "powershell.exe",
            ["-NoProfile", "-Command", "[Console]::Out.Write(' M file.txt' + [Environment]::NewLine)"],
            null,
            TimeSpan.FromSeconds(10),
            TestContext.Current.CancellationToken);

        result.IsSuccess.Should().BeTrue();
        result.StandardOutput.Should().Be(" M file.txt");
    }
}
