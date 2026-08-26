using System.Globalization;
using Kronikol.Tool.Query;

namespace Kronikol.Tool;

/// <summary>
/// <c>values</c> — projection with aggregation: <c>SELECT value, COUNT(*) … GROUP BY value</c> where the
/// column is a JSON path evaluated across every matched body. The question it answers is "what did the
/// system see", so counting is per occurrence, not per distinct body — while each distinct body is parsed
/// and evaluated exactly once through the <see cref="BodyCache"/>.
/// </summary>
internal static partial class QueryCommand
{
    private static int Values(ReportIndex index, QueryOptions options, QueryWriter writer, TextWriter error)
    {
        if (options.Path is null)
        {
            error.WriteLine("values needs a path: kronikol query values <report> --path '$.status' [s3] [--service X] [--stats] [--request|--both]");
            return 2;
        }

        if (!PathEngine.TryParse(options.Path, out var segments, out var parseError))
        {
            error.WriteLine($"Bad path: {parseError}");
            error.WriteLine("Grammar: $.name · [2] · [*] · ['key.with.dots'] · .length() (last segment only). Quote the whole path — [*] is shell-active.");
            return 2;
        }

        ScenarioEntry? only = null;
        if (options.Positional.Count > 0)
        {
            if (!Address.TryParse(options.Positional[0], out var address) || address.Kind != AddressKind.Scenario)
            {
                error.WriteLine($"Not a scenario address: {options.Positional[0]} (expected s3, or no address for the whole run)");
                return 2;
            }
            if (index.Scenario(address.Scenario) is not { } scenario)
            {
                error.WriteLine($"No scenario s{address.Scenario} — the report has {index.Scenarios.Count}.");
                return 2;
            }
            only = scenario;
        }

        var clauses = ParseWheres(options, error);
        if (clauses is null)
            return 2;

        var targetRequests = options.Request || options.Both;
        var targetResponses = !options.Request || options.Both;

        using var cache = new BodyCache(index);
        var buckets = new Dictionary<string, ValueBucket>(StringComparer.Ordinal);
        var numbers = new List<(double Value, string Address)>();
        var evaluated = 0;
        var distinctBodies = new HashSet<string>(StringComparer.Ordinal);
        int absent = 0, nonNumeric = 0, bodiless = 0, unpaired = 0, nonJson = 0, unevaluable = 0, truncated = 0;

        foreach (var (scenario, request, response) in AllInteractions(index, only))
        {
            if (!Matches(scenario, request, options))
                continue;

            if (clauses.Count > 0)
            {
                var excluded = false;
                if (!SatisfiesWheres(clauses, request, response, options, cache, ref excluded))
                {
                    if (excluded)
                        unevaluable++;
                    continue;
                }
            }

            if (targetRequests)
                Evaluate(scenario, request, "req");
            if (targetResponses)
            {
                // Pairing-based, not event-based: an event with a tracked response participates like any
                // other call; only a genuinely unpaired one lands in the footnote. Never silently dropped.
                if (response is null)
                    unpaired++;
                else
                    Evaluate(scenario, response, "resp");
            }
        }

        if (options.Count)
        {
            writer.Line(buckets.Values.Where(b => b.Value != "(absent)").Sum(b => b.Count).ToString());
            return 0;
        }

        var direction = options.Both ? "request+response" : options.Request ? "request" : "response";
        writer.Line($"{options.Path} across {evaluated} {direction} bodies ({distinctBodies.Count} distinct bodies)");

        if (options.Stats)
        {
            writer.Line($"  count {numbers.Count} · absent {absent} · non-numeric {nonNumeric} · distinct {numbers.Select(n => n.Value).Distinct().Count()}");
            if (numbers.Count > 0)
            {
                var sorted = numbers.OrderBy(n => n.Value).ToList();
                var sum = numbers.Sum(n => n.Value);
                writer.Line($"  min {N(sorted[0].Value)} ({sorted[0].Address}) · median {N(sorted[numbers.Count / 2].Value)}"
                            + $" · max {N(sorted[^1].Value)} ({sorted[^1].Address}) · sum {N(sum)} · mean {N(sum / numbers.Count)}");
            }
            Footnotes();
            writer.Footer("");
            return 0;
        }

        var ordered = buckets.Values
            .OrderByDescending(b => b.Count)
            .ThenBy(b => b.Value, StringComparer.Ordinal)
            .ToList();

        writer.Page(ordered, options.Offset, Math.Min(options.Limit, 120), "values", bucket =>
        {
            var addresses = bucket.Count > bucket.Addresses.Count
                ? "e.g. " + bucket.Addresses[0]
                : string.Join(", ", bucket.Addresses);
            var tag = bucket.Direction is { } d ? $"{d,-5}" : "";
            writer.Line($"  {bucket.Value,-12} ×{bucket.Count,-4} {tag}{addresses}");
        }, options.RerunPrefix() + $"--path \"{options.Path}\" ");

        Footnotes();
        return 0;

        void Evaluate(ScenarioEntry scenario, InteractionEntry target, string tag)
        {
            if (target.BodyHash is not { } hash)
            {
                bodiless++;
                return;
            }
            if (cache.Raw(hash) is { } raw && raw.Contains("…truncated (", StringComparison.Ordinal))
                truncated++;
            var document = cache.Json(hash);
            if (document is null)
            {
                nonJson++;
                return;
            }

            evaluated++;
            distinctBodies.Add(hash);
            var address = target.Address(scenario);
            var any = false;
            foreach (var (_, value) in PathEngine.SelectAll(document.RootElement, segments))
            {
                any = true;
                Record(value.Row(), address, tag, value);
            }
            if (!any)
            {
                // A body the path misses is an answer — "one response was missing the field" is exactly
                // the bug this command exists to find.
                absent++;
                Record("(absent)", address, tag, null);
            }
        }

        void Record(string key, string address, string tag, PathValue? value)
        {
            var bucketKey = options.Both ? tag + " " + key : key;
            if (!buckets.TryGetValue(bucketKey, out var bucket))
                buckets[bucketKey] = bucket = new ValueBucket(key, options.Both ? tag : null);
            bucket.Count++;
            if (bucket.Addresses.Count < 2)
                bucket.Addresses.Add(address);
            if (value is { } v)
            {
                if (v.TryNumber(out var number))
                    numbers.Add((number, address));
                else
                    nonNumeric++;
            }
        }

        void Footnotes()
        {
            if (bodiless > 0)
                writer.Line($"{bodiless} call{(bodiless == 1 ? "" : "s")} carried no body");
            if (unpaired > 0)
                writer.Line($"{unpaired} call{(unpaired == 1 ? "" : "s")} had no response to evaluate");
            if (nonJson > 0)
                writer.Line($"{nonJson} {(nonJson == 1 ? "body was" : "bodies were")} not JSON — counted, not evaluated");
            if (unevaluable > 0)
                writer.Line($"{unevaluable} call{(unevaluable == 1 ? "" : "s")} had no evaluable body — excluded by --where");
            if (truncated > 0)
                writer.Line($"! {truncated} {(truncated == 1 ? "body was" : "bodies were")} capped at capture time — the rest was never recorded");
        }

        static string N(double value) => value.ToString("0.####", CultureInfo.InvariantCulture);
    }

    private sealed class ValueBucket(string value, string? direction)
    {
        public string Value { get; } = value;
        public string? Direction { get; } = direction;
        public int Count { get; set; }
        public List<string> Addresses { get; } = [];
    }
}
