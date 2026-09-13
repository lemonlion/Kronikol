using Kronikol.Tool.Query;

namespace Kronikol.Tool;

/// <summary>
/// <c>interactions --group-by</c> — generic bucketing over the index: any combination of dimensions, a
/// count/error/timing/bodies summary per bucket. <c>services</c> stays as the curated view and the only
/// answerer of negative questions; this is the general form. Index-only unless combined with
/// <c>--where</c>.
/// </summary>
internal static partial class QueryCommand
{
    /// <summary>The dimensions <c>interactions --group-by</c> accepts. Internal so SkillDriftTests can pin
    /// the help text and the skill's reference to the one list the tool actually validates against.</summary>
    internal static readonly string[] GroupByDimensions =
        ["service", "method", "status", "path", "step", "phase", "category", "kind", "capturedBy"];

    private static int GroupedInteractions(List<(ScenarioEntry Scenario, InteractionEntry Request, InteractionEntry? Response)> matches,
        QueryOptions options, QueryWriter writer, TextWriter error, ScenarioEntry? only)
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

        var buckets = new Dictionary<string, GroupBucket>(StringComparer.Ordinal);
        foreach (var (scenario, request, response) in matches)
        {
            var values = dims.Select(d => DimensionValue(d, request, response)).ToArray();
            var key = string.Join('\x1f', values);
            if (!buckets.TryGetValue(key, out var bucket))
                buckets[key] = bucket = new GroupBucket(values);
            bucket.Calls++;
            if (IsError(response?.StatusCode, response?.StatusText))
                bucket.Errors++;
            if ((request.DurationMs ?? response?.DurationMs) is { } ms)
                bucket.Durations.Add(ms);
            if (response?.BodyHash is { } hash)
                bucket.Bodies.Add(hash);
        }

        if (options.Count)
        {
            writer.Count(buckets.Count);
            return 0;
        }

        if (!SortIsValid(options, BucketSorts, error))
            return 2;

        var ordered = options.Sort switch
        {
            "errors" => buckets.Values.OrderByDescending(b => b.Errors).ThenByDescending(b => b.Calls).ToList(),
            "duration" => buckets.Values.OrderByDescending(b => b.Median() ?? 0).ToList(),
            _ => buckets.Values.OrderByDescending(b => b.Calls).ToList()
        };

        // The bare stepPath string collides across scenarios — say so rather than pretending the
        // buckets are comparable.
        if (only is null && dims.Contains("step", StringComparer.OrdinalIgnoreCase))
            writer.Note("! step \"2\" spans scenarios — the same path is a different step in each; scope with s3 for one scenario's steps");

        var widths = dims.Select((d, i) => Math.Max(d.Length, ordered.Count == 0 ? 0 : ordered.Max(b => b.Values[i].Length)) + 2).ToArray();
        writer.Line(string.Concat(dims.Select((d, i) => d.PadRight(widths[i]))) + $"{"calls",5} {"errors",6} {"median",8} {"max",8} {"bodies",6}");

        writer.Page(ordered, options.Offset, options.PageSize(200, writer, "buckets"), "buckets", bucket =>
        {
            var cells = string.Concat(bucket.Values.Select((v, i) => v.PadRight(widths[i])));
            writer.Line($"{cells}{bucket.Calls,5} {bucket.Errors,6} {QueryWriter.Duration(bucket.Median()),8} {QueryWriter.Duration(bucket.Max()),8} {bucket.Bodies.Count,6}");
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
            bodies = bucket.Bodies.Count
        });

        return 0;
    }

    private static string DimensionValue(string dimension, InteractionEntry request, InteractionEntry? response) =>
        dimension.ToLowerInvariant() switch
        {
            "service" => request.ServiceName,
            "method" => request.Method ?? "-",
            "status" => StatusOf(response).Text ?? "-",
            "path" => UriPath(request.Uri),
            "step" => request.StepPath ?? "-",
            "phase" => request.Phase ?? "-",
            "category" => request.DependencyCategory ?? "-",
            "capturedby" => request.CapturedBy ?? "-",
            _ => request.MetaType ?? "Default" // kind
        };

    private static string UriPath(string uri)
    {
        if (System.Uri.TryCreate(uri, UriKind.Absolute, out var parsed))
            return parsed.AbsolutePath;
        var question = uri.IndexOf('?');
        return question < 0 ? uri : uri[..question];
    }

    private sealed class GroupBucket(string[] values)
    {
        public string[] Values { get; } = values;
        public int Calls { get; set; }
        public int Errors { get; set; }
        public List<double> Durations { get; } = [];
        public HashSet<string> Bodies { get; } = new(StringComparer.Ordinal);

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
