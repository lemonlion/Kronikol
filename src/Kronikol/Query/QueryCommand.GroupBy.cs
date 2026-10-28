using Kronikol.Constants;
using Kronikol.History;

namespace Kronikol.Query;

/// <summary>
/// <c>interactions --group-by</c> — generic bucketing over the index: any combination of dimensions, a
/// count/error/timing/bodies/bytes summary per bucket. <c>services</c> stays as the curated view and the only
/// answerer of negative questions; this is the general form. Index-only unless combined with
/// <c>--where</c>, or grouped by <c>route</c>, which reads the request body of a POST or a statement-shaped call (#192).
/// <c>origin</c> and <c>cause</c> read the report's party table, the one <c>parties</c> prints, and the flow nesting.
/// </summary>
public static partial class QueryCommand
{
    /// <summary>The dimensions <c>interactions --group-by</c> accepts. Internal so SkillDriftTests can pin
    /// the help text and the skill's reference to the one list the tool actually validates against.</summary>
    internal static readonly string[] GroupByDimensions =
        ["service", "caller", "origin", "cause", "method", "status", "path", "route", "step", "phase", "category", "kind", "capturedBy"];

    /// <summary>The first release whose reports carry their run's HistoryShapeTemplates (#192): an earlier report cannot say whether its run had rules.</summary>
    internal static readonly Version FirstVersionWithShapeTemplates = new(5, 19, 0);

    /// <summary>
    /// Whether a report without the block was written by a version that cannot say whether its run had rules: one before
    /// <see cref="FirstVersionWithShapeTemplates"/>, or one whose version is not Kronikol's (Kronikol4J writes its own).
    /// </summary>
    internal static bool PredatesShapeTemplates(string? kronikolVersion)
    {
        var numbers = (kronikolVersion ?? "").Split('-', '+')[0];
        return !Version.TryParse(numbers, out var version) || version < FirstVersionWithShapeTemplates;
    }

    /// <summary>How much of a route a text row shows; the JSON key carries all of it.</summary>
    private const int RouteWidth = 80;

