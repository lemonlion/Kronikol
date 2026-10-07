using System.Text.RegularExpressions;
using Kronikol.Constants;
using Kronikol.History;
using Kronikol.Tracking;

namespace Kronikol.Reports;

/// <summary>
/// One call the run's first-call warm-up slowed down (plans/WARM_UP_PLAN.md, #113): the run's first call of its shape
/// (<see cref="FirstKind"/>), or a call that started while that one ran and waited for it (<see cref="WaitedKind"/>).
/// </summary>
/// <param name="Kind"><see cref="FirstKind"/> or <see cref="WaitedKind"/>.</param>
/// <param name="Shape">The method and the templated path (and, for a statement-shaped dependency, the templated statement
/// head), as in <c>POST /orders/{id}</c>. The service is the call's own.</param>
/// <param name="BaselineMs">The median duration of the shape's calls that started after the first one ended.</param>
/// <param name="BaselineCalls">How many calls that median is over.</param>
/// <param name="First">For a call that waited, the <see cref="RequestResponseLog.RequestResponseId"/> of the run's first
/// call of its shape; null for the first call itself.</param>
/// <param name="DurationMs">The call's own duration, as the data files write it.</param>
/// <param name="ScenarioId">The scenario the call belongs to.</param>
internal sealed record WarmUpMark(string Kind, string Shape, double BaselineMs, int BaselineCalls, Guid? First,
    double DurationMs = 0, string ScenarioId = "")
{
    /// <summary>The run's first call of its shape.</summary>
    public const string FirstKind = "first";

    /// <summary>A call that started while the run's first call of its shape ran, and waited for it.</summary>
    public const string WaitedKind = "waited";
}

/// <summary>What <see cref="WarmUpCalls.Find"/> found: the marked calls, by their request's
/// <see cref="RequestResponseLog.RequestResponseId"/>, and each scenario's warm-up in milliseconds, by scenario id. A
/// scenario with no warm-up has no entry.</summary>
internal sealed record WarmUpResult(IReadOnlyDictionary<Guid, WarmUpMark> Calls, IReadOnlyDictionary<string, double> ScenarioMs)
{
    /// <summary>Nothing marked.</summary>
    public static readonly WarmUpResult None = new(new Dictionary<Guid, WarmUpMark>(), new Dictionary<string, double>(StringComparer.Ordinal));
}

/// <summary>
/// Finds the calls a run's first-call warm-up slowed down (plans/WARM_UP_PLAN.md 4.1, #113). The first request down a
/// path can cost tens of times what later ones do (JIT, a schema build, a first connection), and whichever scenario goes
/// first pays it. For each shape of call a test makes (service, method and templated path), the run's first call is a
/// warm-up when it took at least <see cref="Ratio"/> times and <see cref="FloorMs"/> more than the median of the shape's
/// calls that started after it ended, and so is a call that waited for it. The rule's executable spec is
/// <c>plans/WARM_UP_PLAN.harness/warmup.py</c> (<c>marks</c>), which this must equal call for call.
/// </summary>
internal static partial class WarmUpCalls
{
    /// <summary>How many times the later calls' median a first call takes to be a warm-up (Q9: a constant).</summary>
    internal const double Ratio = 10;

    /// <summary>How far above the later calls' median, in milliseconds, a first call must also be.</summary>
    internal const double FloorMs = 50;

    /// <summary>How close, in milliseconds, a call's end must be to the first call's end for the two to have been
    /// released together.</summary>
    internal const double ReleaseMs = 50;

