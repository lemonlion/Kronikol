using System.Collections.Concurrent;

namespace Kronikol.Parties;

/// <summary>
/// The test clients <c>CreateTestTrackingClient</c> built in this process, and whom each called (#190, O1). Their
/// caller is the test and their callees the service under test, both observed, so <see cref="ReportConfigurationOptions.ServiceId"/>
/// alone is safe even where the calls' shape would mislead the inference: a renamed test client, or a service whose
/// trackers were left at the test's name. Filled from tests running in parallel; a run is one process, so nothing in a
/// consumer's path clears it.
/// </summary>
internal static class TestClientRegistry
{
    private static ConcurrentQueue<TestClientRecord> _clients = new();

    internal static TestClientRecord Register(string? caller, string? application)
    {
        var record = new TestClientRecord(caller, application);
        _clients.Enqueue(record);
        return record;
    }

    /// <summary>The clients that made a call, each with whom it called.</summary>
    internal static PartyObservations Observations() =>
        new(_clients.Where(c => c.Caller is not null && !c.Callees.IsEmpty)
            .Select(c => new TestClientObservation(c.Caller!, c.Callees.Keys.Order(StringComparer.Ordinal).ToArray(), c.Application))
            .ToList());

    /// <summary>Forgets every client. Only Kronikol's own tests call it, beside <see cref="Tracking.TrackingComponentRegistry.Clear"/>.</summary>
    internal static void Clear() => Interlocked.Exchange(ref _clients, new());
}

/// <summary>One test client: the caller name its handler writes, the application it ran, and the names its calls went to.</summary>
internal sealed class TestClientRecord(string? caller, string? application)
{
    internal string? Caller { get; } = caller;

    internal string? Application { get; } = application;

    internal ConcurrentDictionary<string, byte> Callees { get; } = new(StringComparer.Ordinal);

    internal void Saw(string callee)
    {
        if (!string.IsNullOrEmpty(callee))
            Callees.TryAdd(callee, 0);
    }
}
