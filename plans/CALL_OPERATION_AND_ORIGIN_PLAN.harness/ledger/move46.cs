#:project ../../../src/Kronikol/Kronikol.csproj
#:property PublishAot=false

// The real 4 -> 6 move in the ledger (2026-10-19): per suite, the last v4 run against the first v6 run.
// Which call lines exist only on one side, how many positions kept their set fingerprint, and whether
// the v6 lines can be computed from the v4 lines (the plan's premise for a translation).

using System.Text.RegularExpressions;
using Kronikol.History;

var path = args[0];
var ledger = HistoryLedgerReader.Read(path, 0).Ledger!;
var deps = ledger.Suites.SelectMany(s => ledger.Runs(s)).SelectMany(r => r.Deps ?? []).ToHashSet(StringComparer.Ordinal);
var onlyOld = new Dictionary<string, int>(StringComparer.Ordinal);
var onlyNew = new Dictionary<string, int>(StringComparer.Ordinal);
long same = 0, diff = 0, diffWithStatement = 0, diffNoStatement = 0, samePairs = 0;
var commits = new List<string>();
foreach (var suite in ledger.Suites.OrderBy(s => s, StringComparer.Ordinal))
{
    var runs = ledger.Runs(suite);
    var lastOld = runs.Last(r => r.ShapeVersion == 4);
    var firstNew = runs.First(r => r.ShapeVersion == 6);
    var iNew = runs.ToList().IndexOf(firstNew);
    var prevNew = runs[iNew - 1];
    commits.Add($"{suite}: v4 {lastOld.Commit?[..7]} -> v6 {firstNew.Commit?[..7]} (adjacent: {ReferenceEquals(prevNew, lastOld)})");
    var rOld = ledger.Roster(lastOld.RosterHash)!; var rNew = ledger.Roster(firstNew.RosterHash)!;
    var sOld = ledger.Shapes(lastOld.ShapesHash!)!; var sNew = ledger.Shapes(firstNew.ShapesHash!)!;
    foreach (var l in sOld.Calls.Except(sNew.Calls, StringComparer.Ordinal)) onlyOld[l] = onlyOld.GetValueOrDefault(l) + 1;
    foreach (var l in sNew.Calls.Except(sOld.Calls, StringComparer.Ordinal)) onlyNew[l] = onlyNew.GetValueOrDefault(l) + 1;
    for (var p = 0; p < rNew.Count; p++)
    {
        var at = rOld.IndexOf(rNew.Ids[p], rNew.Slots[p]);
        if (at < 0) continue;
        samePairs++;
        if (lastOld.ShapeSetAt(at) == firstNew.ShapeSetAt(p)) { same++; continue; }
        diff++;
        var a = lastOld.CallSetAt(at)!.Select(i => sOld.Calls[i]).ToHashSet(StringComparer.Ordinal);
        var b = firstNew.CallSetAt(p)!.Select(i => sNew.Calls[i]).ToHashSet(StringComparer.Ordinal);
        var changedLines = a.Except(b).Concat(b.Except(a)).ToArray();
        if (changedLines.Any(IsStatement)) diffWithStatement++; else diffNoStatement++;
    }
}
Console.WriteLine(string.Join("\n", commits.Take(3)) + $"\n... ({commits.Count} suites)");
Console.WriteLine($"positions present on both sides: {samePairs}; same set fingerprint across the move: {same}; different: {diff} (a statement line among the differences: {diffWithStatement}, none: {diffNoStatement})");
Console.WriteLine($"distinct lines only under v4: {onlyOld.Count}; only under v6: {onlyNew.Count}");
foreach (var g in onlyOld.Keys.GroupBy(Service)) Console.WriteLine($"  v4-only by service: {g.Key}: {g.Count()}");
foreach (var g in onlyNew.Keys.GroupBy(Service)) Console.WriteLine($"  v6-only by service: {g.Key}: {g.Count()}");
var full = args.Length > 1 && args[1] == "full";
Console.WriteLine("-- v4-only lines [suites holding it] (head cut to 150 chars):");
foreach (var l in onlyOld.Keys.OrderBy(x => x, StringComparer.Ordinal).Take(full ? 100 : 8)) Console.WriteLine($"  [{onlyOld[l]}] {Cut(l)} (len {l.Length})");
Console.WriteLine("-- v6-only lines [suites holding it]:");
foreach (var l in onlyNew.Keys.OrderBy(x => x, StringComparer.Ordinal).Take(full ? 100 : 8)) Console.WriteLine($"  [{onlyNew[l]}] {Cut(l)} (len {l.Length})");
Console.WriteLine("-- the positions that differ with no statement line among the differences: the differing lines");
var nonStatementDiffs = new Dictionary<string, int>(StringComparer.Ordinal);
foreach (var suite in ledger.Suites)
{
    var runs = ledger.Runs(suite);
    var lastOld = runs.Last(r => r.ShapeVersion == 4);
    var firstNew = runs.First(r => r.ShapeVersion == 6);
    var rOld = ledger.Roster(lastOld.RosterHash)!; var rNew = ledger.Roster(firstNew.RosterHash)!;
    var sOld = ledger.Shapes(lastOld.ShapesHash!)!; var sNew = ledger.Shapes(firstNew.ShapesHash!)!;
    for (var p = 0; p < rNew.Count; p++)
    {
        var at = rOld.IndexOf(rNew.Ids[p], rNew.Slots[p]);
        if (at < 0 || lastOld.ShapeSetAt(at) == firstNew.ShapeSetAt(p)) continue;
        var a = lastOld.CallSetAt(at)!.Select(i => sOld.Calls[i]).ToHashSet(StringComparer.Ordinal);
        var b = firstNew.CallSetAt(p)!.Select(i => sNew.Calls[i]).ToHashSet(StringComparer.Ordinal);
        var changed = a.Except(b).Select(x => "gone: " + x).Concat(b.Except(a).Select(x => "new:  " + x)).ToArray();
        if (changed.Any(x => IsStatement(x[6..]))) continue;
        foreach (var x in changed) nonStatementDiffs[x] = nonStatementDiffs.GetValueOrDefault(x) + 1;
        if (lastOld.ResultAt(at) != firstNew.ResultAt(p)) nonStatementDiffs["(result changed " + lastOld.ResultAt(at) + "->" + firstNew.ResultAt(p) + ")"] = nonStatementDiffs.GetValueOrDefault("(result changed " + lastOld.ResultAt(at) + "->" + firstNew.ResultAt(p) + ")") + 1;
    }
}
foreach (var (k, v) in nonStatementDiffs.OrderByDescending(kv => kv.Value)) Console.WriteLine($"  x{v} {Cut(k)}");

// Which statement-shaped services exist at all, and their head lengths
var latestShapes = ledger.Suites.Select(s => ledger.Shapes(ledger.LatestRun(s)!.ShapesHash!)!).SelectMany(s => s.Calls).Distinct(StringComparer.Ordinal).ToArray();
Console.WriteLine($"latest runs: {latestShapes.Length} distinct lines; by service: {string.Join(", ", latestShapes.GroupBy(Service).Select(g => $"{g.Key} {g.Count()}"))}");

bool IsStatement(string line)
{
    // A statement-shaped line has text between its path and its status: pair, method, path, head..., status.
    var pair = Pair(line);
    if (pair is null) return false;
    var rest = line[(pair.Length + 1)..].Split(' ');
    return rest.Length > 3;
}
string Service(string line) => Pair(line) is { } pair ? pair[(pair.IndexOf('>') + 1)..] : "?";
string? Pair(string line) => deps.Where(d => line.StartsWith(d + " ", StringComparison.Ordinal)).OrderByDescending(d => d.Length).FirstOrDefault();
static string Cut(string s) => s.Length > 230 ? s[..230] + "..." : s;
