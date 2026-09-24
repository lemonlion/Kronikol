using Kronikol.Tracking;

namespace Kronikol.Tests.Tracking;

[Collection("DiagramsFetcher")]
public class CorrelatedProcessingScopeTests : IDisposable
{
    public CorrelatedProcessingScopeTests()
    {
        TestCorrelationStore.Clear();
        TestIdentityScope.Reset();
    }

    // The TTL is process-wide: restored whatever a fact did to it, or an assertion that fails before the
    // fact's own restore leaves every later correlation in the collection expiring in a millisecond.
    public void Dispose() => TestCorrelationStore.DefaultTtl = TimeSpan.FromMinutes(30);

    [Fact]
    public void Begin_sets_TestIdentityScope_from_correlation_store()
    {
        TestCorrelationStore.Correlate("key-1", "Test A", "id-a");

        using var scope = CorrelatedProcessingScope.Begin("key-1");

        Assert.NotNull(scope);
        Assert.NotNull(TestIdentityScope.Current);
        Assert.Equal("Test A", TestIdentityScope.Current.Value.Name);
        Assert.Equal("id-a", TestIdentityScope.Current.Value.Id);
    }

    [Fact]
    public void Begin_returns_null_when_key_not_found()
    {
        var scope = CorrelatedProcessingScope.Begin("nonexistent-key");

        Assert.Null(scope);
        Assert.Null(TestIdentityScope.Current);
    }

    [Fact]
    public void Dispose_restores_previous_identity()
    {
        TestCorrelationStore.Correlate("key-1", "Test A", "id-a");

        using (TestIdentityScope.Begin("Outer", "outer-id"))
        {
            using (CorrelatedProcessingScope.Begin("key-1"))
            {
                Assert.Equal("Test A", TestIdentityScope.Current!.Value.Name);
            }

            Assert.Equal("Outer", TestIdentityScope.Current!.Value.Name);
        }
    }

    [Fact]
    public void Begin_returns_null_for_expired_entry()
    {
        TestCorrelationStore.DefaultTtl = TimeSpan.FromMilliseconds(1);
        TestCorrelationStore.Correlate("key-1", "Test A", "id-a");

        Thread.Sleep(10);

        var scope = CorrelatedProcessingScope.Begin("key-1");

        Assert.Null(scope);
        Assert.Null(TestIdentityScope.Current);

        TestCorrelationStore.DefaultTtl = TimeSpan.FromMinutes(30);
    }

    [Fact]
    public async Task Begin_works_across_async_flow()
    {
        TestCorrelationStore.Correlate("key-1", "Async Test", "async-id");

        using var scope = CorrelatedProcessingScope.Begin("key-1");

        await Task.Yield();

        Assert.NotNull(TestIdentityScope.Current);
        Assert.Equal("Async Test", TestIdentityScope.Current.Value.Name);
    }
}
