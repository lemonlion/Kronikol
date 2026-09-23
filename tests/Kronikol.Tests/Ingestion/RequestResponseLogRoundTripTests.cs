using System.Net;
using System.Reflection;
using Kronikol.Constants;
using Kronikol.Ingestion;
using Kronikol.Tracking;

namespace Kronikol.Tests.Ingestion;

/// <summary>
/// The member guard of <c>plans/INGEST_FEED_PLAN.md</c> (§3, T2). Every public instance member of
/// <see cref="RequestResponseLog"/> sits in exactly one of the sets below, and the round trip through the
/// NDJSON writer and reader (<see cref="InteractionRecord.FromLog"/> → <see cref="InteractionRecord.ToJson"/>
/// → <see cref="InteractionRecord.FromJson"/> → <see cref="InteractionRecord.ToLogs"/>) does to each member
/// what its set says. A member added to the log without a row here fails the build, so the feed cannot
/// start losing something new in silence; a pinned gap that begins to round-trip fails too, so the ledger
/// is moved rather than left stale.
/// </summary>
public class RequestResponseLogRoundTripTests
{
    /// <summary>Written by the writer and read back equal.</summary>
    private static readonly string[] Carried =
    [
        nameof(RequestResponseLog.ActivitySpanId),
        nameof(RequestResponseLog.ActivityTraceId),
        nameof(RequestResponseLog.CallerDependencyCategory),
        nameof(RequestResponseLog.CallerName),
        nameof(RequestResponseLog.CapturedBy),
        nameof(RequestResponseLog.Content),
        nameof(RequestResponseLog.DependencyCategory),
        // #94: dropped by FromLog until 3.27.4, so a measured duration was replaced by the timestamp delta.
        nameof(RequestResponseLog.DurationMs),
        nameof(RequestResponseLog.Headers),
        nameof(RequestResponseLog.IsUserAction), // as kind: ui
        nameof(RequestResponseLog.MetaType), // Default is written as absent and read back as Default
        nameof(RequestResponseLog.Method),
        nameof(RequestResponseLog.Phase), // Unknown is written as absent and read back as Unknown
        nameof(RequestResponseLog.RequestResponseId),
        nameof(RequestResponseLog.ServiceName),
        nameof(RequestResponseLog.StatusCode), // an HttpStatusCode as its number, a custom label as text
        nameof(RequestResponseLog.TestId),
        nameof(RequestResponseLog.TestName),
        nameof(RequestResponseLog.Timestamp),
        nameof(RequestResponseLog.TraceId),
        nameof(RequestResponseLog.TrackingIgnore),
        nameof(RequestResponseLog.Type),
        nameof(RequestResponseLog.Uri),
    ];

    /// <summary>Never on the wire, by design. Not asserted either way.</summary>
    private static readonly Dictionary<string, string> ExcludedByDesign = new()
    {
        [nameof(RequestResponseLog.CollapsedCount)] = "set by the diagram pipeline from adjacent pairs at render time, never by a capturer; the store never holds it",
        [nameof(RequestResponseLog.CollapsedSummary)] = "the collapsed run's label, computed with CollapsedCount at render time",
        [nameof(RequestResponseLog.IsDiagramMarker)] = "derived from IsOverrideStart, IsOverrideEnd and IsActionStart; it follows them",
    };

    /// <summary>
    /// Lost by the writer today and pinned as such; each feeds roadmap item 14.1, the round-trip gate
    /// (plan §3.2). Asserted lost, so closing one of them is a deliberate move of its row.
    /// </summary>
    private static readonly Dictionary<string, string> KnownGaps = new()
    {
        [nameof(RequestResponseLog.Error)] = "evidence of a failed send (3.18.0), which httpInteractions[].error already exposes; a new contract field, so 14.1",
        [nameof(RequestResponseLog.AttributionSource)] = "provenance of how a call got its scenario: meaningless to an external capturer and not recomputable at replay; 14.1",
        [nameof(RequestResponseLog.ExpiredFromTestId)] = "the same provenance; 14.1",
        [nameof(RequestResponseLog.FocusFields)] = "author-supplied rendering intent, the note's focus; 14.1",
        [nameof(RequestResponseLog.NoteOnRight)] = "author-supplied rendering intent, the note's side; 14.1",
        [nameof(RequestResponseLog.SetupVariant)] = "the capturing extension's verbosity rules, not re-derivable at replay without its options; 14.1",
        [nameof(RequestResponseLog.ActionVariant)] = "the same rules for the Action phase; 14.1",
    };

