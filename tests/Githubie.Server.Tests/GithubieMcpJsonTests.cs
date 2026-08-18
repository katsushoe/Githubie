using FluentAssertions;
using Xunit;

namespace Githubie.Server.Tests;

public sealed class GithubieMcpJsonTests
{
    [Fact]
    public void CreateOptions_SetsTypeInfoResolver()
    {
        // 実機検証で発見した回帰防止: TypeInfoResolver未設定だと
        // WithTools<T>()呼び出し時にJsonSerializerOptionsのreadonly化で例外になる。
        var options = GithubieMcpJson.CreateOptions();

        options.TypeInfoResolver.Should().NotBeNull();
    }

    [Fact]
    public void CreateOptions_UsesSnakeCaseLowerNamingPolicy()
    {
        var options = GithubieMcpJson.CreateOptions();

        options.PropertyNamingPolicy.Should().Be(System.Text.Json.JsonNamingPolicy.SnakeCaseLower);
    }
}
