// Replays a history ledger run by run and writes what the analyzer said about each run as CSV, so a
// change to the analyzer can be read against every run a real ledger holds: build this at the base
// commit and at the change, replay the same ledger with both, diff the two files.
//
// Each run is analysed against the ledger AS IT STOOD when the run was appended: the file is cut at
// the run's own line, because the analyzer takes as "prior" every run of the stream but the current
// one and does not cut there itself.
//
//   dotnet run -c Release --project tools/history-replay -- <ledger.jsonl> <out.csv> [--min-runs N] [--last N] [--detail]
//   dotnet run -c Release --project tools/history-replay -- --bench [scenarios] [runs]
//
// --bench     no ledger: a synthetic stream (default 5,000 scenarios over 50 earlier runs, durations
//             within 20% of each scenario's own usual), one analysis of the latest run timed seven times.
//             Wall-clock: compare two builds in the same session on an idle machine, never across sessions.
//
// --min-runs  the consumer's HistoryMinRuns (default: the analyzer's)
// --last      replay only the last N run lines of the file
// --detail    a second file beside the CSV, <out>.detail.txt: every verdict that is not stable, with
//             its evidence, and every scenario's duration over its bar
using System.Globalization;
using System.Text;
using System.Text.Json;
using Kronikol.History;

if (args.Length >= 1 && args[0] == "--bench")
{
    var scenarioCount = args.Length > 1 ? int.Parse(args[1], CultureInfo.InvariantCulture) : 5000;
    var runCount = args.Length > 2 ? int.Parse(args[2], CultureInfo.InvariantCulture) : 50;
    var random = new Random(75);
    var benchRoster = HistoryRoster.Create("Bench", Enumerable.Range(0, scenarioCount).Select(i => new HistoryRosterEntry($"{i:x16}", "Scenario " + i, "Feature " + (i % 50), null)).ToArray());
    var usual = Enumerable.Range(0, scenarioCount).Select(_ => random.Next(1, 2000)).ToArray();
    HistoryRun Synthetic(int n) => new()
    {
        Id = $"local:{n}", Suite = "Bench", Partial = false, At = new DateTimeOffset(2026, 9, 1, 0, 0, 0, TimeSpan.Zero).AddHours(n),
        Branch = null, Commit = null, Provider = null, Url = null, Shards = 1, RosterHash = benchRoster.Hash,
        Results = new string('P', scenarioCount), Attempts = new string('1', scenarioCount),
        Durations = usual.Select(u => (int?)Math.Max(0, (int)(u * (0.8 + random.NextDouble() * 0.4)))).ToArray(),
        Errors = new string?[scenarioCount], ShapeSet = null, ShapeOrdered = null, Calls = null, ShapeVersion = null,
        ErrorText = new Dictionary<string, string>(), Deps = []
    };
    var file = new StringBuilder(HistoryJson.HeaderLine("bench") + "\n" + HistoryJson.RosterLine(benchRoster) + "\n");
    for (var n = 1; n <= runCount; n++) file.Append(HistoryJson.RunLine(Synthetic(n))).Append('\n');
    var benchLedger = HistoryLedgerReader.Parse(file.ToString(), window: 0).Ledger!;
    var latest = Synthetic(runCount + 1);
    var timings = new List<double>();
    for (var attempt = 0; attempt < 7; attempt++)
    {
        var watch = System.Diagnostics.Stopwatch.StartNew();
        var result = HistoryAnalyzer.Analyse(benchLedger, benchRoster, latest, new HistoryAnalysisOptions());
        watch.Stop();
        timings.Add(watch.Elapsed.TotalMilliseconds);
        if (attempt == 0) Console.WriteLine($"{result.Scenarios.Count} scenarios against {result.RunsRecorded} runs, pace of the latest {Pace(result.Runs[^1])}");
    }
    timings.Sort();
    Console.WriteLine(string.Create(CultureInfo.InvariantCulture, $"analyse: median {timings[3]:0} ms, min {timings[0]:0} ms, max {timings[^1]:0} ms (first attempt included)"));
    return 0;
}

if (args.Length < 2)
{
    Console.Error.WriteLine("usage: history-replay <ledger.jsonl> <out.csv> [--min-runs N] [--last N] [--detail]");
    return 2;
}

int? minRuns = null;
var last = int.MaxValue;
var detail = false;
for (var i = 2; i < args.Length; i++)
    switch (args[i])
    {
        case "--min-runs": minRuns = int.Parse(args[++i], CultureInfo.InvariantCulture); break;
        case "--last": last = int.Parse(args[++i], CultureInfo.InvariantCulture); break;
        case "--detail": detail = true; break;
        default: Console.Error.WriteLine($"unknown argument {args[i]}"); return 2;
    }

