
namespace Kronikol.Query;

/// <summary>
/// The commands that answer "what happened" before anything is fetched: the run header, the scenario list,
/// and the per-service view — the only one that answers a negative question, which is why it earns its
/// place beside the others.
/// </summary>
public static partial class QueryCommand
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
        // Counted: the run's tests. A scenario that is not a test (an ingest's fold, a stack's start-up) is named and in no
        // count, as the report's band counts (plans/RUN_WINDOW_PLAN.md R1); its calls are the run's, so they are counted.
        var tests = scenarios.Where(s => !s.NotATest).ToList();
        var notATest = scenarios.Count - tests.Count;
        if (options.Count)
        {
            writer.Count(tests.Count);
            return 0;
        }

        var failed = tests.Count(s => s.Failed);
        var interactions = scenarios.Sum(s => s.Interactions.Count);

        // Five heterogeneous sections and no list, so `items` alone would be a lie: the per-feature rows
        // are the one repeated thing here, and the rest are named members beside them. Inert in text mode.
        var run = new
        {
            file = Path.GetFileName(index.Path),
            sizeBytes = index.FileLength,
            // The JSON the file holds: decoded from a compressed file, and the file's own size for a JSON one.
            jsonBytes = index.JsonLength,
            startTime = index.StartTime,
            endTime = index.EndTime,
            scenarios = tests.Count,
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
        };
        object runData = notATest > 0 ? QueryWriter.WithMember(run, "notATest", notATest) : run;
        // Only on a report that declares its parties (#190): every other report's summary is as it was.
        if (index.HasParties && index.ServiceId is { } serviceId)
            runData = QueryWriter.WithMember(runData, "serviceId", serviceId);
        writer.Data("run", runData);

        writer.Line($"{Path.GetFileName(index.Path)}  {QueryWriter.Size((int)Math.Min(index.FileLength, int.MaxValue))}"
                    + (index.Snapshot is null ? "" : $" on disk, {QueryWriter.Size((int)Math.Min(index.JsonLength, int.MaxValue))} as JSON")
                    + (index.KronikolVersion is { } v ? $"  Kronikol {v}" : ""));
        writer.Line($"{index.StartTime} → {index.EndTime}");
        if (index.HasParties)
        {
            var subjects = index.Parties.Where(p => p.Kind == "underTest").Select(p => p.Name).ToList();
            writer.Line($"service: {index.ServiceId ?? "no id declared"}"
                        + (subjects.Count > 0 ? $"  under test: {string.Join(", ", subjects)}" : "")
                        + "  (parties lists every party)");
        }
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
        writer.Line($"{tests.Count} scenarios{(notATest > 0 ? $" · {notATest} not a test" : "")} · {failed} failed · {interactions} interactions · {index.Bodies.Count} distinct bodies");
        writer.Line();

        foreach (var feature in scenarios.GroupBy(s => s.FeatureName))
        {
            var featureTests = feature.Where(s => !s.NotATest).ToList();
            var featureNotATest = feature.Count() - featureTests.Count;
            // Counted, not inferred from the failures: a scenario that was skipped or bypassed did not pass.
            var featureFailed = featureTests.Count(s => s.Failed);
            var featurePassed = featureTests.Count(s => s.Result.Equals("Passed", StringComparison.OrdinalIgnoreCase));
            var featureSkipped = featureTests.Count(s => s.Result.Equals("Skipped", StringComparison.OrdinalIgnoreCase)
                                                    || s.Result.Equals("SkippedAfterFailure", StringComparison.OrdinalIgnoreCase));
            var featureBypassed = featureTests.Count(s => s.Result.Equals("Bypassed", StringComparison.OrdinalIgnoreCase));
            // A feature of no test (a stack's start-up) says only that; one with tests says it after their counts.
            writer.Line(featureTests.Count == 0 && featureNotATest > 0
                ? $"{QueryWriter.OneLine(feature.Key, 80)}  {featureNotATest} not a test"
                : $"{QueryWriter.OneLine(feature.Key, 80)}  {featurePassed} passed"
                  + (featureSkipped > 0 ? $", {featureSkipped} skipped" : "")
                  + (featureBypassed > 0 ? $", {featureBypassed} bypassed" : "")
                  + (featureFailed > 0 ? $", {featureFailed} FAILED" : "")
                  + (featureNotATest > 0 ? $", {featureNotATest} not a test" : ""));
            var item = new
            {
                feature = feature.Key,
                total = featureTests.Count,
                passed = featurePassed,
                failed = featureFailed,
                skipped = featureSkipped,
                bypassed = featureBypassed
            };
            writer.Item(featureNotATest > 0 ? QueryWriter.WithMember(item, "notATest", featureNotATest) : item);
        }

        if (failed > 0)
        {
            writer.Line();
            writer.Line("Failed:");
            foreach (var scenario in tests.Where(s => s.Failed).Take(10))
                writer.Line($"  {scenario.Address}  {scenario.Name}"
                            + (scenario.ErrorMessage is { } e ? $"  — {QueryWriter.OneLine(e, 90)}" : ""));
            if (failed > 10)
                writer.Line($"  … {failed - 10} more · scenarios --result Failed");
            writer.Line("  → failures");

            // The same ten, and the same reason for the cap: this is the orientation view, and `failures`
            // is the one that pages. `failedTotal` says how many the ten came out of.
            writer.Data("failed", tests.Where(s => s.Failed).Take(10).Select(s => new
            {
                address = s.Address,
                stableId = s.StableId,
                feature = s.FeatureName,
                scenario = s.Name,
                errorMessage = s.ErrorMessage
            }));
            writer.Data("failedTotal", failed);
        }

        // Ranked by each scenario's own time when the run paid a first-call warm-up (plans/WARM_UP_PLAN.md): whichever
        // scenario went first carries the app's one-time cost, and ranking it first sends a reader to the wrong test.
        var warmed = scenarios.Where(s => s.WarmUpSeconds is > 0).ToArray();
        // Ties on the time left go to the longer scenario by wall time: a scenario that was all warm-up and a skipped one of
        // 0 s both leave none, and the skipped one, first in the file, stood at the top and dropped the whole block (until
        // 4.16.3), which shows only when a scenario took any time.
        // A scenario that is not a test spans no test's time: the start-up's minutes are not the slowest test.
        var slowest = tests.OrderByDescending(s => s.TimeLeftSeconds).ThenByDescending(s => s.DurationSeconds).Take(3).ToArray();
        if (slowest.Length > 0 && slowest[0].DurationSeconds > 0)
        {
            writer.Line();
            writer.Line(warmed.Length > 0 ? "Slowest, first-call warm-up left out:" : "Slowest:");
            foreach (var scenario in slowest)
                writer.Line($"  {scenario.Address}  {scenario.TimeLeftSeconds:0.##}s  {QueryWriter.OneLine(scenario.Name, 70)}");

            writer.Data("slowest", slowest.Select(s => s.WarmUpSeconds is { } warmUp
                ? (object)new
                {
                    address = s.Address,
                    durationSeconds = s.DurationSeconds,
                    warmUpSeconds = warmUp,
                    scenario = s.Name
                }
                : new
                {
                    address = s.Address,
                    durationSeconds = s.DurationSeconds,
                    scenario = s.Name
                }));
        }

        if (warmed.Length > 0)
        {
            var largest = warmed.OrderByDescending(s => s.WarmUpSeconds).First();
            var total = warmed.Sum(s => s.WarmUpSeconds!.Value);
            // Under ten milliseconds in milliseconds: a warm-up capped at a 3 ms BDDfy duration (#122) read "0s of its 0s".
            static string Time(double seconds) => seconds < 0.01 ? $"{seconds * 1000:0.#}ms" : $"{seconds:0.##}s";
            writer.Line($"First-call warm-up: {Time(total)} in {warmed.Length} scenario{(warmed.Length == 1 ? "" : "s")}; the most in {largest.Address}, "
                        + $"{Time(largest.WarmUpSeconds!.Value)} of its {Time(largest.DurationSeconds)} (flow {largest.Address})");
            writer.Data("warmUp", new
            {
                seconds = total,
                scenarios = warmed.Length,
                // The calls of the scenarios counted: one with no duration holds marks and no seconds.
                calls = warmed.Sum(s => s.Interactions.Count(i => i.WarmUp is not null)),
                largest = new { address = largest.Address, warmUpSeconds = largest.WarmUpSeconds, durationSeconds = largest.DurationSeconds }
            });
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

        // --slower-than reads each scenario's own time and the rows read wall time, so a scenario that took longer than
        // the ones listed can be missing from them: the answer says so (until 4.16.3 it did not).
        var warmedOut = options.SlowerThan is { } slowerThan
            ? scope.Count(s => !s.NotATest && s.DurationSeconds >= slowerThan && s.TimeLeftSeconds < slowerThan && MatchesBesidesTime(s, options))
            : 0;
        void NoteWarmedOut()
        {
            if (warmedOut > 0)
                writer.Note($"{warmedOut} scenario{(warmedOut == 1 ? " that took" : "s that took")} {options.SlowerThan:0.##}s or more "
                            + (warmedOut == 1 ? "is left out: the first-call warm-up it paid is not its own time"
                                : "are left out: the first-call warm-up they paid is not their own time"));
        }

        if (matches.Count == 0)
        {
            writer.Note("no scenarios matched");
            NoteWarmedOut();
            var notATest = index.Scenarios.Count(s => s.NotATest);
            writer.Footer($"{index.Scenarios.Count - notATest} scenarios in the report" + (notATest > 0 ? $" · {notATest} not a test" : ""));
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
            // A scenario that is not a test has no verdict to flag, and says what it is after its name.
            var flags = scenario.NotATest ? "" : scenario.Failed ? "FAIL" : scenario.Result.Length > 0 ? scenario.Result[..Math.Min(4, scenario.Result.Length)].ToLowerInvariant() : "";
            var notATest = scenario.NotATest ? "  not a test" : "";
            var repeats = shared.TryGetValue(scenario.StableId, out var count) ? $"  ×{count}" : "";
            var attempt = scenario.Attempt is > 1 ? $"  attempt {scenario.Attempt}" : "";
            // A call is its request, as `interactions` and `services` count it: the records count each half (the JSON's
            // `interactions`), and printed as calls they doubled every answered call.
            var calls = scenario.Interactions.Count(i => i.Type.Equals("Request", StringComparison.OrdinalIgnoreCase));
            // The row's time is wall time; what of it was the run's first-call warm-up follows, as --slower-than leaves it out.
            var warmUp = scenario.WarmUpSeconds is > 0 and var seconds ? $"  {seconds:0.##}s warm-up" : "";
            writer.Line($"{scenario.Address,-5} {flags,-4} {scenario.DurationSeconds,6:0.##}s  {calls,4} calls  "
                        + QueryWriter.OneLine(scenario.Name, 80) + notATest + repeats + attempt + warmUp);
            if (scenario.Failed && scenario.ErrorMessage is { } message)
                writer.Line($"        {QueryWriter.OneLine(message, 100)}");
        }, options.RerunArgs(), scenario => WithNotATest(scenario, WithWarmUp(scenario, new
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
            tags = scenario.EffectiveTags,
            exampleValues = scenario.ExampleValues
        })));
        NoteWarmedOut();

        return 0;
    }

    /// <summary>A scenario's item with <c>warmUpSeconds</c> when it paid the run's first-call warm-up, as the report
    /// writes it, and as it was otherwise.</summary>
    private static object WithWarmUp(ScenarioEntry scenario, object item) =>
        scenario.WarmUpSeconds is { } seconds ? QueryWriter.WithMember(item, "warmUpSeconds", seconds) : item;

    /// <summary>A scenario's item with <c>notATest</c> when it is not a test, as the report writes it, and as it was otherwise.</summary>
    private static object WithNotATest(ScenarioEntry scenario, object item) =>
        scenario.NotATest ? QueryWriter.WithMember(item, "notATest", true) : item;

    private static bool Matches(ScenarioEntry scenario, QueryOptions options) =>
        MatchesBesidesTime(scenario, options)
        // The scenario's own time: a scenario slow only because it paid the run's first-call warm-up is not slow, and one that
        // is not a test spans no test's time, as the summary's Slowest leaves it out.
        && !(options.SlowerThan is { } slower && (scenario.NotATest || scenario.TimeLeftSeconds < slower));

    private static bool MatchesBesidesTime(ScenarioEntry scenario, QueryOptions options)
    {
        // A scenario that is not a test has no verdict for a result to match.
        if (options.Result is { } result && (scenario.NotATest || !scenario.Result.Equals(result, StringComparison.OrdinalIgnoreCase)))
            return false;
        if (options.Failed && !scenario.Failed)
            return false;
        if (options.Feature is { } feature && !scenario.FeatureName.Contains(feature, StringComparison.OrdinalIgnoreCase))
            return false;
        if (options.Tag is { } tag
            && !scenario.RowTags.Any(t => t.Contains(tag, StringComparison.OrdinalIgnoreCase)))
            return false;
        if (options.Grep is { } grep && !scenario.Name.Contains(grep, StringComparison.OrdinalIgnoreCase))
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

        // A report that declares its parties (#190) says each service's id and kind, before statuses, which stays last
        // because it is the one column of variable width. Every other report's rows are as they were.
        var partyOf = index.HasParties ? PartyLookup(index) : null;
        writer.Line($"{"service",-24} {"calls",5} {"errors",6} {"bytes",9} {"p50",8} {"max",8}  "
                    + (partyOf is null ? "" : $"{"id",-20} {"kind",-10} ") + "statuses");

        // Paged like every other listing rather than printed whole: it used to hand-roll its loop and its
        // footer, which is how it came to be the one listing that could overflow the budget with nothing
        // said about how to resume. The closing line is preserved because it says what a row count cannot.
        var statusesCut = false;
        writer.Page(ordered, options.Offset, options.PageSize(200, writer, "services"), "services", entry =>
            {
                writer.Line($"{QueryWriter.OneLine(entry.Name, 24),-24} {entry.Calls,5} {entry.Errors,6} "
                            + $"{QueryWriter.Size(entry.Bytes),9} {QueryWriter.Duration(entry.Median()),8} {QueryWriter.Duration(entry.MaxMs),8}  "
                            + (partyOf is null ? "" : PartyColumns(partyOf, entry.Name))
                            + entry.StatusSummary());
                statusesCut |= entry.StatusesCut;
            },
            options.RerunArgs(),
            entry =>
            {
                object item = new
                {
                    service = entry.Name,
                    calls = entry.Calls,
                    errors = entry.Errors,
                    bytes = entry.Bytes,
                    medianMs = entry.Median(),
                    maxMs = entry.MaxMs,
                    totalMs = entry.TotalMs,
                    statuses = entry.Statuses
                };
                if (partyOf is null)
                    return item;
                partyOf.TryGetValue(entry.Name, out var party);
                return QueryWriter.WithMember(QueryWriter.WithMember(item, "id", party?.Id), "kind", party?.Kind ?? "unknown");
            },
            $"{ordered.Count} services · a service missing here was never called");

        // The column names a service's four commonest statuses; a "+N more" in it has an address, as every cut does.
        if (statusesCut && !writer.Json)
            writer.Note($"+N more: past its {MaxStatusesShown} commonest, `services --json` lists every status of a service");

        return 0;
    }

    /// <summary>A report's declared parties by name, ignoring case, as <c>services</c> groups its rows.</summary>
    private static Dictionary<string, ReportPartyEntry> PartyLookup(ReportIndex index)
    {
        var lookup = new Dictionary<string, ReportPartyEntry>(StringComparer.OrdinalIgnoreCase);
        foreach (var party in index.Parties)
            lookup.TryAdd(party.Name, party);
        return lookup;
    }

    private static string PartyColumns(Dictionary<string, ReportPartyEntry> lookup, string service) =>
        lookup.TryGetValue(service, out var party)
            ? $"{QueryWriter.OneLine(party.Id ?? "-", 20),-20} {party.Kind ?? "unknown",-10} "
            : $"{"-",-20} {"unknown",-10} ";

    /// <summary>
    /// <c>parties</c> (#190): every party, callers included, with its kind, how the kind is known, its id, the calls it
    /// made and received, and its note. A report that declares its parties is read as written; on any other the table is
    /// inferred from the calls, as the run's own writer would have, and the checks a declaring run reports as diagnostics
    /// are printed as notes. Calls no scenario owns are not in the index, so they are not counted.
    /// </summary>
    private static int Parties(ReportIndex index, QueryOptions options, QueryWriter writer, TextWriter error)
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

        Kronikol.Parties.PartyKind? only = null;
        if (options.Kind is { } kindText)
        {
            if (!Kronikol.Parties.PartiesFile.TryParseKind(kindText, out var parsed))
            {
                error.WriteLine($"Not a party kind: {kindText} (expected test, underTest, real, double or unknown)");
                return 2;
            }
            only = parsed;
        }

        static bool IsRequest(InteractionEntry i) => i.Type.Equals("Request", StringComparison.OrdinalIgnoreCase) && i.ServiceName.Length > 0 && i.CallerName.Length > 0;
        var made = new Dictionary<string, int>(StringComparer.Ordinal);
        var received = new Dictionary<string, int>(StringComparer.Ordinal);
        // The calls a test double answered, as their capturers saw (#190, R2): counted here, the row keeps its members.
        var doubled = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var interaction in scope.SelectMany(s => s.Interactions).Where(IsRequest))
        {
            made[interaction.CallerName] = made.GetValueOrDefault(interaction.CallerName) + 1;
            received[interaction.ServiceName] = received.GetValueOrDefault(interaction.ServiceName) + 1;
            if (interaction.TestDouble is not null)
                doubled[interaction.ServiceName] = doubled.GetValueOrDefault(interaction.ServiceName) + 1;
        }

        IReadOnlyList<Kronikol.Parties.Party> table;
        if (index.HasParties)
        {
            table = index.Parties.Select(p =>
            {
                var kind = p.Kind is { } k && Kronikol.Parties.PartiesFile.TryParseKind(k, out var parsed) ? parsed : Kronikol.Parties.PartyKind.Unknown;
                Kronikol.Parties.PartyKindSource? source = Kronikol.Parties.PartiesReport.TryParseSource(p.KindSource, out var how) ? how : null;
                return new Kronikol.Parties.Party(p.Name, p.Id, kind, kind == Kronikol.Parties.PartyKind.Unknown ? null : source, p.Note);
            }).ToList();
        }
        else
        {
            var calls = index.Scenarios.SelectMany(s => s.Interactions).Where(IsRequest)
                .Select(i => new Kronikol.Parties.PartyCall(i.CallerName, i.ServiceName, i.DependencyCategory, i.CallerDependencyCategory, i.AttributionSource, i.TestDouble));
            var inferred = Kronikol.Parties.PartyResolver.Resolve(calls, Kronikol.Parties.PartyDeclarations.None, Kronikol.Parties.PartyObservations.None);
            table = inferred.Parties;
            foreach (var note in inferred.Notes)
                writer.Note(note.Message);
        }

        var rows = table.Where(p => made.ContainsKey(p.Name) || received.ContainsKey(p.Name))
            .Where(p => only is null || p.Kind == only)
            .ToList();

        if (options.Count)
        {
            writer.Count(rows.Count);
            return 0;
        }

        var unknown = rows.Count(p => p.Kind == Kronikol.Parties.PartyKind.Unknown);
        var footer = (unknown > 0 ? $"{unknown} parties of unknown kind: declare them in Parties or .kronikol/parties.json" : $"{rows.Count} parties")
                     + (index.HasParties ? "" : " · the report declares nothing, so this table is inferred from the calls");
        if (rows.Count == 0)
        {
            writer.Note(only is null ? "no parties: nothing was captured for this scope" : $"no parties of kind {Kronikol.Parties.PartiesReport.KindName(only.Value)}");
            writer.Footer(footer);
            return 0;
        }

        writer.Line($"{"party",-24} {"kind",-10} {"how",-9} {"made",5} {"received",8}  id · note");
        writer.Page(rows, options.Offset, options.PageSize(200, writer, "parties"), "parties", party =>
            {
                var tail = string.Join(" · ", new[] { party.Id, party.Note }.Where(t => t is not null));
                writer.Line($"{QueryWriter.OneLine(party.Name, 24),-24} {Kronikol.Parties.PartiesReport.KindName(party.Kind),-10} "
                            + $"{(party.KindSource is { } how ? Kronikol.Parties.PartiesReport.SourceName(how) : "-"),-9} "
                            + $"{made.GetValueOrDefault(party.Name),5} {received.GetValueOrDefault(party.Name),8}  {QueryWriter.OneLine(tail, 80)}".TrimEnd());
            },
            options.RerunArgs(),
            party => new
            {
                party = party.Name,
                id = party.Id,
                kind = Kronikol.Parties.PartiesReport.KindName(party.Kind),
                kindSource = party.KindSource is { } how ? Kronikol.Parties.PartiesReport.SourceName(how) : null,
                callsMade = made.GetValueOrDefault(party.Name),
                callsReceived = received.GetValueOrDefault(party.Name),
                doubleCalls = doubled.GetValueOrDefault(party.Name),
                note = party.Note
            },
            footer);
        return 0;
    }

    /// <summary>How many kinds of status a row of <c>services</c> names; <c>--json</c> gives every one.</summary>
    private const int MaxStatusesShown = 4;

    private sealed class ServiceStats(string name)
    {
        // One time per call. The data file writes a call's durationMs on both of its records, so a call is timed by
        // whichever record carries it, once: until 4.7.3 only the response's was read, and a call recorded as one record
        // (the NDJSON ingest contract allows it, with the capturer's measurement) took no time here.
        private readonly Dictionary<string, double> _durationByCall = new(StringComparer.Ordinal);
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

            if (interaction.DurationMs is { } ms
                && (interaction.RequestResponseId is { Length: > 0 } call
                    ? _durationByCall.TryAdd(call, ms)
                    : interaction.Type.Equals("Response", StringComparison.OrdinalIgnoreCase)))
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

        /// <summary>
        /// The status column: the four most frequent kinds, and how many more there are, which the column used to leave
        /// unsaid (#161, plans/LIST_CAPS_PLAN.md R2). <see cref="Statuses"/> holds every one, and <c>services --json</c>
        /// prints it.
        /// </summary>
        public string StatusSummary()
        {
            if (_statuses.Count == 0)
                return "";
            var shown = string.Join(" ", _statuses.OrderByDescending(s => s.Value).Take(MaxStatusesShown).Select(s => $"{s.Key}×{s.Value}"));
            return _statuses.Count > MaxStatusesShown ? $"{shown} +{_statuses.Count - MaxStatusesShown} more" : shown;
        }

        /// <summary>Whether <see cref="StatusSummary"/> leaves kinds of status out.</summary>
        public bool StatusesCut => _statuses.Count > MaxStatusesShown;

        /// <summary>
        /// The whole status mix, unsummarised and uncapped. <see cref="StatusSummary"/> renders a column;
        /// this is the fact behind it, which is what <c>--json</c> owes a consumer that wants to count.
        /// </summary>
        public IReadOnlyDictionary<string, int> Statuses => _statuses;
    }
}
