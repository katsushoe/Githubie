using System.Diagnostics;
using FluentAssertions;
using Githubie.Infrastructure.Git;
using Xunit;

namespace Githubie.Infrastructure.Tests;

public sealed class GitCommandClientIntegrationTests
{
    [Theory]
    [InlineData("git@github.com:katsushoe/Shiori.git")]
    [InlineData("https://github.com/katsushoe/Shiori.git")]
    public async Task GetRemoteUrlAsync_RealGit_ReturnsConfiguredGitHubUrl(string remoteUrl)
    {
        using var repository = await TemporaryGitRepository.CreateAsync(remoteUrl);
        var client = new GitCommandClient(new ProcessExecutor(), "unused-askpass.exe");

        var result = await client.GetRemoteUrlAsync(repository.Root, "origin", TestContext.Current.CancellationToken);

        result.IsSuccess.Should().BeTrue(result.StandardError);
        result.StandardOutput.Should().Be(remoteUrl);
    }

    [Fact]
    public async Task GetRemoteUrlAsync_RealGit_MissingRemoteReturnsFailedResult()
    {
        using var repository = await TemporaryGitRepository.CreateAsync("https://github.com/katsushoe/Shiori.git");
        var client = new GitCommandClient(new ProcessExecutor(), "unused-askpass.exe");

        var result = await client.GetRemoteUrlAsync(repository.Root, "missing", TestContext.Current.CancellationToken);

        result.IsSuccess.Should().BeFalse();
        result.StandardError.Should().Contain("missing");
    }

    private sealed class TemporaryGitRepository : IDisposable
    {
        private TemporaryGitRepository(string root) => Root = root;

        public string Root { get; }

        public static async Task<TemporaryGitRepository> CreateAsync(string remoteUrl)
        {
            var root = Directory.CreateTempSubdirectory("githubie-git-").FullName;
            await RunGitAsync(root, "init");
            await RunGitAsync(root, "remote", "add", "origin", remoteUrl);
            return new TemporaryGitRepository(root);
        }

        public void Dispose() => Directory.Delete(Root, recursive: true);

        private static async Task RunGitAsync(string root, params string[] arguments)
        {
            var startInfo = new ProcessStartInfo
            {
                FileName = "git",
                WorkingDirectory = root,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true,
            };
            foreach (var argument in arguments)
                startInfo.ArgumentList.Add(argument);

            using var process = Process.Start(startInfo) ?? throw new InvalidOperationException("Failed to start git.");
            var standardError = await process.StandardError.ReadToEndAsync();
            await process.WaitForExitAsync();
            if (process.ExitCode != 0)
                throw new InvalidOperationException($"git failed: {standardError}");
        }
    }
}
