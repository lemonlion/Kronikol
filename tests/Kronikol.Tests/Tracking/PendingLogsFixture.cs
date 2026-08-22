using Kronikol.Tracking;

namespace Kronikol.Tests.Tracking;

/// <summary>
/// Empties the pending-log queue around the shared-capture-state collection
/// (<see cref="Kronikol.Tests.DiagramsFetcherCollection"/>), which is where every test that touches it runs.
/// </summary>
public class PendingLogsFixture : IDisposable
{
    public PendingLogsFixture() => PendingRequestResponseLogs.Clear();
    public void Dispose() => PendingRequestResponseLogs.Clear();
}
