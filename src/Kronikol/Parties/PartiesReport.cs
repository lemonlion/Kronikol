using System.Xml.Linq;
using Kronikol.Tracking;

namespace Kronikol.Parties;

/// <summary>
/// A run's parties as the report carries them (#190): resolved once from the run's calls, its options, the parties
/// file and the test clients Kronikol built, and written as <c>serviceId</c> and <c>parties</c> in each form of the
/// data file, only when the run declared something.
///
/// <para>The service card never calls this: it reads the block the report carries, else infers over the report's calls
/// (<see cref="CallGraph.GraphParties"/>), so the card a run writes and the one <c>kronikol card</c> writes from its data
/// file are the same. What the run observed of its test clients reaches the card only through the block, that is only
/// when the run declares something (plans/SERVICE_CARD_PLAN.md, Q15).</para>
/// </summary>
internal static class PartiesReport
{
    /// <summary>
    /// The run's table: the calls its report carries, what it and its parties file declare, and what Kronikol observed.
    /// A call whose test id names none of the run's scenarios is in no scenario and not in the background, so the report
    /// leaves it out, and its names are no parties of the report either.
    /// </summary>
    internal static PartyTable Resolve(ReportConfigurationOptions options, IReadOnlyList<Reports.Feature> features, IEnumerable<RequestResponseLog> logs, string reportsDirectory,
        Func<string, string?>? getEnv = null, string? baseDirectory = null)
    {
        var path = Locate(options, reportsDirectory, getEnv, baseDirectory);
        var (rows, findings) = path is null ? ([], []) : PartiesFile.Read(path);
        var declarations = new PartyDeclarations(options.ServiceId?.Trim(), [.. options.Parties ?? []], rows, findings);
        var carried = new HashSet<string>(features.SelectMany(f => f.Scenarios ?? []).Select(s => s.Id), StringComparer.Ordinal) { TestIdentityScope.UnknownTestId };
        return PartyResolver.Resolve(CallsOf(logs.Where(l => l is not null && carried.Contains(l.TestId))), declarations, TestClientRegistry.Observations());
    }

    /// <summary>
    /// The run's parties file. <paramref name="baseDirectory"/> is where it is looked for first, then above the reports
    /// directory: an ingest passes the directory it runs in, as it does for the history ledger, and a test run its own
    /// base directory.
    /// </summary>
    private static string? Locate(ReportConfigurationOptions options, string reportsDirectory, Func<string, string?>? getEnv, string? baseDirectory) =>
        PartiesFile.Locate(options.PartiesFilePath, reportsDirectory, getEnv ?? System.Environment.GetEnvironmentVariable, baseDirectory ?? AppContext.BaseDirectory);

    /// <summary>One call a pair: the request of each, diagram markers left out, as the data file's interactions list them.</summary>
    internal static IEnumerable<PartyCall> CallsOf(IEnumerable<RequestResponseLog> logs) =>
        logs.Where(l => l is not null && l.Type == RequestResponseType.Request && !l.IsDiagramMarker)
            .Select(l => new PartyCall(l.CallerName, l.ServiceName, l.DependencyCategory, l.CallerDependencyCategory, l.AttributionSource?.ToString(), l.TestDouble));

    /// <summary>A kind as the data file writes it: <c>test</c>, <c>underTest</c>, <c>real</c>, <c>double</c>, <c>unknown</c>.</summary>
    internal static string KindName(PartyKind kind) => kind switch
    {
        PartyKind.Test => "test",
        PartyKind.UnderTest => "underTest",
        PartyKind.Real => "real",
        PartyKind.Double => "double",
        _ => "unknown"
    };

    /// <summary>How a kind became known, as the data file writes it: <c>declared</c>, <c>observed</c>, <c>inferred</c>.</summary>
    internal static string SourceName(PartyKindSource source) => source switch
    {
        PartyKindSource.Declared => "declared",
        PartyKindSource.Observed => "observed",
        _ => "inferred"
    };

    internal static bool TryParseSource(string? text, out PartyKindSource source)
    {
        foreach (var candidate in Enum.GetValues<PartyKindSource>())
        {
            if (string.Equals(text, SourceName(candidate), StringComparison.OrdinalIgnoreCase))
            {
                source = candidate;
                return true;
            }
        }
        source = default;
        return false;
    }

    /// <summary>The rows for the JSON data file: sorted by name, members that hold nothing left out, and no arrays inside a row.</summary>
    internal static List<Dictionary<string, object?>> Json(PartyTable table) =>
        table.Parties.Select(party =>
        {
            var row = new Dictionary<string, object?> { ["name"] = party.Name };
            if (party.Id is not null) row["id"] = party.Id;
            row["kind"] = KindName(party.Kind);
            if (party.KindSource is { } source) row["kindSource"] = SourceName(source);
            if (party.Note is not null) row["note"] = party.Note;
            return row;
        }).ToList();

    /// <summary>The block for the XML data file, in its own idiom: <c>ServiceId</c>, then <c>Parties</c> of <c>Party</c> elements.</summary>
    internal static IEnumerable<XElement> Xml(PartyTable? table)
    {
        if (table is not { Declared: true })
            yield break;
        if (table.ServiceId is { } serviceId)
            yield return new XElement("ServiceId", serviceId);
        yield return new XElement("Parties", table.Parties.Select(party => new XElement("Party",
            new XElement("Name", party.Name),
            party.Id is null ? null : new XElement("Id", party.Id),
            new XElement("Kind", KindName(party.Kind)),
            party.KindSource is { } source ? new XElement("KindSource", SourceName(source)) : null,
            party.Note is null ? null : new XElement("Note", party.Note))));
    }
}
