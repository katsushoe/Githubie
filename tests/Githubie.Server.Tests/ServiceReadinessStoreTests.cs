using FluentAssertions;
using Xunit;

namespace Githubie.Server.Tests;

public sealed class ServiceReadinessStoreTests
{
    [Fact]
    public async Task WaitForReadyAsync_InitializingThenReady_WaitsAndSucceeds()
    {
        var root = CreateRoot();
        try
        {
            var store = new ServiceReadinessStore(Path.Combine(root, "service-state.json"));
            await store.WriteInitializingAsync(TestContext.Current.CancellationToken);

            var wait = store.WaitForReadyAsync(
                TimeSpan.FromSeconds(2),
                TimeSpan.FromMilliseconds(20),
                TestContext.Current.CancellationToken);
            await Task.Delay(100, TestContext.Current.CancellationToken);
            wait.IsCompleted.Should().BeFalse();
            await store.WriteReadyAsync(TestContext.Current.CancellationToken);

            var result = await wait;
            result.IsReady.Should().BeTrue();
            result.Error.Should().BeNull();
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }

    [Fact]
    public async Task WriteReadyAsync_WhenReaderHoldsFile_RetriesUntilReaderCloses()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var root = CreateRoot();
        try
        {
            var statePath = Path.Combine(root, "service-state.json");
            var store = new ServiceReadinessStore(statePath);
            await store.WriteInitializingAsync(cancellationToken);

            Task write;
            using (File.Open(statePath, FileMode.Open, FileAccess.Read, FileShare.Read | FileShare.Delete))
            {
                write = store.WriteReadyAsync(cancellationToken);
                await Task.Delay(100, cancellationToken);
            }
            await write;

            var state = await store.WaitForReadyAsync(TimeSpan.FromSeconds(1), TimeSpan.FromMilliseconds(20), cancellationToken);
            state.IsReady.Should().BeTrue();
            Directory.GetFiles(root).Should().ContainSingle().Which.Should().Be(statePath);
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }

    [Fact]
    public async Task WaitForReadyAsync_FailedState_ReturnsFailureReason()
    {
        var root = CreateRoot();
        try
        {
            var store = new ServiceReadinessStore(Path.Combine(root, "service-state.json"));
            await store.WriteFailedAsync("test failure", TestContext.Current.CancellationToken);

            var result = await store.WaitForReadyAsync(
                TimeSpan.FromSeconds(1),
                TimeSpan.FromMilliseconds(20),
                TestContext.Current.CancellationToken);

            result.IsReady.Should().BeFalse();
            result.Error.Should().Be("test failure");
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }

    [Fact]
    public async Task WaitForReadyAsync_MissingState_TimesOut()
    {
        var root = CreateRoot();
        try
        {
            var store = new ServiceReadinessStore(Path.Combine(root, "service-state.json"));

            var result = await store.WaitForReadyAsync(
                TimeSpan.FromMilliseconds(100),
                TimeSpan.FromMilliseconds(20),
                TestContext.Current.CancellationToken);

            result.IsReady.Should().BeFalse();
            result.Error.Should().Be("service readiness timed out");
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }

    private static string CreateRoot()
    {
        var root = Path.Combine(Path.GetTempPath(), $"githubie-readiness-{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);
        return root;
    }
}
