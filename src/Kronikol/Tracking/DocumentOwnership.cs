namespace Kronikol.Tracking;

/// <summary>
/// A document a scenario wrote is the scenario's. The database trackers record every attributed write in
/// <see cref="TestCorrelationStore"/> (keyed by store and document id, the same keys the change-feed and
/// change-stream decorators read). When a later document operation resolves no scenario at all, a detached
/// hosted service claiming, retrying and failing an outbox row the scenario seeded, the operation is
/// attributed to the document's last attributed writer, for that one call, with the provenance
/// <see cref="AttributionSource.DocumentOwner"/>. Nothing ambient is established, so the poll before the
/// claim stays the host's; a call that already has a scenario is never re-attributed; and a call attributed
/// this way after its owner ended expires like an inherited one (plans/DOCUMENT_OWNERSHIP_PLAN.md).
/// </summary>
public static class DocumentOwnership
{
    /// <summary>
    /// The owner of the document a call named, when the call resolved no scenario. Null when the call already
    /// has a scenario, when it named no document, or when nobody wrote the document under a scenario.
    /// </summary>
    /// <param name="resolved">What the resolver answered for the call: null, or a background identity.</param>
    /// <param name="correlationKey">The store's key for the document, or null when the call named none.</param>
    public static TestIdentity? Resolve(TestIdentity? resolved, string? correlationKey)
    {
        if (resolved is { IsAttributed: true } || correlationKey is null)
            return null;
        return TestCorrelationStore.Lookup(correlationKey) is { } owner
            ? new TestIdentity(owner.Name, owner.Id, AttributionSource.DocumentOwner)
            : null;
    }
}