    /// <summary>
    /// The calls of <paramref name="features"/>' scenarios that the run's warm-up slowed down. <paramref name="logs"/>
    /// are the run's records after <see cref="BackgroundAttribution.Expire"/>: a call in no scenario (the background,
    /// expired calls among it) is neither judged nor charged. <paramref name="rules"/> are the consumer's
    /// <see cref="ReportConfigurationOptions.HistoryShapeTemplates"/>, applied before the built-in templates.
    /// </summary>
    internal static WarmUpResult Find(Feature[] features, IReadOnlyList<RequestResponseLog?>? logs, HistoryShapeRules? rules = null)
    {
        if (logs is null || logs.Count == 0)
            return WarmUpResult.None;

        var scenarios = new Dictionary<string, Scenario>(StringComparer.Ordinal);
        foreach (var scenario in features.SelectMany(f => f.Scenarios))
            scenarios.TryAdd(scenario.Id, scenario);

        var durations = ReportGenerator.ComputeInteractionDurations(logs.OfType<RequestResponseLog>().ToArray());
        var calls = TestCalls(logs, scenarios, durations, rules);

        var marks = new Dictionary<Guid, WarmUpMark>();
        foreach (var shape in calls.GroupBy(c => (c.Service, c.Method, c.Shape)))
            Judge(shape.ToList(), marks);
        if (marks.Count == 0)
            return WarmUpResult.None;

        var scenarioMs = new Dictionary<string, double>(StringComparer.Ordinal);
        foreach (var scenario in calls.Where(c => marks.ContainsKey(c.Id)).GroupBy(c => c.ScenarioId))
        {
            // Calls in one scenario can overlap: each instant counts once, and never more than the scenario's own time.
            var covered = Covered(scenario.Select(c => (c.Start, c.End!.Value)));
            var wall = scenarios[scenario.Key].Duration?.TotalMilliseconds ?? 0;
            if (Math.Min(covered, wall) is > 0 and var ms)
                scenarioMs[scenario.Key] = ms;
        }

        return new WarmUpResult(marks, scenarioMs);
    }

    /// <summary>
    /// A start as the data files write it, to the millisecond (<c>yyyy-MM-ddTHH:mm:ss.fffZ</c>), so the marks a report
    /// carries are the ones a reader of the report finds: calls a parallel lane starts in one millisecond are ordered by
    /// their duration there, not by ticks the file does not hold. A duration is written whole, so it is taken as it is.
    /// </summary>
    private static DateTimeOffset? AsWritten(DateTimeOffset? start) =>
        start is { } at ? new DateTimeOffset(at.UtcTicks - at.UtcTicks % TimeSpan.TicksPerMillisecond, TimeSpan.Zero) : null;

    private sealed record Call(Guid Id, int Order, string ScenarioId, string Service, string Method, string Shape,
        DateTimeOffset? StartAt, double? DurationMs, int? Status)
    {
        public DateTimeOffset Start => StartAt!.Value;
        public double Duration => DurationMs!.Value;
        public DateTimeOffset? End => StartAt is { } start && DurationMs is { } ms ? start.AddTicks((long)Math.Round(ms * TimeSpan.TicksPerMillisecond)) : null;
        public bool Failed => Status / 100 == 5;
    }

    /// <summary>
    /// The calls the rule judges (4.1 step 1): a request paired with its response, in a scenario, made by the test
    /// itself. The test's caller never appears as a service among its scenario's calls (the actor rule the sequence
    /// diagram uses, <c>PlantUmlCreator</c>), and an event a broker delivered is not a request the test made.
    /// </summary>
    private static List<Call> TestCalls(IReadOnlyList<RequestResponseLog?> logs, Dictionary<string, Scenario> scenarios,
        IReadOnlyDictionary<Guid, double> durations, HistoryShapeRules? rules)
    {
        var requests = new List<RequestResponseLog>();
        var responses = new Dictionary<Guid, RequestResponseLog>();
        var services = new Dictionary<string, HashSet<string>>(StringComparer.Ordinal);
        foreach (var log in logs)
        {
            if (log is null || log.TrackingIgnore || log.IsDiagramMarker || !scenarios.ContainsKey(log.TestId))
                continue;
            if (log.Type == RequestResponseType.Response)
            {
                responses.TryAdd(log.RequestResponseId, log);
                continue;
            }
            requests.Add(log);
            if (!services.TryGetValue(log.TestId, out var called))
                services[log.TestId] = called = new HashSet<string>(StringComparer.Ordinal);
            called.Add(log.ServiceName);
        }

        var calls = new List<Call>(requests.Count);
        foreach (var request in requests)
        {
            if (request.MetaType == RequestResponseMetaType.Event || services[request.TestId].Contains(request.CallerName))
                continue;
            responses.TryGetValue(request.RequestResponseId, out var response);
            calls.Add(new Call(request.RequestResponseId, calls.Count, request.TestId, request.ServiceName,
                MethodOf(request).ToUpperInvariant(), ShapeOf(request, rules), AsWritten(request.Timestamp),
                durations.TryGetValue(request.RequestResponseId, out var ms) ? ms : null,
                InteractionStatus.Split(response?.StatusCode).Code));
        }
        return calls;
    }