    /// <summary>
    /// The members that make a log a diagram marker. Every marker log is written as the same junk request
    /// line today (plan F2); S2 of the plan (3.28.0) writes one <c>kind: marker</c> record per override half
    /// and moves these five to <see cref="Carried"/>. Asserted lost on the marker probes until then.
    /// </summary>
    private static readonly Dictionary<string, string> LostUntilMarkersAreRecords = new()
    {
        [nameof(RequestResponseLog.IsOverrideStart)] = "the opening half of an override pair",
        [nameof(RequestResponseLog.IsOverrideEnd)] = "the closing half",
        [nameof(RequestResponseLog.IsActionStart)] = "the Setup/Action boundary",
        [nameof(RequestResponseLog.MarkerKind)] = "what the marker stands for; annotations[].kind and step attribution read it",
        [nameof(RequestResponseLog.PlantUml)] = "the fragment the opening half splices into the diagram",
    };

    private static readonly DateTimeOffset T0 = new(2026, 9, 22, 10, 0, 0, TimeSpan.Zero);
    private const string TestId = "0af7651916cd43dd8448eb211c80319c";

    [Fact]
    public void Every_public_member_of_the_log_is_in_exactly_one_set()
    {
        var members = PublicMembers();
        var classified = Carried
            .Concat(ExcludedByDesign.Keys)
            .Concat(KnownGaps.Keys)
            .Concat(LostUntilMarkersAreRecords.Keys)
            .ToArray();

        var unclassified = members.Except(classified).ToArray();
        Assert.True(unclassified.Length == 0,
            $"RequestResponseLog member(s) in no set of the feed's member guard: {string.Join(", ", unclassified)}. "
            + "Decide what the NDJSON writer does with each and add it to Carried, ExcludedByDesign or KnownGaps (plans/INGEST_FEED_PLAN.md §3).");

        var duplicated = classified.GroupBy(m => m).Where(g => g.Count() > 1).Select(g => g.Key).ToArray();
        Assert.True(duplicated.Length == 0, "Member(s) in more than one set: " + string.Join(", ", duplicated));

        var unknown = classified.Except(members).ToArray();
        Assert.True(unknown.Length == 0, "Named by the guard but not a public member of RequestResponseLog: " + string.Join(", ", unknown));
    }

    [Fact]
    public void The_probe_sets_every_carried_and_pinned_member_so_equality_is_never_vacuous()
    {
        var log = FullyPopulatedInteraction();
        var unset = Carried.Concat(KnownGaps.Keys).Where(m => IsDefault(Get(log, m))).ToArray();
        Assert.True(unset.Length == 0, "Probe member(s) left at their default: " + string.Join(", ", unset));
    }

    [Fact]
    public void Carried_members_survive_the_writer_and_the_reader()
    {
        var log = FullyPopulatedInteraction();
        var back = RoundTrip(log);

        var lost = Carried
            .Where(m => !Same(Get(log, m), Get(back, m)))
            .Select(m => $"{m}: {Show(Get(log, m))} came back as {Show(Get(back, m))}")
            .ToArray();

        Assert.True(lost.Length == 0, "Carried member(s) lost by FromLog → JSON → ToLogs:\n" + string.Join("\n", lost));
    }

    [Fact]
    public void Known_gaps_are_still_lost_so_the_ledger_moves_when_one_closes()
    {
        var log = FullyPopulatedInteraction();
        var back = RoundTrip(log);

        var closed = KnownGaps.Keys.Where(m => Same(Get(log, m), Get(back, m))).ToArray();

        Assert.True(closed.Length == 0,
            "KnownGaps member(s) now round-trip. Move each to Carried and strike its row from plans/INGEST_FEED_PLAN.md §3.2: "
            + string.Join(", ", closed));
    }

    [Fact]
    public void Marker_members_are_lost_until_a_marker_is_written_as_a_record()
    {
        var probes = MarkerProbes();
        var unset = LostUntilMarkersAreRecords.Keys.Where(m => probes.All(p => IsDefault(Get(p, m)))).ToArray();
        Assert.True(unset.Length == 0, "No marker probe sets: " + string.Join(", ", unset));

        var carried = LostUntilMarkersAreRecords.Keys
            .Where(m => probes.All(p => Same(Get(p, m), Get(RoundTrip(p), m))))
            .ToArray();

        Assert.True(carried.Length == 0,
            "Marker member(s) now round-trip. Move each to Carried and retire this fact (plans/INGEST_FEED_PLAN.md S2): "
            + string.Join(", ", carried));
    }

    // ─── probes ────────────────────────────────────────────────

