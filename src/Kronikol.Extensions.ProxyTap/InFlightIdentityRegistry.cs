using System.Collections.Concurrent;

namespace Kronikol.Extensions.ProxyTap;

/// <summary>
/// Who is being served, right now, by each service a <see cref="ProxyTap"/> fronts.
/// </summary>
/// <remarks>
/// <para>
/// A tap that sits on an HTTP hop reads the test identity straight off the request headers. A tap that
/// sits on a <em>database</em> connection cannot: Redis and MongoDB wire protocols have nowhere to put
/// a <c>test-tracking-current-test-id</c>, and the connection is pooled besides. The identity has to be
/// inferred, and the only honest source is "which test was the service handling when the query left it".
/// </para>
/// <para>
/// This registry is that source. An HTTP tap in the same process registers each request against the
/// service it forwards to for as long as it is in flight, and a database tap asks
/// <see cref="MostRecentFor"/> which identity that service is currently working for. With one worker
/// (the common shape for an end-to-end suite) the answer is exact; with several it is a best guess, which
/// is why the whole mechanism is opt-in — set <see cref="ProxyTapOptions.InFlightRegistry"/> to switch it
/// on. Ingest-time window attribution
/// (<c>Kronikol.Ingestion.IngestRequest.AttributeByTestWindow</c>) is the deterministic alternative and
/// needs no coupling between taps at all.
/// </para>
/// <para>Thread-safe: taps register and release from their own request threads.</para>
/// </remarks>
public sealed class InFlightIdentityRegistry
{
    /// <summary>One request a service is handling.</summary>
    /// <param name="Name">The test's display name.</param>
    /// <param name="Id">The test id, as it will appear on the interaction records.</param>
    /// <param name="Since">When the request started (UTC).</param>
    public sealed record InFlightIdentity(string Name, string Id, DateTimeOffset Since);

    private readonly ConcurrentDictionary<string, ConcurrentDictionary<long, InFlightIdentity>> _byService =
        new(StringComparer.OrdinalIgnoreCase);

    private long _nextTicket;

    /// <summary>
    /// The process-wide registry, for hosts that would rather not thread an instance through their tap
    /// construction. Nothing writes to it unless a <see cref="ProxyTapOptions.InFlightRegistry"/> points at it.
    /// </summary>
    public static InFlightIdentityRegistry Shared { get; } = new();

    /// <summary>
    /// Records that <paramref name="serviceName"/> has started handling a request for
    /// (<paramref name="testName"/>, <paramref name="testId"/>). Dispose the returned handle when the
    /// request completes — the tap does this in a <c>finally</c>, so a failed forward cannot leave a
    /// stale identity behind.
    /// </summary>
    public IDisposable Register(string serviceName, string testName, string testId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(serviceName);

        var ticket = Interlocked.Increment(ref _nextTicket);
        var forService = _byService.GetOrAdd(serviceName, _ => new ConcurrentDictionary<long, InFlightIdentity>());
        forService[ticket] = new InFlightIdentity(testName, testId, DateTimeOffset.UtcNow);
        return new Registration(forService, ticket);
    }

    /// <summary>
    /// The identity of the most recently started request still in flight on <paramref name="serviceName"/>,
    /// or null when that service is idle. "Most recent" is the right answer because a call a service makes
    /// belongs to the request it is currently handling, and nested work always starts after its cause.
    /// </summary>
    public InFlightIdentity? MostRecentFor(string? serviceName)
    {
        if (string.IsNullOrWhiteSpace(serviceName) || !_byService.TryGetValue(serviceName, out var forService))
            return null;

        InFlightIdentity? best = null;
        foreach (var candidate in forService.Values)
        {
            if (best is null || candidate.Since >= best.Since)
                best = candidate;
        }

        return best;
    }

    /// <summary>How many requests <paramref name="serviceName"/> is handling right now (diagnostics).</summary>
    public int CountFor(string? serviceName) =>
        string.IsNullOrWhiteSpace(serviceName) || !_byService.TryGetValue(serviceName, out var forService)
            ? 0
            : forService.Count;

    /// <summary>The services that currently have at least one request in flight.</summary>
    public IReadOnlyCollection<string> ActiveServices =>
        _byService.Where(kvp => !kvp.Value.IsEmpty).Select(kvp => kvp.Key).ToArray();

    /// <summary>Forgets everything — for tests, and for a host restarting its taps.</summary>
    public void Clear() => _byService.Clear();

    private sealed class Registration(ConcurrentDictionary<long, InFlightIdentityRegistry.InFlightIdentity> forService, long ticket)
        : IDisposable
    {
        private int _disposed;

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _disposed, 1) == 0)
                forService.TryRemove(ticket, out _);
        }
    }
}
