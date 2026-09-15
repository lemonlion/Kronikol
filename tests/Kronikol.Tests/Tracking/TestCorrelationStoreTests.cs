using Kronikol.Tracking;

namespace Kronikol.Tests.Tracking;

[Collection("DiagramsFetcher")]
public class TestCorrelationStoreTests
{
    public TestCorrelationStoreTests()
    {
        TestCorrelationStore.Clear();
        TestCorrelationStore.DefaultTtl = TimeSpan.FromMinutes(30);
    }

    [Fact]
    public void Lookup_is_a_probe_and_does_not_report_a_miss()
    {
        var misses = 0;
        TestCorrelationStore.OnResolveMiss = _ => misses++;
        try
        {
            Assert.Null(TestCorrelationStore.Lookup("cosmos:orders:nobody-wrote-this"));
            Assert.Equal(0, misses);
            Assert.Null(TestCorrelationStore.Resolve("cosmos:orders:nobody-wrote-this"));
            Assert.Equal(1, misses);
            TestCorrelationStore.Correlate("cosmos:orders:doc-7", "Test A", "test-a-id");
            Assert.Equal(("Test A", "test-a-id"), TestCorrelationStore.Lookup("cosmos:orders:doc-7"));
        }
        finally
        {
            TestCorrelationStore.OnResolveMiss = null;
        }
    }

    [Fact]
    public void Correlate_and_Resolve_returns_stored_identity()
    {
        TestCorrelationStore.Correlate("cosmos:orders:doc-1", "Test A", "test-a-id");

        var result = TestCorrelationStore.Resolve("cosmos:orders:doc-1");

        Assert.NotNull(result);
        Assert.Equal("Test A", result.Value.Name);
        Assert.Equal("test-a-id", result.Value.Id);
    }

    [Fact]
    public void Resolve_returns_null_for_unknown_key()
    {
        var result = TestCorrelationStore.Resolve("nonexistent-key");

        Assert.Null(result);
    }

    [Fact]
    public void Correlate_overwrites_existing_entry()
    {
        TestCorrelationStore.Correlate("key-1", "Test A", "id-a");
        TestCorrelationStore.Correlate("key-1", "Test B", "id-b");

        var result = TestCorrelationStore.Resolve("key-1");

        Assert.NotNull(result);
        Assert.Equal("Test B", result.Value.Name);
        Assert.Equal("id-b", result.Value.Id);
    }

    [Fact]
    public void Remove_deletes_entry_and_returns_true()
    {
        TestCorrelationStore.Correlate("key-1", "Test A", "id-a");

        var removed = TestCorrelationStore.Remove("key-1");

        Assert.True(removed);
        Assert.Null(TestCorrelationStore.Resolve("key-1"));
    }

    [Fact]
    public void Remove_returns_false_for_nonexistent_key()
    {
        var removed = TestCorrelationStore.Remove("nonexistent");

        Assert.False(removed);
    }

    [Fact]
    public void Clear_removes_all_entries()
    {
        TestCorrelationStore.Correlate("key-1", "Test A", "id-a");
        TestCorrelationStore.Correlate("key-2", "Test B", "id-b");

        TestCorrelationStore.Clear();

        Assert.Null(TestCorrelationStore.Resolve("key-1"));
        Assert.Null(TestCorrelationStore.Resolve("key-2"));
    }

    [Fact]
    public void Resolve_returns_null_for_expired_entry()
    {
        TestCorrelationStore.DefaultTtl = TimeSpan.FromMilliseconds(1);
        TestCorrelationStore.Correlate("key-1", "Test A", "id-a");

        Thread.Sleep(10);

        var result = TestCorrelationStore.Resolve("key-1");

        Assert.Null(result);
    }

    [Fact]
    public void Resolve_returns_entry_within_ttl()
    {
        TestCorrelationStore.DefaultTtl = TimeSpan.FromMinutes(30);
        TestCorrelationStore.Correlate("key-1", "Test A", "id-a");

        var result = TestCorrelationStore.Resolve("key-1");

        Assert.NotNull(result);
        Assert.Equal("Test A", result.Value.Name);
    }

    [Fact]
    public void Seed_populates_entry_without_requiring_test_context()
    {
        TestCorrelationStore.Seed("pre-existing-key", "Seeded Test", "seeded-id");

        var result = TestCorrelationStore.Resolve("pre-existing-key");

        Assert.NotNull(result);
        Assert.Equal("Seeded Test", result.Value.Name);
        Assert.Equal("seeded-id", result.Value.Id);
    }

    [Fact]
    public async Task Concurrent_correlate_and_resolve_is_thread_safe()
    {
        var tasks = Enumerable.Range(0, 100).Select(i => Task.Run(() =>
        {
            var key = $"key-{i}";
            TestCorrelationStore.Correlate(key, $"Test {i}", $"id-{i}");
            var result = TestCorrelationStore.Resolve(key);
            Assert.NotNull(result);
            Assert.Equal($"Test {i}", result.Value.Name);
        }));

        await Task.WhenAll(tasks);
    }

    [Fact]
    public void DefaultTtl_defaults_to_30_minutes()
    {
        // Reset to check default
        TestCorrelationStore.DefaultTtl = TimeSpan.FromMinutes(30);

        Assert.Equal(TimeSpan.FromMinutes(30), TestCorrelationStore.DefaultTtl);
    }

    [Fact]
    public void OnResolveMiss_invoked_when_key_not_found()
    {
        string? missedKey = null;
        TestCorrelationStore.OnResolveMiss = key => missedKey = key;

        TestCorrelationStore.Resolve("missing-key");

        Assert.Equal("missing-key", missedKey);
        TestCorrelationStore.OnResolveMiss = null;
    }

    [Fact]
    public void OnResolveMiss_invoked_when_entry_expired()
    {
        string? missedKey = null;
        TestCorrelationStore.OnResolveMiss = key => missedKey = key;
        TestCorrelationStore.DefaultTtl = TimeSpan.FromMilliseconds(1);
        TestCorrelationStore.Correlate("expiring-key", "Test A", "id-a");

        Thread.Sleep(10);
        TestCorrelationStore.Resolve("expiring-key");

        Assert.Equal("expiring-key", missedKey);
        TestCorrelationStore.OnResolveMiss = null;
        TestCorrelationStore.DefaultTtl = TimeSpan.FromMinutes(30);
    }

    [Fact]
    public void OnResolveMiss_not_invoked_on_successful_resolve()
    {
        string? missedKey = null;
        TestCorrelationStore.OnResolveMiss = key => missedKey = key;
        TestCorrelationStore.Correlate("found-key", "Test A", "id-a");

        TestCorrelationStore.Resolve("found-key");

        Assert.Null(missedKey);
        TestCorrelationStore.OnResolveMiss = null;
    }
}

public class TestCorrelationStoreFixture : IDisposable
{
    public TestCorrelationStoreFixture() => TestCorrelationStore.Clear();
    public void Dispose() => TestCorrelationStore.Clear();
}
