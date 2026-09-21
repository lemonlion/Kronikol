// Replays a history ledger run by run and writes what the analyzer said about each run as CSV, so a
// change to the analyzer can be read against every run a real ledger holds: build this at the base
// commit and at the change, replay the same ledger with both, diff the two files.
//
// Each run is analysed against the ledger AS IT STOOD when the run was appended: the file is cut at
// the run's own line, because the analyzer takes as "prior" every run of the stream but the current
// one and does not cut there itself.
//
//   dotnet run -c Release --project tools/history-replay -- <ledger.jsonl> <out.csv> [--min-runs N] [--last N] [--detail]
//
// --min-runs  the consumer's HistoryMinRuns (default: the analyzer's)
// --last      replay only the last N run lines of the file
// --detail    a second file beside the CSV, <out>.detail.txt: every verdict that is not stable, with
//             its evidence, and every scenario's duration over its bar
using System.Globalization;
using System.Text;
using System.Text.Json;
using Kronikol.History;

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