    /// <summary>Every member a capturer or the store can set, non-default, on one response log.</summary>
    private static RequestResponseLog FullyPopulatedInteraction() =>
        new("Overview renders", TestId, HttpMethod.Post, """{"data":{}}""",
            new Uri("http://localhost:8081/sidekick?x=1"), [("Content-Type", "application/json"), ("X-Empty", null)],
            "graphql", "web", RequestResponseType.Response,
            Guid.Parse("0af76519-16cd-43dd-8448-eb211c80319c"), Guid.Parse("6f1c0d8e-9b7a-4c2e-8e1a-2d3f4b5c6d7e"),
            true, HttpStatusCode.Created, RequestResponseMetaType.Event, DependencyCategories.AI, DependencyCategories.User)
        {
            NoteOnRight = true,
            PlantUml = null,
            FocusFields = ["data", "errors"],
            Timestamp = T0.AddMilliseconds(2050),
            AttributionSource = Kronikol.Tracking.AttributionSource.TestContext,
            ExpiredFromTestId = "1bf7651916cd43dd8448eb211c80319d",
            Error = "HttpRequestException: boom",
            ActivitySpanId = "b7ad6b7169203331",
            ActivityTraceId = "0af7651916cd43dd8448eb211c80319c",
            Phase = TestPhase.Action,
            SetupVariant = new PhaseVariant(HttpMethod.Post, new Uri("http://localhost:8081/sidekick"), "{}", [], false),
            ActionVariant = new PhaseVariant(HttpMethod.Post, new Uri("http://localhost:8081/sidekick"), """{"data":{}}""", [], true),
            CollapsedCount = 3,
            CollapsedSummary = "12–48 ms",
            IsUserAction = true,
            CapturedBy = "wire",
            DurationMs = 123.4,
        };

    /// <summary>The three shapes DefaultTrackingDiagramOverride emits: an opening half with its fragment, a closing half, the phase boundary.</summary>
    private static RequestResponseLog[] MarkerProbes() =>
    [
        Marker(start: true, DiagramMarkerKind.Row, "\nhnote across #lightyellow : Row 3\n\n", T0.AddMilliseconds(3000)),
        Marker(start: false, DiagramMarkerKind.Row, null, T0.AddMilliseconds(3001)),
        new("Overview renders", TestId, "", "", new Uri("http://override.com"), [], "", "", RequestResponseType.Request,
            Guid.NewGuid(), Guid.NewGuid(), false) { IsActionStart = true, MarkerKind = DiagramMarkerKind.Phase, Timestamp = T0.AddMilliseconds(3500) },
    ];

    private static RequestResponseLog Marker(bool start, DiagramMarkerKind kind, string? plantUml, DateTimeOffset at) =>
        new("Overview renders", TestId, "", "", new Uri("http://override.com"), [], "", "", RequestResponseType.Request,
            Guid.NewGuid(), Guid.NewGuid(), false)
        {
            IsOverrideStart = start,
            IsOverrideEnd = !start,
            MarkerKind = kind,
            PlantUml = plantUml,
            Timestamp = at,
        };

    // ─── the round trip and the comparison ─────────────────────

    private static RequestResponseLog RoundTrip(RequestResponseLog log) =>
        InteractionRecord.FromJson(InteractionRecord.FromLog(log).ToJson()).ToLogs().Single();

    private static string[] PublicMembers() =>
        typeof(RequestResponseLog)
            .GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Select(p => p.Name)
            .OrderBy(n => n, StringComparer.Ordinal)
            .ToArray();

    private static object? Get(RequestResponseLog log, string member) =>
        typeof(RequestResponseLog).GetProperty(member, BindingFlags.Public | BindingFlags.Instance)!.GetValue(log);

    private static bool Same(object? a, object? b)
    {
        if (a is null || b is null)
            return a is null && b is null;
        if (a.GetType().IsGenericType && a.GetType().GetGenericTypeDefinition() == typeof(OneOf<,>))
            return Same(a.GetType().GetProperty("Value")!.GetValue(a), b.GetType().GetProperty("Value")!.GetValue(b));
        if (a is Array x && b is Array y)
            return x.Length == y.Length && x.Cast<object?>().Zip(y.Cast<object?>()).All(pair => Same(pair.First, pair.Second));
        return a.Equals(b);
    }

    private static bool IsDefault(object? value) => value switch
    {
        null => true,
        bool b => !b,
        int i => i == 0,
        string s => s.Length == 0,
        Array a => a.Length == 0,
        Enum e => Convert.ToInt32(e) == 0,
        _ => false,
    };

    private static string Show(object? value) => value switch
    {
        null => "null",
        Array a => "[" + string.Join(", ", a.Cast<object?>().Select(Show)) + "]",
        string s => "\"" + s.Replace("\n", "\\n") + "\"",
        _ when value.GetType().IsGenericType && value.GetType().GetGenericTypeDefinition() == typeof(OneOf<,>)
            => Show(value.GetType().GetProperty("Value")!.GetValue(value)),
        _ => value.ToString() ?? "null",
    };
}
