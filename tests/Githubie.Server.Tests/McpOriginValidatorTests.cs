using FluentAssertions;
using Xunit;

namespace Githubie.Server.Tests;

public sealed class McpOriginValidatorTests
{
    [Fact]
    public void IsAllowed_AllowsMissingOrigin()
    {
        McpOriginValidator.IsAllowed(null, 45460).Should().BeTrue();
        McpOriginValidator.IsAllowed(string.Empty, 45460).Should().BeTrue();
    }

    [Fact]
    public void IsAllowed_AllowsLoopbackWithMatchingPort()
    {
        McpOriginValidator.IsAllowed("http://127.0.0.1:45460", 45460).Should().BeTrue();
        McpOriginValidator.IsAllowed("http://localhost:45460", 45460).Should().BeTrue();
    }

    [Theory]
    [InlineData("http://127.0.0.1:9999")]
    [InlineData("https://127.0.0.1:45460")]
    [InlineData("http://example.com:45460")]
    [InlineData("http://127.0.0.1:45460?x=1")]
    [InlineData("not-a-url")]
    public void IsAllowed_RejectsUnexpectedOrigins(string origin)
    {
        McpOriginValidator.IsAllowed(origin, 45460).Should().BeFalse();
    }
}
