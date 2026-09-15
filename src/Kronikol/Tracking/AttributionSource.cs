namespace Kronikol.Tracking;

/// <summary>
/// How a captured call got its scenario. Recorded on every <see cref="RequestResponseLog"/> so the
/// report can tell a call the scenario made from one that merely inherited its context — a web host
/// started inside a test carries the test's identity into every hosted service it runs, and until this
/// was recorded their work landed in that scenario with nothing to say so.
/// </summary>
public enum AttributionSource
{
    /// <summary>The scenario's own request carried it in the test-tracking headers.</summary>
    RequestHeader,

    /// <summary>The test framework's ambient context: the test itself, or work that inherited its context.</summary>
    TestContext,

    /// <summary>An explicit <see cref="TestIdentityScope.Begin"/> scope, or an identity a message carried.</summary>
    Scope,

    /// <summary><see cref="TestIdentityScope.GlobalFallback"/>.</summary>
    GlobalFallback,

    /// <summary>Inside a detached flow (<see cref="TestIdentityScope.Detach"/>, a detached hosted service): no scenario, by design.</summary>
    Detached,

    /// <summary>Nothing resolved.</summary>
    None,

    /// <summary>Resolved from a test context after that scenario had ended: background, marked when the report is written.</summary>
    Expired,

    /// <summary>
    /// Nothing named a scenario, but the call named a document a scenario had written earlier in the run:
    /// attributed to that scenario, the document's last attributed writer, for this one call (3.19.0). A
    /// detached hosted service claiming, retrying and failing an outbox row lands where the row was seeded.
    /// </summary>
    DocumentOwner,

    /// <summary>
    /// Nothing named a scenario, but the detached flow had just written a document a scenario owns, and this
    /// call came between that write and the flow's next operation on the document (3.20.0): the dispatch
    /// between the claim of an outbox row and its status update. Held until that operation confirmed it; see
    /// <see cref="DocumentOwnership"/>.
    /// </summary>
    DocumentFlow
}
