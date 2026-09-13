using Kronikol.Tool.Query;

namespace Kronikol.Tool;

/// <summary>
/// The commands that answer "what happened" before anything is fetched: the run header, the scenario list,
/// and the per-service view — the only one that answers a negative question, which is why it earns its
/// place beside the others.
/// </summary>
internal static partial class QueryCommand
{
    private static int Summary(ReportIndex index, QueryOptions options, QueryWriter writer, TextWriter error)
    {
        // An address used to be read and discarded here, so `summary s3` answered for the whole run and
        // looked like an answer about s3. There is no such thing as a summary of one scenario, so this
        // refuses rather than inventing one - and names the two verbs that do answer narrowly.
        if (options.Positional.Count > 0)
        {
            error.WriteLine($"summary answers for the whole run; it has no per-scenario form ({options.Positional[0]} was given).");
            error.WriteLine("For one scenario: `steps <address>` for its step tree, `flow <address>` for its calls.");
            return 2;
        }

        var scenarios = index.Scenarios;
        if (options.Count)
        {
            writer.Count(scenarios.Count);
            return 0;
        }

        var failed = scenarios.Count(s => s.Failed);
        var interactions = scenarios.Sum(s => s.Interactions.Count);

        // Five heterogeneous sections and no list, so `items` alone would be a lie: the per-feature rows
        // are the one repeated thing here, and the rest are named members beside them. Inert in text mode.
        writer.Data("run", new
        {
            file = Path.GetFileName(index.Path),
            sizeBytes = index.FileLength,
            startTime = index.StartTime,
            endTime = index.EndTime,
            scenarios = scenarios.Count,
            failed,
            interactions,
            distinctBodies = index.Bodies.Count,
            ci = index.OnCi
                ? new
                {
                    provider = index.CiProvider,
                    branch = index.CiBranch,
                    commit = index.CiCommitSha,
                    repository = index.CiRepository,
                    build = index.CiBuildNumber,
                    runId = index.CiRunId,
                    runAttempt = index.CiRunAttempt
                }
                : null
        });

        writer.Line($"{Path.GetFileName(index.Path)}  {QueryWriter.Size((int)Math.Min(index.FileLength, int.MaxValue))}"
                    + (index.KronikolVersion is { } v ? $"  Kronikol {v}" : ""));
        writer.Line($"{index.StartTime} → {index.EndTime}");
        // Only when there was a CI to read: a line saying "None" on every local run is noise, and the
        // budget is spent on answers. This is the line that tells two downloaded artifacts apart.
        if (index.OnCi)
        {
            var sha = index.CiCommitSha is { Length: > 7 } full ? full[..7] : index.CiCommitSha;
            writer.Line($"run: {index.CiBranch ?? "?"} @{sha ?? "?"}"
                        + (index.CiRepository is { } repo ? $"  {repo}" : "")
                        + $"  {index.CiProvider}"
                        + (index.CiBuildNumber is { } build ? $" #{build}" : "")
                        // Only past the first: an attempt of 1 is every ordinary run, and the line is
                        // budget. An attempt above it is the one thing that tells this artifact from
                        // the one the same runId already produced.
                        + (index.CiRunAttempt is { } attempt && attempt != "1" ? $"  attempt {attempt}" : ""));
        }
        writer.Line($"{scenarios.Count} scenarios · {failed} failed · {interactions} interactions · {index.Bodies.Count} distinct bodies");
        writer.Line();

        foreach (var feature in scenarios.GroupBy(s => s.FeatureName))
        {
            var featureFailed = feature.Count(s => s.Failed);
            writer.Line($"{QueryWriter.OneLine(feature.Key, 80)}  {feature.Count() - featureFailed} passed"
                        + (featureFailed > 0 ? $", {featureFailed} FAILED" : ""));
            writer.Item(new
            {
                feature = feature.Key,
                total = feature.Count(),
                passed = feature.Count() - featureFailed,
                failed = featureFailed
            });
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

            // The same ten, and the same reason for the cap: this is the orientation view, and `failures`
            // is the one that pages. `failedTotal` says how many the ten came out of.
            writer.Data("failed", scenarios.Where(s => s.Failed).Take(10).Select(s => new
            {
                address = s.Address,
                stableId = s.StableId,
                feature = s.FeatureName,
                scenario = s.Name,
                errorMessage = s.ErrorMessage
            }));
            writer.Data("failedTotal", failed);
        }

        var slowest = scenarios.OrderByDescending(s => s.DurationSeconds).Take(3).ToArray();
        if (slowest.Length > 0 && slowest[0].DurationSeconds > 0)
        {
            writer.Line();
            writer.Line("Slowest:");
            foreach (var scenario in slowest)
                writer.Line($"  {scenario.Address}  {scenario.DurationSeconds:0.##}s  {QueryWriter.OneLine(scenario.Name, 70)}");

            writer.Data("slowest", slowest.Select(s => new
            {
                address = s.Address,
                durationSeconds = s.DurationSeconds,
                scenario = s.Name
            }));
        }

        if (index.Diagnostics.Count > 0)
        {
            writer.Line();
            writer.Line($"Diagnostics ({index.Diagnostics.Count}):");
            foreach (var group in index.Diagnostics.GroupBy(d => d.Kind))
                writer.Line($"  {group.Key} ×{group.Count()}  {QueryWriter.OneLine(group.First().Message, 90)}");

            writer.Data("diagnostics", index.Diagnostics.GroupBy(d => d.Kind).Select(g => new
            {
                kind = g.Key,
                count = g.Count(),
                message = g.First().Message
            }));
        }

        writer.Footer(failed > 0 ? "next: failures" : "next: scenarios · services");
        return 0;
    }