var options = new HistoryAnalysisOptions { ReportReordered = true };
if (minRuns is { } m) options = options with { MinRuns = m };

var lines = File.ReadAllLines(args[0]).Where(l => l.Length > 0).ToArray();
var runLines = lines.Select((text, index) => (text, index)).Where(x => x.text.Contains("\"t\":\"run\"", StringComparison.Ordinal)).ToArray();
var kinds = Enum.GetValues<HistoryVerdictKind>();

var csv = new StringBuilder();
csv.Append("suite,stream,run,partial,scenarios,prior,absent,pace,degraded");
foreach (var kind in kinds) csv.Append(',').Append(kind);
csv.AppendLine(",bars");
var text = new StringBuilder();
var soFar = new StringBuilder();
var next = 0;
var replayed = 0;
var ordinal = 0;

foreach (var (line, index) in runLines)
{
    for (; next <= index; next++) soFar.Append(lines[next]).Append('\n');
    if (runLines.Length - ordinal++ > last) continue;

    using var doc = JsonDocument.Parse(line);
    var id = doc.RootElement.GetProperty("id").GetString();
    var suite = doc.RootElement.TryGetProperty("suite", out var s) ? s.GetString() : null;
    var ledger = HistoryLedgerReader.Parse(soFar.ToString(), window: 0).Ledger!;
    var current = ledger.Runs(suite).Last(r => r.Id == id);
    if (ledger.Roster(current.RosterHash) is not { } roster) continue;
    var shapes = current.ShapesHash is { } hash ? ledger.Shapes(hash) : null;

    var verdicts = HistoryAnalyzer.Analyse(ledger, roster, current, options, shapes: shapes);
    replayed++;

    // The bars as one number: a p95 that moves without a verdict moving is still seen in the diff.
    var bars = verdicts.Scenarios.Sum(x => (long)(x.DurationP95 ?? 0));
    var run = verdicts.Runs[^1];
    csv.Append(Csv(suite)).Append(',').Append(Csv(verdicts.Stream)).Append(',').Append(Csv(id)).Append(',')
        .Append(verdicts.Partial ? "1" : "0").Append(',')
        .Append(roster.Count.ToString(CultureInfo.InvariantCulture)).Append(',')
        .Append(verdicts.RunsRecorded.ToString(CultureInfo.InvariantCulture)).Append(',')
        .Append(verdicts.Absent.Count.ToString(CultureInfo.InvariantCulture)).Append(',')
        .Append(Pace(run)).Append(',').Append(Degraded(run) ? "1" : "0");
    foreach (var kind in kinds) csv.Append(',').Append(verdicts.Counts.GetValueOrDefault(kind).ToString(CultureInfo.InvariantCulture));
    csv.Append(',').Append(bars.ToString(CultureInfo.InvariantCulture)).AppendLine();

    if (!detail) continue;
    text.AppendLine(CultureInfo.InvariantCulture, $"=== {suite} {id} partial={verdicts.Partial} scenarios={roster.Count} prior={verdicts.RunsRecorded} pace={Pace(run)}{(Degraded(run) ? " DEGRADED" : "")}");
    foreach (var scenario in verdicts.Scenarios.Where(x => x.Primary != HistoryVerdictKind.Stable).OrderBy(x => x.StableId, StringComparer.Ordinal).ThenBy(x => x.Slot))
    {
        text.AppendLine(CultureInfo.InvariantCulture, $"  [{scenario.VerdictNames}] {scenario.Feature} / {scenario.Name}");
        text.AppendLine(CultureInfo.InvariantCulture, $"      {scenario.Evidence}");
    }
    text.AppendLine("bars: " + string.Join(" ", verdicts.Scenarios.Select(x => $"{x.DurationMs?.ToString(CultureInfo.InvariantCulture) ?? "-"}/{x.DurationP95?.ToString(CultureInfo.InvariantCulture) ?? "-"}")));
}

File.WriteAllText(args[1], csv.ToString());
if (detail) File.WriteAllText(args[1] + ".detail.txt", text.ToString());
Console.WriteLine($"{replayed} run(s) replayed of {runLines.Length} in {args[0]}");
return 0;

static string Csv(string? value) => value is null ? "" : value.Contains(',') || value.Contains('"') ? "\"" + value.Replace("\"", "\"\"") + "\"" : value;

// Pace and the degraded label arrive with plans/HISTORY_VERDICT_NOISE_PLAN.md S4. Read by reflection so
// the same source builds at a base commit from before them, which is the whole use of this tool.
static string Pace(RunPoint run) =>
    typeof(RunPoint).GetProperty("Pace")?.GetValue(run) is double pace ? pace.ToString("0.00", CultureInfo.InvariantCulture) : "";

static bool Degraded(RunPoint run) => typeof(RunPoint).GetProperty("Degraded")?.GetValue(run) is true;
