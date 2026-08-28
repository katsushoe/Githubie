using FluentAssertions;
using Githubie.Application.Git;
using Xunit;

namespace Githubie.Application.Tests;

public sealed class GitErrorDiagnosticTests
{
    [Fact]
    public void Sanitize_RedactsCredentialsAndKeepsFailureReason()
    {
        const string standardError =
            "fatal: unable to access 'https://user:secret@github.com/owner/repo.git': token=ghp_abcdefghijklmnopqrstuvwxyz123456";

        var result = GitErrorDiagnostic.Sanitize(standardError);

        result.Should().Be("fatal: unable to access 'https://[REDACTED]@github.com/owner/repo.git': token=[REDACTED]");
    }

    [Fact]
    public void Sanitize_TruncatesLongDiagnostic()
    {
        var result = GitErrorDiagnostic.Sanitize(new string('x', 3000));

        result.Should().HaveLength(2051).And.EndWith("...");
    }

    [Fact]
    public void Sanitize_EmptyStandardError_ReturnsNull()
    {
        GitErrorDiagnostic.Sanitize(" ").Should().BeNull();
    }
}