    private static int Scenarios(ReportIndex index, QueryOptions options, QueryWriter writer, TextWriter error)
    {
        // An address narrows the listing to one scenario. It used to be parsed and thrown away, so an
        // agent that had narrowed got the whole run back with nothing saying it had not been narrowed.
        var scope = index.Scenarios.AsEnumerable();
        if (options.Positional.Count > 0)
        {
            if (!TryScenario(index, options, error, out var one, out var step))
                return 2;

            scope = [one];
            if (step is not null)
                writer.Note($"! a scenario listing has no per-step form — scoped to {one.Address}; `steps {one.Address}/{step}` answers for the step");
        }

        var matches = scope.Where(s => Matches(s, options)).ToList();

        if (options.Count)
        {
            writer.Count(matches.Count);
            return 0;
        }

        if (matches.Count == 0)
        {
            writer.Note("no scenarios matched");
            writer.Footer($"{index.Scenarios.Count} scenarios in the report");
            return 0;
        }

        // Several scenarios can carry one stableId - a [Theory] with repeated data, the same Examples:
        // row in two blocks, a retry. A cross-run diff cannot tell them apart except by position, and
        // nothing in the listing used to say so. Empty ids are skipped: a report written before 3.0.47
        // has none at all, and marking every row would say the opposite of the truth.
        var shared = index.Scenarios
            .Where(s => s.StableId.Length > 0)
            .GroupBy(s => s.StableId, StringComparer.Ordinal)
            .Where(g => g.Count() > 1)
            .ToDictionary(g => g.Key, g => g.Count(), StringComparer.Ordinal);

        writer.Page(matches, options.Offset, options.PageSize(200, writer, "scenarios"), "scenarios", scenario =>
        {
            var flags = scenario.Failed ? "FAIL" : scenario.Result.Length > 0 ? scenario.Result[..Math.Min(4, scenario.Result.Length)].ToLowerInvariant() : "";
            var repeats = shared.TryGetValue(scenario.StableId, out var count) ? $"  ×{count}" : "";
            var attempt = scenario.Attempt is > 1 ? $"  attempt {scenario.Attempt}" : "";
            writer.Line($"{scenario.Address,-5} {flags,-4} {scenario.DurationSeconds,6:0.##}s  {scenario.Interactions.Count,4} calls  "
                        + QueryWriter.OneLine(scenario.Name, 80) + repeats + attempt);
            if (scenario.Failed && scenario.ErrorMessage is { } message)
                writer.Line($"        {QueryWriter.OneLine(message, 100)}");
        }, options.RerunArgs(), scenario => new
        {
            address = scenario.Address,
            stableId = scenario.StableId,
            feature = scenario.FeatureName,
            scenario = scenario.Name,
            result = scenario.Result,
            failed = scenario.Failed,
            durationSeconds = scenario.DurationSeconds,
            interactions = scenario.Interactions.Count,
            // Full, not one-lined: the flattening exists so a listing stays one row per item, which is a
            // property of a terminal. A consumer that wants a summary can take one itself.
            errorMessage = scenario.ErrorMessage,
            attempt = scenario.Attempt,
            stableIdShared = shared.TryGetValue(scenario.StableId, out var repeats) ? repeats : 1,
            labels = scenario.Labels,
            categories = scenario.Categories,
            exampleValues = scenario.ExampleValues
        });

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
            writer.Count(stats.Count);
            return 0;
        }

