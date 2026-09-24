using FluentAssertions;
using Githubie.Application.Configuration;
using Githubie.Application.Credentials;
using Githubie.Cli;
using Xunit;

namespace Githubie.Cli.Tests;

public sealed class AuthSetTests
{
    private static RepositoryOptions Repository => new("owner", "repo", ".", "origin", "develop", "main",
        ["develop"], ["develop"], ["main"], "main", "^v", "merge", true);
    [Theory]
    [InlineData(false, true)]
    [InlineData(true, true)]
    [InlineData(false, false)]
    [InlineData(true, false)]
    public async Task RunAsync_AuthSet_SelectsInputAndClearsBuffer(bool console, bool saveSucceeds)
    {
        var token = "test-secret-value".ToCharArray();
        var guiCalls = 0;
        var consoleCalls = 0;
        var dependencies = new AuthSetDependencies(
            (_, _) => Task.FromResult<RepositoryOptions?>(Repository),
            (request, _) =>
            {
                guiCalls++;
                request.ProjectName.Should().Be("sample");
                request.RepositoryUrl.Should().Be("https://github.com/owner/repo");
                return Task.FromResult<char[]?>(token);
            },
            () => { consoleCalls++; return token; },
            (repository, value) =>
            {
                repository.Should().Be("sample");
                new string(value).Should().Be("test-secret-value");
                return saveSucceeds ? ApiTokenStoreResult.Success() : ApiTokenStoreResult.Failure(ApiTokenStoreError.IoError);
            });
        using var output = new StringWriter();
        using var error = new StringWriter();

        var exit = await CliApplication.RunAsync(
            console ? ["auth", "set", "sample", "--console"] : ["auth", "set", "sample"],
            output, error, TestContext.Current.CancellationToken, dependencies);

        exit.Should().Be(saveSucceeds ? 0 : 1);
        guiCalls.Should().Be(console ? 0 : 1);
        consoleCalls.Should().Be(console ? 1 : 0);
        token.Should().OnlyContain(value => value == '\0');
        (output.ToString() + error).Should().NotContain("test-secret-value");
        output.ToString().Should().Contain(saveSucceeds ? "[OK]" : "[SAVE_FAILED]");
    }

    [Theory]
    [InlineData("cancel", "[CANCELLED]")]
    [InlineData("launch", "[DIALOG_FAILED]")]
    [InlineData("save", "[SAVE_FAILED]")]
    public async Task RunAsync_GuiFailure_ReportsDistinctResultWithoutSecret(string failure, string expected)
    {
        var token = "test-secret-value".ToCharArray();
        var saves = 0;
        var dependencies = new AuthSetDependencies(
            (_, _) => Task.FromResult<RepositoryOptions?>(Repository),
            (_, _) => failure switch
            {
                "cancel" => Task.FromResult<char[]?>(null),
                "launch" => throw new FileNotFoundException("test-secret-value"),
                _ => Task.FromResult<char[]?>(token),
            },
            () => throw new InvalidOperationException("Console must not be selected"),
            (_, _) => { saves++; throw new System.Security.Cryptography.CryptographicException("test-secret-value"); });
        using var output = new StringWriter();
        using var error = new StringWriter();

        var exit = await CliApplication.RunAsync(["auth", "set", "sample"], output, error,
            TestContext.Current.CancellationToken, dependencies);

        exit.Should().Be(1);
        (output.ToString() + error).Should().Contain(expected).And.NotContain("test-secret-value");
        saves.Should().Be(failure == "save" ? 1 : 0);
        if (failure == "save") token.Should().OnlyContain(value => value == '\0');
    }

    [Fact]
    public async Task RequestAsync_MissingExecutable_IsNotCancellation()
    {
        var client = new TokenPromptClient(Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"), "missing.exe"));
        var action = () => client.RequestAsync(new("sample", "https://github.com/owner/repo"), TestContext.Current.CancellationToken);
        await action.Should().ThrowAsync<FileNotFoundException>();
    }
}
