using Kronikol.Tool.Query;

namespace Kronikol.Tool;

/// <summary>
/// The commands that answer "what happened" before anything is fetched: the run header, the scenario list,
/// and the per-service view — the only one that answers a negative question, which is why it earns its
/// place beside the others.
/// </summary>
internal static partial class QueryCommand
{
    private static int Summary(ReportIndex index, QueryOptions options, QueryWriter writer)
    {
        var scenarios = index.Scenarios;
        if (options.Count)
        {
            writer.Line(scenarios.Count.ToString());
            return 0;
        }

        var failed = scenarios.Count(s => s.Failed);
        var interactions = scenarios.Sum(s => s.Interactions.Count);

        writer.Line($"{Path.GetFileName(index.Path)}  {QueryWriter.Size((int)Math.Min(index.FileLength, int.MaxValue))}"
                    + (index.KronikolVersion is { } v ? $"  Kronikol {v}" : ""));
        writer.Line($"{index.StartTime} → {index.EndTime}");
        writer.Line($"{scenarios.Count} scenarios · {failed} failed · {interactions} interactions · {index.Bodies.Count} distinct bodies");
        writer.Line();

        foreach (var feature in scenarios.GroupBy(s => s.FeatureName))
        {
            var featureFailed = feature.Count(s => s.Failed);
            writer.Line($"{feature.Key}  {feature.Count() - featureFailed} passed"
                        + (featureFailed > 0 ? $", {featureFailed} FAILED" : ""));
        }

        if (failed > 0)
        {
            writer.Line();
            writer.Line("Failed:");
            foreach (var scenario in scenarios.Where(s => s.Failed).Take(10))
                writer.Line($"  {scenario.Address}  {scenario.Name}"
                            + (scenario.ErrorMessage is { } e ? $"  — {QueryWriter.OneLine(e, 90)}" : ""));
            if (failed > 10)
                writer.Line($"  … {failed - 10} more · scenarios --result Failed");
            writer.Line("  → failures");
        }

        var slowest = scenarios.OrderByDescending(s => s.DurationSeconds).Take(3).ToArray();
        if (slowest.Length > 0 && slowest[0].DurationSeconds > 0)
        {
            writer.Line();
            writer.Line("Slowest:");
            foreach (var scenario in slowest)
                writer.Line($"  {scenario.Address}  {scenario.DurationSeconds:0.##}s  {QueryWriter.OneLine(scenario.Name, 70)}");
        }

        if (index.Diagnostics.Count > 0)
        {
            writer.Line();
            writer.Line($"Diagnostics ({index.Diagnostics.Count}):");
            foreach (var group in index.Diagnostics.GroupBy(d => d.Kind))
                writer.Line($"  {group.Key} ×{group.Count()}  {QueryWriter.OneLine(group.First().Message, 90)}");
        }

        writer.Footer(failed > 0 ? "next: failures" : "next: scenarios · services");
        return 0;
    }

    private static int Scenarios(ReportIndex index, QueryOptions options, QueryWriter writer)
    {
        var matches = index.Scenarios.Where(s => Matches(s, options)).ToList();

        if (options.Count)
        {
            writer.Line(matches.Count.ToString());
            return 0;
        }

        if (matches.Count == 0)
        {
            writer.Line("no scenarios matched");
            writer.Footer($"{index.Scenarios.Count} scenarios in the report");
            return 0;
        }

        writer.Page(matches, options.Offset, Math.Min(options.Limit, 200), "scenarios", scenario =>
        {
            var flags = scenario.Failed ? "FAIL" : scenario.Result.Length > 0 ? scenario.Result[..Math.Min(4, scenario.Result.Length)].ToLowerInvariant() : "";
            writer.Line($"{scenario.Address,-5} {flags,-4} {scenario.DurationSeconds,6:0.##}s  {scenario.Interactions.Count,4} calls  "
                        + QueryWriter.OneLine(scenario.Name, 80));
            if (scenario.Failed && scenario.ErrorMessage is { } message)
                writer.Line($"        {QueryWriter.OneLine(message, 100)}");
        }, options.RerunPrefix());

        return 0;
    }

