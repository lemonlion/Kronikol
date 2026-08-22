using Kronikol.Tests.Tracking;

namespace Kronikol.Tests;

/// <summary>
/// Everything that touches the process-wide capture state runs here, one class at a time.
/// </summary>
/// <remarks>
/// <see cref="Kronikol.Tracking.RequestResponseLogger"/>, the diagram cache and the pending-log queue are
/// static: a test that replays a run clears the store, and a test in another collection reading its own
/// logs at that moment finds them gone. Serialising them is the only way to keep that honest — the
/// alternative is a suite that fails a different test every few runs.
/// </remarks>
[CollectionDefinition("DiagramsFetcher")]
// One collection for every global the tests share — the capture store (PendingLogsFixture) AND the
// tracking-component registry: two collections ran in parallel and each cleared the other's state.
public class DiagramsFetcherCollection : ICollectionFixture<PendingLogsFixture>, ICollectionFixture<TrackingComponentRegistryFixture>, ICollectionFixture<Kronikol.Tests.Tracking.TestIdentityScopeFixture>;