    /// <summary>4.1 steps 3 to 6, for one shape's calls.</summary>
    private static void Judge(List<Call> calls, Dictionary<Guid, WarmUpMark> marks)
    {
        // A call without a start or a duration may have been the first: nothing is said about its shape.
        if (calls.Count < 2 || calls.Any(c => c.StartAt is null || c.DurationMs is null))
            return;

        // In time, not in the data file's order (an ingest replays a test's records as a call tree); ties go to the
        // shorter call, then to capture order, as warmup.py's stable sort on (start, duration) does.
        calls.Sort((a, b) => a.Start != b.Start ? a.Start.CompareTo(b.Start)
            : a.Duration != b.Duration ? a.Duration.CompareTo(b.Duration)
            : a.Order.CompareTo(b.Order));
        var first = calls[0];
        var firstEnd = first.End!.Value;

        var baseline = calls.Skip(1).Where(c => c.Start >= firstEnd);
        // A first call that failed with a 5xx is compared only with later calls that failed so too (F16): a fast
        // success says nothing about how long the failure path takes.
        if (first.Failed)
            baseline = baseline.Where(c => c.Failed);
        var later = baseline.Select(c => c.Duration).ToList();
        if (later.Count == 0)
            return;

        var median = Median(later);
        if (!(first.Duration >= Ratio * median && first.Duration - median >= FloorMs))
            return;

        var shape = first.Shape;
        marks[first.Id] = new WarmUpMark(WarmUpMark.FirstKind, shape, median, later.Count, null, first.Duration, first.ScenarioId);
        foreach (var call in calls.Skip(1))
        {
            if (call.Start < first.Start || call.Start >= firstEnd)
                continue;
            var byRatio = call.Duration >= Ratio * median && call.Duration - median >= FloorMs;
            var released = Math.Abs((call.End!.Value - firstEnd).TotalMilliseconds) <= ReleaseMs && call.Duration - median >= FloorMs;
            if (byRatio || released)
                marks[call.Id] = new WarmUpMark(WarmUpMark.WaitedKind, shape, median, later.Count, first.Id, call.Duration, call.ScenarioId);
        }
    }

    private static double Median(List<double> values)
    {
        values.Sort();
        var middle = values.Count / 2;
        return values.Count % 2 == 1 ? values[middle] : (values[middle - 1] + values[middle]) / 2;
    }

    /// <summary>The milliseconds a set of intervals covers, each instant counted once.</summary>
    private static double Covered(IEnumerable<(DateTimeOffset Start, DateTimeOffset End)> intervals)
    {
        var total = TimeSpan.Zero;
        DateTimeOffset? start = null, end = null;
        foreach (var (s, e) in intervals.OrderBy(i => i.Start).ThenBy(i => i.End))
        {
            if (end is null || s > end)
            {
                if (end is not null)
                    total += end.Value - start!.Value;
                (start, end) = (s, e);
            }
            else if (e > end)
                end = e;
        }
        if (end is not null)
            total += end.Value - start!.Value;
        return total.TotalMilliseconds;
    }

    /// <summary>
    /// 4.1 step 2: the method and the templated path, without the query string or the fragment, so neither splits a
    /// shape; a statement-shaped dependency, whose URI names the connection, adds its templated statement head.
    /// </summary>
    private static string ShapeOf(RequestResponseLog request, HistoryShapeRules? rules)
    {
        var shape = MethodOf(request).ToUpperInvariant() + " " + InteractionShape.Template(PathOf(request.Uri.ToString()), rules);
        return DependencyCategories.IsStatementShaped(request.DependencyCategory) && !string.IsNullOrWhiteSpace(request.Content)
            ? shape + " " + InteractionShape.StatementHead(request.Content, rules)
            : shape;
    }

    /// <summary>The path of the URI the data file writes: an absolute URI loses its scheme and authority, and any URI
    /// its query and fragment. warmup.py's <c>path_of</c>.</summary>
    internal static string PathOf(string uri)
    {
        var match = SchemeAndAuthority().Match(uri);
        var rest = match.Success ? match.Groups["rest"].Value : uri;
        var cut = rest.IndexOfAny(['?', '#']);
        var path = cut >= 0 ? rest[..cut] : rest;
        return path.Length == 0 ? "/" : path;
    }

    private static string MethodOf(RequestResponseLog log) => log.Method.Value switch
    {
        HttpMethod method => method.Method,
        var other => other?.ToString() ?? ""
    };

    [GeneratedRegex(@"^[a-zA-Z][a-zA-Z0-9+.\-]*://[^/?#]*(?<rest>.*)$")]
    private static partial Regex SchemeAndAuthority();
}