        if (stats.Count == 0)
        {
            // Absence is the answer this command exists to give, so say it plainly rather than printing
            // nothing - and as a note rather than a row, or `--json` would answer the negative question
            // with an empty array and no explanation of it.
            writer.Note("no services were called");
            writer.Footer("nothing was captured for this scope — check the capture is attached, not that the test skipped the call");
            return 0;
        }

        if (!SortIsValid(options, ServiceSorts, error))
            return 2;

        var ordered = options.Sort switch
        {
            "duration" => stats.Values.OrderByDescending(s => s.TotalMs).ToList(),
            "bytes" => stats.Values.OrderByDescending(s => s.Bytes).ToList(),
            "errors" => stats.Values.OrderByDescending(s => s.Errors).ToList(),
            _ => stats.Values.OrderByDescending(s => s.Calls).ToList()
        };

        writer.Line($"{"service",-24} {"calls",5} {"errors",6} {"bytes",9} {"p50",8} {"max",8}  statuses");

        // Paged like every other listing rather than printed whole: it used to hand-roll its loop and its
        // footer, which is how it came to be the one listing that could overflow the budget with nothing
        // said about how to resume. The closing line is preserved because it says what a row count cannot.
        writer.Page(ordered, options.Offset, options.PageSize(200, writer, "services"), "services", entry =>
                writer.Line($"{QueryWriter.OneLine(entry.Name, 24),-24} {entry.Calls,5} {entry.Errors,6} "
                            + $"{QueryWriter.Size(entry.Bytes),9} {QueryWriter.Duration(entry.Median()),8} {QueryWriter.Duration(entry.MaxMs),8}  "
                            + entry.StatusSummary()),
            options.RerunArgs(),
            entry => new
            {
                service = entry.Name,
                calls = entry.Calls,
                errors = entry.Errors,
                bytes = entry.Bytes,
                medianMs = entry.Median(),
                maxMs = entry.MaxMs,
                totalMs = entry.TotalMs,
                statuses = entry.Statuses
            },
            $"{ordered.Count} services · a service missing here was never called");

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

            // Counted by LABEL, not by code: `services` prints `OK x9`, and a reader wants the name.
            // The error test uses the number, which is the whole point of the two fields.
            if (StatusOf(interaction).Text is { Length: > 0 } status)
            {
                _statuses[status] = _statuses.GetValueOrDefault(status) + 1;
                if (IsError(interaction.StatusCode, interaction.StatusText))
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

        /// <summary>
        /// The whole status mix, unsummarised and uncapped. <see cref="StatusSummary"/> renders a column;
        /// this is the fact behind it, which is what <c>--json</c> owes a consumer that wants to count.
        /// </summary>
        public IReadOnlyDictionary<string, int> Statuses => _statuses;
    }
}
