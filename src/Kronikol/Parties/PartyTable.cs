using Kronikol.Reports;

namespace Kronikol.Parties;

/// <summary>
/// A run's parties: one row a participant, sorted by name, and what the checks found. The findings are the report's
/// diagnostics only when the run declared something, so a report that declares nothing keeps its bytes; the
/// <c>parties</c> verb prints them as notes either way.
/// </summary>
internal sealed record PartyTable(IReadOnlyList<Party> Parties, string? ServiceId, bool Declared, IReadOnlyList<DiagnosticEntry> Findings)
{
    private const string TestContext = nameof(Kronikol.Tracking.AttributionSource.TestContext);
    private const string RequestHeader = nameof(Kronikol.Tracking.AttributionSource.RequestHeader);

    internal IReadOnlyList<DiagnosticEntry> Diagnostics => Declared ? Findings : [];

    internal IReadOnlyList<DiagnosticEntry> Notes => Findings;

    /// <summary>
    /// The table of a report already written, and the subject the report is about (#192 R3): the report's <c>parties</c>
    /// block as written when it has one, else the table its run would have inferred from its calls, those outside every
    /// scenario included, with nothing declared, since a report without the block declared nothing. The subject is the
    /// subject whose id is the service id, else the one that received the most calls, then the first by name. One
    /// function, so the query engine and the service card (#191) agree on who each party is.
    /// </summary>
    internal static (PartyTable Table, string? Subject) OfReport(IReadOnlyList<Party>? written, string? serviceId, IReadOnlyCollection<PartyCall> calls)
    {
        var table = written is not null
            ? new PartyTable(written, serviceId, Declared: true, [])
            : PartyResolver.Resolve(calls, PartyDeclarations.None, PartyObservations.None);
        var received = calls.GroupBy(c => c.Service, StringComparer.Ordinal).ToDictionary(g => g.Key, g => g.Count(), StringComparer.Ordinal);
        var subjects = table.Parties.Where(p => p.Kind == PartyKind.UnderTest).ToList();
        var subject = (serviceId is null ? null : subjects.FirstOrDefault(p => string.Equals(p.Id, serviceId, StringComparison.Ordinal)))
                      ?? subjects.OrderByDescending(p => received.GetValueOrDefault(p.Name)).ThenBy(p => p.Name, StringComparer.Ordinal).FirstOrDefault();
        return (table, subject?.Name);
    }

    /// <summary>
    /// Who made a call, as a role (#192 R3, plans/CALL_OPERATION_AND_ORIGIN_PLAN.md 4.3): <c>test</c> when its caller is a
    /// test party, <c>subject</c> when the caller is a subject, and <c>other</c> for any other party or a name the table
    /// does not hold.
    /// </summary>
    internal string OriginOf(string caller) =>
        KindOf(caller) switch
        {
            PartyKind.Test => "test",
            PartyKind.UnderTest => "subject",
            _ => "other"
        };

    /// <summary>
    /// Why a call was made (#192 R3, plans/SERVICE_CARD_PLAN.md 4.2), the first answer that applies: <c>test</c> when its
    /// caller is a test party; <c>background</c> when it ran outside every scenario; <c>test</c> when a subject made it on
    /// the test's context and no party is the test, the subject running in the test's own process; by its attribution when
    /// it has one, <c>request</c> for one its scenario came to in a request header and <c>background</c> for any other (a
    /// scope, a message, the test's context reaching a hosted service or a delivery, a document's owner); and with none,
    /// <c>request</c> when the flow nesting puts it inside another call (<paramref name="insideReceivedCall"/>: one its
    /// caller was serving, or the publish a delivered message came from), else <c>unknown</c>.
    /// </summary>
    internal string CauseOf(string caller, string? attributionSource, bool inBackgroundBlock, bool insideReceivedCall)
    {
        var kind = KindOf(caller);
        if (kind == PartyKind.Test)
            return "test";
        if (inBackgroundBlock)
            return "background";
        if (kind == PartyKind.UnderTest && attributionSource == TestContext && !Parties.Any(p => p.Kind == PartyKind.Test))
            return "test";
        if (!string.IsNullOrEmpty(attributionSource))
            return attributionSource == RequestHeader ? "request" : "background";
        return insideReceivedCall ? "request" : "unknown";
    }

    // Read in place rather than through a lookup kept on the record: a `with` would carry a stale one along.
    private PartyKind? KindOf(string caller) => Parties.FirstOrDefault(p => string.Equals(p.Name, caller, StringComparison.Ordinal))?.Kind;
}