    private static int GroupedInteractions(ReportIndex index, BodyCache cache, List<(ScenarioEntry Scenario, InteractionEntry Request, InteractionEntry? Response)> matches,
        QueryOptions options, QueryWriter writer, TextWriter error, ScenarioEntry? only,
        IReadOnlyList<WhereClause>? clauses = null, int unevaluable = 0)
    {
        var dims = options.GroupBy!.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        foreach (var dim in dims)
            if (!GroupByDimensions.Contains(dim, StringComparer.OrdinalIgnoreCase))
            {
                error.WriteLine($"Unknown dimension: {dim}");
                error.WriteLine("Valid dimensions: " + string.Join(", ", GroupByDimensions));
                return 2;
            }
        if (dims.Length == 0)
        {
            error.WriteLine("--group-by takes a comma list of dimensions: " + string.Join(", ", GroupByDimensions));
            return 2;
        }
        // A bucket's JSON key is an object of its dimensions, which holds each once: a repeat threw there.
        if (dims.GroupBy(d => d, StringComparer.OrdinalIgnoreCase).FirstOrDefault(g => g.Count() > 1) is { } twice)
        {
            error.WriteLine($"--group-by: {twice.Key} is named twice; each dimension is one column of a bucket, so name it once.");
            return 2;
        }

        // A route is the call's operation key, read as the run read it: under the rules its report recorded, and from the
        // request body only where the key is in it, a statement's head or a POST that may be GraphQL (plan 4.4).
        var routes = dims.Contains("route", StringComparer.OrdinalIgnoreCase) ? new RouteReader(index, cache) : null;
        var roles = dims.Any(d => d.Equals("origin", StringComparison.OrdinalIgnoreCase) || d.Equals("cause", StringComparison.OrdinalIgnoreCase))
            ? new RoleReader(index)
            : null;
        var buckets = new Dictionary<string, GroupBucket>(StringComparer.Ordinal);
        foreach (var (scenario, request, response) in matches)
        {
            var values = dims.Select(d => d.ToLowerInvariant() switch
            {
                "route" => routes!.Of(request),
                "origin" => roles!.OriginOf(request),
                "cause" => roles!.CauseOf(scenario, request),
                _ => DimensionValue(d, request, response)
            }).ToArray();
            // A service is one service whatever case its callers name it in, as `services` counts it (OrdinalIgnoreCase, so
            // the invariant upper case), the row named as its first call names it, and a caller likewise, since a caller is
            // a service named by the other end of the call (#192); every other value is kept as written.
            var key = string.Join('\x1f', values.Select((v, i) => IsServiceName(dims[i]) ? v.ToUpperInvariant() : v));
            if (!buckets.TryGetValue(key, out var bucket))
                buckets[key] = bucket = new GroupBucket(values);
            bucket.Calls++;
            if (IsError(response?.StatusCode, response?.StatusText))
                bucket.Errors++;
            if ((request.DurationMs ?? response?.DurationMs) is { } ms)
                bucket.Durations.Add(ms);
            if (response?.BodyHash is { } hash)
                bucket.Bodies.Add(hash);
            // As `services` counts a service's bytes: each body's length as the index records it, decoded (#116).
            bucket.Bytes += request.BodyLength + (response?.BodyLength ?? 0);
        }

        // Under --count the answer is one token, so the caveats go to stderr, as every other note's do.
        routes?.Note(options.Count && !options.Json ? error.WriteLine : writer.Note);
        var capped = cache.CappedReads;

        if (options.Count)
        {
            writer.Count(buckets.Count);
            NoteWhereExclusions(options, writer, error, clauses, unevaluable, capped);
            return 0;
        }

        if (!SortIsValid(options, BucketSorts, error))
            return 2;

        var ordered = options.Sort switch
        {
            "errors" => buckets.Values.OrderByDescending(b => b.Errors).ThenByDescending(b => b.Calls).ToList(),
            "duration" => buckets.Values.OrderByDescending(b => b.Median() ?? 0).ToList(),
            "bytes" => buckets.Values.OrderByDescending(b => b.Bytes).ThenByDescending(b => b.Calls).ToList(),
            _ => buckets.Values.OrderByDescending(b => b.Calls).ToList()
        };

        // The bare stepPath string collides across scenarios — say so rather than pretending the
        // buckets are comparable.
        if (only is null && dims.Contains("step", StringComparer.OrdinalIgnoreCase))
            writer.Note("! step \"2\" spans scenarios — the same path is a different step in each; scope with s3 for one scenario's steps");

        // A route is cut in text as the listing cuts a call's summary: 47 of one lane's 160 routes ran past 100 characters.
        string Cell(int i, string value) => dims[i].Equals("route", StringComparison.OrdinalIgnoreCase) ? QueryWriter.OneLine(value, RouteWidth) : value;
        var widths = dims.Select((d, i) => Math.Max(d.Length, ordered.Count == 0 ? 0 : ordered.Max(b => Cell(i, b.Values[i]).Length)) + 2).ToArray();
        writer.Line(string.Concat(dims.Select((d, i) => d.PadRight(widths[i]))) + $"{"calls",5} {"errors",6} {"median",8} {"max",8} {"bodies",6} {"bytes",9}");

        writer.Page(ordered, options.Offset, options.PageSize(200, writer, "buckets"), "buckets", bucket =>
        {
            var cells = string.Concat(bucket.Values.Select((v, i) => Cell(i, v).PadRight(widths[i])));
            writer.Line($"{cells}{bucket.Calls,5} {bucket.Errors,6} {QueryWriter.Duration(bucket.Median()),8} {QueryWriter.Duration(bucket.Max()),8} {bucket.Bodies.Count,6} "
                        + $"{QueryWriter.Size((int)Math.Min(bucket.Bytes, int.MaxValue)),9}");
        }, options.RerunArgs(), bucket => new
        {
            // The bucket holds its dimension values positionally against `dims`, so the key has to come
            // from the dimension list: an object of {service, status} says what a ["payments","500"]
            // cannot, and the column header a text reader uses for the same job is suppressed in JSON.
            key = dims.Select((d, i) => new KeyValuePair<string, string>(d, bucket.Values[i]))
                .ToDictionary(e => e.Key, e => e.Value, StringComparer.Ordinal),
            calls = bucket.Calls,
            errors = bucket.Errors,
            medianMs = bucket.Median(),
            maxMs = bucket.Max(),
            bodies = bucket.Bodies.Count,
            bytes = bucket.Bytes
        });
        NoteWhereExclusions(options, writer, error, clauses, unevaluable, capped);

        return 0;
    }

    private static bool IsServiceName(string dimension) =>
        dimension.Equals("service", StringComparison.OrdinalIgnoreCase) || dimension.Equals("caller", StringComparison.OrdinalIgnoreCase);

    private static string DimensionValue(string dimension, InteractionEntry request, InteractionEntry? response) =>
        dimension.ToLowerInvariant() switch
        {
            "service" => request.ServiceName,
            "caller" => request.CallerName.Length > 0 ? request.CallerName : "-",
            "method" => request.Method ?? "-",
            "status" => StatusOf(response).Text ?? "-",
            "path" => UriPath(request.Uri),
            "step" => request.StepPath ?? "-",
            "phase" => request.Phase ?? "-",
            "category" => request.DependencyCategory ?? "-",
            "capturedby" => request.CapturedBy ?? "-",
            _ => request.MetaType ?? "Default" // kind
        };

