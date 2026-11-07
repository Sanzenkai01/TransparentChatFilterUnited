using TransparentChatFilter;
using Xunit;

namespace TransparentChatFilter.Tests;

public sealed class BoundedAsyncCacheTests
{
    [Fact]
    public async Task SharesConcurrentInFlightFactory()
    {
        var cache = new BoundedAsyncCache<string, int>(8, StringComparer.Ordinal);
        var factoryCalls = 0;
        var gate = new TaskCompletionSource<int>(TaskCreationOptions.RunContinuationsAsynchronously);

        var requests = Enumerable.Range(0, 8)
            .Select(_ => cache.GetOrCreateAsync(
                "key",
                () =>
                {
                    Interlocked.Increment(ref factoryCalls);
                    return gate.Task;
                },
                CancellationToken.None))
            .ToArray();

        gate.SetResult(42);
        var results = await Task.WhenAll(requests);

        Assert.All(results, result => Assert.Equal(42, result));
        Assert.Equal(1, factoryCalls);
    }

    [Fact]
    public async Task EvictsLeastRecentlyUsedEntryWhenCapacityIsExceeded()
    {
        var cache = new BoundedAsyncCache<string, int>(2, StringComparer.Ordinal);
        var factoryCalls = new Dictionary<string, int>(StringComparer.Ordinal);

        Task<int> Get(string key) => cache.GetOrCreateAsync(
            key,
            () =>
            {
                factoryCalls[key] = factoryCalls.GetValueOrDefault(key) + 1;
                return Task.FromResult(factoryCalls[key]);
            },
            CancellationToken.None);

        await Get("a");
        await Get("b");
        await Get("a");
        await Get("c");
        await Get("b");

        Assert.Equal(1, factoryCalls["a"]);
        Assert.Equal(2, factoryCalls["b"]);
        Assert.Equal(1, factoryCalls["c"]);
    }

    [Fact]
    public async Task ShrinkingCapacityImmediatelyEvictsLeastRecentlyUsedEntries()
    {
        var cache = new BoundedAsyncCache<string, int>(4, StringComparer.Ordinal);
        await cache.GetOrCreateAsync("a", () => Task.FromResult(1), CancellationToken.None);
        await cache.GetOrCreateAsync("b", () => Task.FromResult(2), CancellationToken.None);
        await cache.GetOrCreateAsync("c", () => Task.FromResult(3), CancellationToken.None);
        await cache.GetOrCreateAsync("d", () => Task.FromResult(4), CancellationToken.None);
        await cache.GetOrCreateAsync("b", () => Task.FromResult(20), CancellationToken.None);

        cache.SetCapacity(2);

        Assert.Equal(2, cache.Count);
        Assert.Equal(2, await cache.GetOrCreateAsync("b", () => Task.FromResult(20), CancellationToken.None));
        Assert.Equal(10, await cache.GetOrCreateAsync("a", () => Task.FromResult(10), CancellationToken.None));
    }

    [Fact]
    public async Task CachesFailuresBrieflyToAvoidRepeatedRequests()
    {
        var cache = new BoundedAsyncCache<string, int>(2, StringComparer.Ordinal);
        var factoryCalls = 0;
        Task<int> Fail()
        {
            Interlocked.Increment(ref factoryCalls);
            return Task.FromException<int>(new HttpRequestException("temporary"));
        }

        await Assert.ThrowsAsync<HttpRequestException>(() =>
            cache.GetOrCreateAsync("key", Fail, CancellationToken.None));
        await Assert.ThrowsAsync<HttpRequestException>(() =>
            cache.GetOrCreateAsync("key", Fail, CancellationToken.None));

        Assert.Equal(1, factoryCalls);
    }
}
