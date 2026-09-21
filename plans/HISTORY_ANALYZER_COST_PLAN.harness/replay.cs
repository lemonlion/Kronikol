#:project src/Kronikol/Kronikol.csproj
// Replays a ledger run by run: each run is analysed as the current one against the ledger as it stood
// when that run was appended (the analyzer does not cut "prior" at the current run, so the file is cut).
// One JSON line per run, everything HistoryVerdicts holds bar HistoryPoint.CallSet, which one side lacks.
using System.Diagnostics;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization.Metadata;
using Kronikol.History;

var lines = File.ReadAllLines(args[0]);
var json = new JsonSerializerOptions
{
    TypeInfoResolver = new DefaultJsonTypeInfoResolver
    {
        Modifiers =
        {
            info =>
            {
                for (var i = info.Properties.Count - 1; i >= 0; i--)
                    if (info.Properties[i].Name == "CallSet") info.Properties.RemoveAt(i);
            }
        }
    }
};

var aliases = args.Length > 2 ? HistoryAliases.Load(args[2]) : null;
using var output = new StreamWriter(args[1], false, new UTF8Encoding(false));
var soFar = new StringBuilder();
var analyse = TimeSpan.Zero;
int runs = 0, scenarios = 0, points = 0, named = 0;
var kinds = new SortedDictionary<string, int>(StringComparer.Ordinal);
foreach (var line in lines)
{
    soFar.Append(line).Append('\n');
    if (!line.Contains("\"t\":\"run\"")) continue;

    var ledger = HistoryLedgerReader.Parse(soFar.ToString(), window: 0).Ledger!;
    using var doc = JsonDocument.Parse(line);
    var id = doc.RootElement.GetProperty("id").GetString();
    var suite = doc.RootElement.TryGetProperty("suite", out var s) ? s.GetString() : null;
    var current = ledger.Runs(suite).Last(r => r.Id == id);
    var roster = ledger.Roster(current.RosterHash);
    if (roster is null) { output.WriteLine($"{{\"run\":\"{id}\",\"roster\":null}}"); continue; }
    var shapes = current.ShapesHash is { } hash ? ledger.Shapes(hash) : null;

    var watch = Stopwatch.StartNew();
    var verdicts = HistoryAnalyzer.Analyse(ledger, roster, current, new HistoryAnalysisOptions { ReportReordered = true }, aliases: aliases, shapes: shapes);
    analyse += watch.Elapsed;

    runs++;
    scenarios += verdicts.Scenarios.Count;
    points += verdicts.Scenarios.Sum(x => x.Points.Count);
    named += verdicts.Scenarios.Count(x => x.NewCalls.Count > 0 || x.GoneCalls.Count > 0);
    foreach (var pair in verdicts.Counts)
        kinds[pair.Key.ToString()] = kinds.GetValueOrDefault(pair.Key.ToString()) + pair.Value;
    output.WriteLine(JsonSerializer.Serialize(verdicts, json));
}

Console.WriteLine($"aliases {aliases?.Mappings.Count ?? 0}, runs {runs}, scenario analyses {scenarios}, points {points}, scenarios with named calls {named}, analyse total {analyse.TotalMilliseconds:F0} ms");
Console.WriteLine(string.Join(", ", kinds.Select(p => $"{p.Key} {p.Value}")));