    // The `path` dimension: the URI's path as recorded, ids and all; `route` is the call's operation, templated.
    private static string UriPath(string uri)
    {
        if (System.Uri.TryCreate(uri, UriKind.Absolute, out var parsed))
            return parsed.AbsolutePath;
        var question = uri.IndexOf('?');
        return question < 0 ? uri : uri[..question];
    }

    /// <summary>
    /// The <c>route</c> of each call, for one command: <see cref="CallOperation.Of"/> without the query's keys, under the
    /// rules the report recorded (read so that a pattern that times out is dropped for the rest of the command), with a
    /// request body read only for a statement-shaped call or a POST, once per distinct body through the command's cache.
    /// </summary>
    private sealed class RouteReader(ReportIndex index, BodyCache cache)
    {
        private readonly HistoryShapeRules? _rules = HistoryShapeRules.ForReading(index.ShapeTemplates);
        private readonly Dictionary<string, string> _routes = new(StringComparer.Ordinal);

        public string Of(InteractionEntry request)
        {
            var needsBody = DependencyCategories.IsStatementShaped(request.DependencyCategory)
                            || string.Equals(request.Method, "POST", StringComparison.OrdinalIgnoreCase);
            var hash = needsBody ? request.BodyHash : null;
            var key = string.Join('\x1f', request.Method, request.Uri, request.DependencyCategory, hash);
            if (_routes.TryGetValue(key, out var route))
                return route;
            var content = hash is null ? null : cache.Raw(hash);
            return _routes[key] = CallOperation.Of(request.Method, request.Uri, request.DependencyCategory, content, _rules, queryKeys: false);
        }

        /// <summary>
        /// Says once what the routes could not use: a report without the block, written before it or by Kronikol4J, whose
        /// writer records none and whose version is its own, cannot say whether its run had rules; and a recorded rule that
        /// did not compile or timed out is named, as a run names it. A report that holds the block has its rules applied,
        /// whichever version wrote it.
        /// </summary>
        public void Note(Action<string> note)
        {
            if (index.ShapeTemplates.Count == 0 && PredatesShapeTemplates(index.KronikolVersion))
                note("route: built-in templating only; this report cannot say whether its run had HistoryShapeTemplates");
            foreach (var problem in _rules?.Problems ?? [])
                note("route: " + problem);
        }
    }

    /// <summary>
    /// The <c>origin</c> and <c>cause</c> of each call, for one command (#192 R3): the report's party table, read once, as
    /// <see cref="Kronikol.Parties.PartyTable"/> answers them, with each scenario's nesting worked out once.
    /// </summary>
    private sealed class RoleReader(ReportIndex index)
    {
        private readonly Kronikol.Parties.PartyTable _table = PartiesOf(index).Table;
        private readonly Dictionary<ScenarioEntry, int?[]> _parents = [];
        private readonly Dictionary<string, string> _origins = new(StringComparer.Ordinal);
        private readonly Dictionary<(string Caller, string? Source, bool Inside), string> _causes = [];

        public string OriginOf(InteractionEntry request)
        {
            if (!_origins.TryGetValue(request.CallerName, out var origin))
                _origins[request.CallerName] = origin = _table.OriginOf(request.CallerName);
            return origin;
        }

        /// <summary>A scenario's call, so never one of the root background block's.</summary>
        public string CauseOf(ScenarioEntry scenario, InteractionEntry request)
        {
            if (!_parents.TryGetValue(scenario, out var parents))
                _parents[scenario] = parents = CallNesting.Parents(scenario.Interactions);
            var key = (request.CallerName, request.AttributionSource, request.Ordinal < parents.Length && parents[request.Ordinal] is not null);
            if (!_causes.TryGetValue(key, out var cause))
                _causes[key] = cause = _table.CauseOf(key.CallerName, key.AttributionSource, inBackgroundBlock: false, insideReceivedCall: key.Item3);
            return cause;
        }
    }

    private sealed class GroupBucket(string[] values)
    {
        public string[] Values { get; } = values;
        public int Calls { get; set; }
        public int Errors { get; set; }
        public List<double> Durations { get; } = [];
        public HashSet<string> Bodies { get; } = new(StringComparer.Ordinal);
        public long Bytes { get; set; }

        public double? Median()
        {
            if (Durations.Count == 0)
                return null;
            var sorted = Durations.Order().ToArray();
            return sorted[sorted.Length / 2];
        }

        public double? Max() => Durations.Count == 0 ? null : Durations.Max();
    }
}