    private static bool Matches(ScenarioEntry scenario, QueryOptions options)
    {
        if (options.Result is { } result && !scenario.Result.Equals(result, StringComparison.OrdinalIgnoreCase))
            return false;
        if (options.Failed && !scenario.Failed)
            return false;
        if (options.Feature is { } feature && !scenario.FeatureName.Contains(feature, StringComparison.OrdinalIgnoreCase))
            return false;
        if (options.Label is { } label
            && !scenario.Labels.Concat(scenario.Categories).Concat(scenario.FeatureLabels)
                .Any(l => l.Contains(label, StringComparison.OrdinalIgnoreCase)))
            return false;
        if (options.Grep is { } grep && !scenario.Name.Contains(grep, StringComparison.OrdinalIgnoreCase))
            return false;
        if (options.SlowerThan is { } slower && scenario.DurationSeconds < slower)
            return false;
        return true;
    }

    private static int Services(ReportIndex index, QueryOptions options, QueryWriter writer, TextWriter error)
    {
        var scope = index.Scenarios.AsEnumerable();
        if (options.Positional.Count > 0)
        {
            if (!Address.TryParse(options.Positional[0], out var address) || address.Kind != AddressKind.Scenario)
            {
                error.WriteLine($"Not a scenario address: {options.Positional[0]} (expected s3)");
                return 2;
            }
            if (index.Scenario(address.Scenario) is not { } scenario)
            {
                error.WriteLine($"No scenario {address} — the report has {index.Scenarios.Count}.");
                return 2;
            }
            scope = [scenario];
        }

        var stats = new Dictionary<string, ServiceStats>(StringComparer.OrdinalIgnoreCase);
        foreach (var scenario in scope)
        foreach (var interaction in scenario.Interactions)
        {
            if (!stats.TryGetValue(interaction.ServiceName, out var entry))
                stats[interaction.ServiceName] = entry = new ServiceStats(interaction.ServiceName);
            entry.Add(interaction);
        }

        if (options.Count)
        {
            writer.Line(stats.Count.ToString());
            return 0;
        }

        if (stats.Count == 0)
        {
            // Absence is the answer this command exists to give, so say it plainly rather than printing nothing.
            writer.Line("no services were called");
            writer.Footer("nothing was captured for this scope — check the capture is attached, not that the test skipped the call");
            return 0;
        }

        var ordered = options.Sort switch
        {
            "duration" => stats.Values.OrderByDescending(s => s.TotalMs).ToList(),
            "bytes" => stats.Values.OrderByDescending(s => s.Bytes).ToList(),
            "errors" => stats.Values.OrderByDescending(s => s.Errors).ToList(),
            _ => stats.Values.OrderByDescending(s => s.Calls).ToList()
        };

        writer.Line($"{"service",-24} {"calls",5} {"errors",6} {"bytes",9} {"p50",8} {"max",8}  statuses");
        foreach (var entry in ordered)
            writer.Line($"{QueryWriter.OneLine(entry.Name, 24),-24} {entry.Calls,5} {entry.Errors,6} "
                        + $"{QueryWriter.Size(entry.Bytes),9} {QueryWriter.Duration(entry.Median()),8} {QueryWriter.Duration(entry.MaxMs),8}  "
                        + entry.StatusSummary());

        writer.Footer($"{ordered.Count} services · a service missing here was never called");
        return 0;
    }

    private sealed class ServiceStats(string name)
    {
        private readonly List<double> _durations = [];
        private readonly Dictionary<string, int> _statuses = new(StringComparer.OrdinalIgnoreCase);

        public string Name { get; } = name;
        public int Calls { get; private set; }
        public int Errors { get; private set; }
        public int Bytes { get; private set; }
        public double MaxMs { get; private set; }
        public double TotalMs { get; private set; }

        public void Add(InteractionEntry interaction)
        {
            if (interaction.Type.Equals("Request", StringComparison.OrdinalIgnoreCase))
                Calls++;

            Bytes += interaction.BodyLength;

            if (interaction.StatusCode is { Length: > 0 } status)
            {
                _statuses[status] = _statuses.GetValueOrDefault(status) + 1;
                if (IsError(status))
                    Errors++;
            }

            if (interaction.Type.Equals("Response", StringComparison.OrdinalIgnoreCase) && interaction.DurationMs is { } ms)
            {
                _durations.Add(ms);
                TotalMs += ms;
                MaxMs = Math.Max(MaxMs, ms);
            }
        }

        public double? Median()
        {
            if (_durations.Count == 0)
                return null;
            var sorted = _durations.Order().ToArray();
            return sorted[sorted.Length / 2];
        }

        public string StatusSummary() =>
            _statuses.Count == 0 ? "" : string.Join(" ", _statuses.OrderByDescending(s => s.Value).Take(4).Select(s => $"{s.Key}×{s.Value}"));
    }
}
