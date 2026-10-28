#:project ../../../src/Kronikol/Kronikol.csproj
#:property PublishAot=false

// Where the v6 era's behaviour-changed verdicts come from: per run, how many positions changed set fingerprint
// against the previous run of the suite, and for the biggest step, which call lines appeared and disappeared.

using Kronikol.History;

var ledger = HistoryLedgerReader.Read(args[0], 0).Ledger!;
var steps = new List<(string Suite, HistoryRun Before, HistoryRun After, int Changed, int Both)>();
foreach (var suite in ledger.Suites)
{
    var runs = ledger.Runs(suite).Where(r => r.ShapeVersion == 6).ToList();
    for (var i = 1; i < runs.Count; i++)
    {
        var a = runs[i - 1]; var b = runs[i];
        var ra = ledger.Roster(a.RosterHash)!; var rb = ledger.Roster(b.RosterHash)!;
        int changed = 0, both = 0;
        for (var p = 0; p < rb.Count; p++)
        {
            var at = ra.IndexOf(rb.Ids[p], rb.Slots[p]);
            if (at < 0) continue;
            both++;
            if (a.ShapeSetAt(at) != b.ShapeSetAt(p)) changed++;
        }
        steps.Add((suite!, a, b, changed, both));
    }
}
Console.WriteLine("v6 steps with the most changed positions (suite, from -> to, commits):");
foreach (var s in steps.OrderByDescending(s => s.Changed).Take(6))
    Console.WriteLine($"  {s.Suite,-24} {s.Before.At:MM-dd HH:mm} -> {s.After.At:MM-dd HH:mm}  {s.Before.Commit?[..7]} -> {s.After.Commit?[..7]}  changed {s.Changed}/{s.Both}");
var byDay = steps.GroupBy(s => s.After.At.ToString("MM-dd HH")).Select(g => (g.Key, Sum: g.Sum(s => s.Changed))).Where(x => x.Sum > 0).OrderBy(x => x.Key);
Console.WriteLine("changed positions per step time (all suites): " + string.Join(", ", byDay.Select(x => $"{x.Key}h:{x.Sum}")));
var biggest = steps.GroupBy(s => s.After.Commit).OrderByDescending(g => g.Sum(s => s.Changed)).First();
Console.WriteLine($"biggest commit step: -> {biggest.Key?[..7]} changed {biggest.Sum(s => s.Changed)} positions across {biggest.Count()} suites");
var gone = new Dictionary<string, int>(StringComparer.Ordinal); var appeared = new Dictionary<string, int>(StringComparer.Ordinal);
foreach (var s in biggest)
{
    var sa = ledger.Shapes(s.Before.ShapesHash!)!; var sb = ledger.Shapes(s.After.ShapesHash!)!;
    var ra = ledger.Roster(s.Before.RosterHash)!; var rb = ledger.Roster(s.After.RosterHash)!;
    for (var p = 0; p < rb.Count; p++)
    {
        var at = ra.IndexOf(rb.Ids[p], rb.Slots[p]);
        if (at < 0 || s.Before.ShapeSetAt(at) == s.After.ShapeSetAt(p)) continue;
        var la = s.Before.CallSetAt(at)!.Select(i => sa.Calls[i]).ToHashSet(StringComparer.Ordinal);
        var lb = s.After.CallSetAt(p)!.Select(i => sb.Calls[i]).ToHashSet(StringComparer.Ordinal);
        foreach (var l in la.Except(lb)) gone[l] = gone.GetValueOrDefault(l) + 1;
        foreach (var l in lb.Except(la)) appeared[l] = appeared.GetValueOrDefault(l) + 1;
    }
}
Console.WriteLine("  lines gone (positions):");
foreach (var (l, n) in gone.OrderByDescending(kv => kv.Value).Take(8)) Console.WriteLine($"    x{n} {Cut(l)}");
Console.WriteLine("  lines new (positions):");
foreach (var (l, n) in appeared.OrderByDescending(kv => kv.Value).Take(8)) Console.WriteLine($"    x{n} {Cut(l)}");
static string Cut(string s) => s.Length > 200 ? s[..200] + "..." : s;
