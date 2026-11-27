using FluentAssertions;
using Githubie.Application.Publishing;
using Githubie.Infrastructure.Repositories;
using Xunit;

namespace Githubie.Infrastructure.Tests;

public sealed class SqlitePrPublicationStoreTests : IDisposable
{
    private readonly string root = Directory.CreateTempSubdirectory("githubie-publication-store-").FullName;
    private CancellationToken Ct => TestContext.Current.CancellationToken;
    private SqlitePrPublicationStore Store() => new(Path.Combine(root, "publication.db"));
    private static PrPublicationRecord Record(string id, string publication = "unknown", string pr = "not_attempted", string source = "work/topic") =>
        new("request-hash", "binding", new(new(id, source, "review-target", new string('a', 40), publication, pr), "result_unknown", "unknown"));

    [Fact]
    public async Task ReopenedStore_PreservesRecordAndCaseInsensitiveRepository()
    {
        var first = Store();
        await first.InitializeAsync(Ct);
        string id = Guid.NewGuid().ToString("D");
        var record = Record(id);
        await first.PutAsync("Sample", id, record, Ct);
        var reopened = Store();
        await reopened.InitializeAsync(Ct);
        (await reopened.GetAsync("sample", id, Ct)).Should().Be(record);
        (await reopened.GetAsync("other", id, Ct)).Should().BeNull();
    }

    [Theory]
    [InlineData("unknown", "not_attempted", true)]
    [InlineData("created", "unknown", true)]
    [InlineData("created", "failed", false)]
    [InlineData("already_present", "created", false)]
    public async Task UnresolvedQuery_UsesBothStatesAndExactSource(string publication, string pr, bool unresolved)
    {
        var store = Store();
        await store.InitializeAsync(Ct);
        string id = Guid.NewGuid().ToString("D");
        await store.PutAsync("sample", id, Record(id, publication, pr), Ct);
        (await store.HasUnresolvedAsync("SAMPLE", "work/topic", Ct)).Should().Be(unresolved);
        (await store.HasUnresolvedAsync("other", "work/topic", Ct)).Should().BeFalse();
        (await store.HasUnresolvedAsync("sample", "work/Topic", Ct)).Should().BeFalse();
    }

    [Fact]
    public async Task Reconciliation_UpdatesSameOperationWithoutLeavingUnknown()
    {
        var store = Store();
        await store.InitializeAsync(Ct);
        string id = Guid.NewGuid().ToString("D");
        await store.PutAsync("sample", id, Record(id), Ct);
        var resolved = Record(id, "already_present", "already_present");
        await store.PutAsync("SAMPLE", id, resolved, Ct);
        (await Store().GetAsync("sample", id, Ct)).Should().Be(resolved);
        (await Store().HasUnresolvedAsync("sample", "work/topic", Ct)).Should().BeFalse();
    }

    [Fact]
    public async Task RepositoryLease_IsExclusiveAcrossInstancesAndReleasedOnDispose()
    {
        var first = Store();
        var second = Store();
        var lease = await first.TryAcquireAsync("Sample", Ct);
        lease.Should().NotBeNull();
        try
        {
            (await second.TryAcquireAsync("sample", Ct)).Should().BeNull();
            await using var other = await second.TryAcquireAsync("other", Ct);
            other.Should().NotBeNull();
        }
        finally { await lease!.DisposeAsync(); }
        await using var reacquired = await second.TryAcquireAsync("sample", Ct);
        reacquired.Should().NotBeNull();
    }

    [Fact]
    public async Task CompletedOperations_ReleaseDatabaseWithoutClearingProcessWidePools()
    {
        var store = Store();
        await store.InitializeAsync(Ct);
        string id = Guid.NewGuid().ToString("D");
        await store.PutAsync("sample", id, Record(id), Ct);
        (await store.GetAsync("sample", id, Ct)).Should().NotBeNull();
        (await store.HasUnresolvedAsync("sample", "work/topic", Ct)).Should().BeTrue();

        using var exclusive = new FileStream(Path.Combine(root, "publication.db"),
            FileMode.Open, FileAccess.ReadWrite, FileShare.None);
        exclusive.Length.Should().BeGreaterThan(0);
    }

    public void Dispose()
    {
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        string expectedParent = Path.TrimEndingDirectorySeparator(Path.GetFullPath(Path.GetTempPath()));
        Path.GetDirectoryName(Path.GetFullPath(root)).Should().Be(expectedParent);
        Path.GetFileName(root).Should().StartWith("githubie-publication-store-");
        Directory.Delete(root, recursive: true);
    }
}
