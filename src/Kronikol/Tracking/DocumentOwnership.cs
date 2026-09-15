namespace Kronikol.Tracking;

/// <summary>What a document operation is to <see cref="DocumentOwnership"/>.</summary>
public enum DocumentOperationKind
{
    /// <summary>A read of one document: a point read, a find by id.</summary>
    Read,

    /// <summary>
    /// A write of one document: a create, upsert, replace, patch or delete; an insert, update,
    /// findAndModify or delete by id. The document may not be known yet (a create names it in the body).
    /// </summary>
    Write,

    /// <summary>A query or a listing, which names no document: the poll.</summary>
    Query
}

/// <summary>
/// A document a scenario wrote is the scenario's. The database trackers record every attributed write in
/// <see cref="TestCorrelationStore"/> (keyed by store and document id, the same keys the change-feed and
/// change-stream decorators read). When a later document operation resolves no scenario at all, a detached
/// hosted service claiming, retrying and failing an outbox row the scenario seeded, the operation is
/// attributed to the document's last attributed writer, for that one call, with the provenance
/// <see cref="AttributionSource.DocumentOwner"/> (3.19.0, plans/DOCUMENT_OWNERSHIP_PLAN.md).
/// </summary>
/// <remarks>
/// <para><b>The owner window (3.20.0, plans/OWNER_WINDOW_AND_COUNT_CONFIRMATION_PLAN.md).</b> A detached
/// flow that wrote a scenario's document is doing that scenario's work: the dispatch call between the claim
/// of an outbox row and its status update names no document, and would otherwise be nobody's. After such a
/// write the flow keeps the owner (<see cref="TestIdentityScope.OwnerWindow"/>, answered by
/// <see cref="TestInfoResolver"/> with <see cref="AttributionSource.DocumentFlow"/>) until its next operation
/// on a document that is not the owner's, or a query, which names no document. What the flow captured under
/// the window is held back rather than recorded: the next operation on the owner's document confirms it, and
/// a foreign operation or a query drops it (kept as background when <see cref="RequestResponseLogger.CaptureBackground"/>
/// is on). Two workers that interleave in one flow therefore never hand one scenario's call to another: a
/// call is attributed only when the same document closes it out with nothing else between. A read confirms
/// a window and never opens one, so a poller reading a scenario's document does not make everything between
/// its reads the scenario's; an operation on a document nobody owns, or on one not yet known, leaves the
/// window as it is. Windows exist only in detached flows (<see cref="TestIdentityScope.Detach"/>, which
/// <c>DetachHostedServicesFromTestIdentity</c> establishes for every hosted service); elsewhere ownership
/// stays per call. A call attributed by ownership after its owner ended expires like an inherited one.</para>
/// <para>A tracker calls <see cref="ForOperation"/> with what the resolver answered before the operation,
/// and <see cref="AfterWrite"/> with the identity it logged once a write succeeded.</para>
/// </remarks>
public static class DocumentOwnership
{
    /// <summary>
    /// The owner of the document a call named, when the call resolved no scenario. Null when the call already
    /// has a scenario, when it named no document, or when nobody wrote the document under a scenario. Per call;
    /// the window is neither consulted nor touched (see <see cref="ForOperation"/>).
    /// </summary>
    /// <param name="resolved">What the resolver answered for the call: null, or a background identity.</param>
    /// <param name="correlationKey">The store's key for the document, or null when the call named none.</param>
    public static TestIdentity? Resolve(TestIdentity? resolved, string? correlationKey)
    {
        if (resolved is { IsAttributed: true } || correlationKey is null)
            return null;
        return Owner(correlationKey) is { } owner
            ? new TestIdentity(owner.Name, owner.Id, AttributionSource.DocumentOwner)
            : null;
    }

    /// <summary>
    /// The identity a document operation is captured under, and the flow's owner window kept up to date
    /// (see the type's remarks). A call with a scenario of its own keeps it, and the window is neither
    /// consulted nor touched. Otherwise: a query ends the window; an operation on a document nobody owns
    /// leaves it; an operation on the window's own document confirms what the window held; one on another
    /// scenario's document ends it. The operation itself is the document's owner's, per call, when the
    /// document has one, and nobody's otherwise: null, or the background identity when background is captured.
    /// </summary>
    /// <param name="resolved">What <see cref="TestInfoResolver.ResolveWithSource(Microsoft.AspNetCore.Http.IHttpContextAccessor?, Func{ValueTuple{string, string}?}?)"/> answered for the call.</param>
    /// <param name="correlationKey">The store's key for the document the operation names, or null when it names none or the document is not yet known.</param>
    /// <param name="kind">Whether the operation reads, writes, or queries.</param>
    public static TestIdentity? ForOperation(TestIdentity? resolved, string? correlationKey, DocumentOperationKind kind)
    {
        if (resolved is { IsAttributed: true, Source: not AttributionSource.DocumentFlow })
            return resolved;

        var flow = TestIdentityScope.CurrentFlow;
        if (kind == DocumentOperationKind.Query)
        {
            flow?.Close();
            return Nobody(resolved);
        }

        var owner = correlationKey is null ? null : Owner(correlationKey);
        if (owner is null)
            return Nobody(resolved);

        if (flow?.Window is { } window)
        {
            if (string.Equals(window.Id, owner.Value.Id, StringComparison.Ordinal))
                flow.Confirm();
            else
                flow.Close();
        }
        return new TestIdentity(owner.Value.Name, owner.Value.Id, AttributionSource.DocumentOwner);
    }

    /// <summary>
    /// A write succeeded under the given identity. When the write was attributed by ownership, the flow is
    /// doing the owner's work from here: the window opens for the owner (or stays open, when it already was).
    /// Nothing happens for a write the flow made under a scenario of its own, or outside a detached flow.
    /// </summary>
    public static void AfterWrite(TestIdentity identity)
    {
        if (identity.Source == AttributionSource.DocumentOwner)
            TestIdentityScope.CurrentFlow?.Open(identity.Name, identity.Id);
    }

    /// <summary>Report time: what no operation has confirmed by now is nobody's for this report.</summary>
    internal static void Settle() => DetachedFlow.SettleAll();

    /// <summary>The document's owner, when a scenario wrote it. The unknown identity is no owner.</summary>
    private static (string Name, string Id)? Owner(string correlationKey) =>
        TestCorrelationStore.Lookup(correlationKey) is { } owner
        && !string.Equals(owner.Id, TestIdentityScope.UnknownTestId, StringComparison.OrdinalIgnoreCase)
            ? owner
            : null;

    /// <summary>
    /// What the operation is when it is nobody's: what the resolver said when that was nothing attributed;
    /// when the window answered, what the resolver would have said without it.
    /// </summary>
    private static TestIdentity? Nobody(TestIdentity? resolved) =>
        resolved is { Source: AttributionSource.DocumentFlow }
            ? TestInfoResolver.Background(AttributionSource.Detached)
            : resolved;
}
