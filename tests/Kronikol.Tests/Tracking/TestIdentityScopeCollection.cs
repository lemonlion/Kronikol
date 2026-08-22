using Kronikol.Tracking;

namespace Kronikol.Tests.Tracking;

// The identity-scope fixture is served by the "DiagramsFetcher" collection (DiagramsFetcherCollection.cs):
// Track.That / StepCollector / TestIdentityScope all write the same process-global tracking store as the
// report and ingest tests, and two collections running in parallel cleared each other's state.

public class TestIdentityScopeFixture : IDisposable
{
    public TestIdentityScopeFixture()
    {
        TestIdentityScope.Reset();
        TestIdentityScope.ClearGlobalFallback();
    }

    public void Dispose()
    {
        TestIdentityScope.Reset();
        TestIdentityScope.ClearGlobalFallback();
    }
}
