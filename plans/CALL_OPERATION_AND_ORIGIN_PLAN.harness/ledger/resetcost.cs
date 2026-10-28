#:project ../../../src/Kronikol/Kronikol.csproj
#:property PublishAot=false

// Task 5: what a change of templating rule costs the behaviour verdicts, read with the library's own
// analyzer (HistoryAnalyzer.Analyse, default HistoryAnalysisOptions: window 50, min runs 5, alternating 10).
//  1. The latest run of every suite: the behaviour memory it holds under SameRule.
//  2. A move now: the latest run read as shapeVersion 7 (the quiet run).
//  3. The real 4 -> 6 move (2026-10-19): every v6 run read as it was, and against a counterfactual in which the
//     v4 runs count as the same rule (what an exact translation would have given the scenarios the move left alone).
//  4. A pure relabel K runs back (fingerprints unchanged, version 7 from there on): the gate's cost alone.

using Kronikol.History;

var path = args[0];
var k = args.Length > 1 ? int.Parse(args[1]) : 10;
var text = File.ReadAllText(path).Replace("\r\n", "\n");
var rawLines = text.Split('\n');
var options = new HistoryAnalysisOptions();

// ── 1. The latest run, as a run reads the ledger (window 50 per suite)
var asRun = HistoryLedgerReader.Parse(text, options.Window).Ledger!;
int tests = 0, withPrevious = 0, changedOnce = 0, changedTwice = 0, memoryTwoSets = 0, alternating = 0, unstable = 0, changedNow = 0, countVerdict = 0, quietNotes = 0;
var shapedDepth = new List<int>(); var otherRuleDepth = new List<int>();
foreach (var suite in asRun.Suites)
{
    var latest = asRun.LatestRun(suite)!;
    var v = Analyse(asRun, latest, options);
    foreach (var sc in v.Scenarios)
    {
        if (sc.Current == HistoryFormat.NotATest) continue;
        tests++;
        var cur = sc.Points[^1];
        var prior = sc.Points.Take(sc.Points.Count - 1).Where(p => !p.Partial && p.ShapeSet is { Length: > 0 }).ToList();
        bool Same(HistoryPoint p) => (p.ShapeVersion ?? 1) == (cur.ShapeVersion ?? 1) && string.Equals(p.ShapeRules, cur.ShapeRules, StringComparison.Ordinal);
        var shaped = prior.Where(Same).ToList();
        shapedDepth.Add(shaped.Count);
        otherRuleDepth.Add(prior.Count - shaped.Count);
        if (sc.PreviousShapeSet is not null) withPrevious++;
        var seq = shaped.Select(p => p.ShapeSet).Append(cur.ShapeSet).ToList();
        var changes = Enumerable.Range(1, seq.Count - 1).Count(i => seq[i] != seq[i - 1]);
        if (changes >= 1) changedOnce++;
        if (changes >= 2) changedTwice++;
        if (shaped.Where(p => p.Result == HistoryFormat.Passed).TakeLast(options.AlternatingRuns).Select(p => p.ShapeSet).Distinct().Count() >= 2) memoryTwoSets++;
        if (sc.Verdicts.Contains(HistoryVerdictKind.Alternating)) alternating++;
        if (sc.Verdicts.Contains(HistoryVerdictKind.UnstableShape)) unstable++;
        if (sc.Verdicts.Contains(HistoryVerdictKind.BehaviourChanged))
        {
            if (sc.Evidence.Contains("made a different number of times", StringComparison.Ordinal)) countVerdict++; else changedNow++;
        }
        if (sc.Evidence.Contains("fingerprinted by an earlier rule", StringComparison.Ordinal)) quietNotes++;
    }
}
Console.WriteLine($"== 1. latest run of {asRun.Suites.Count} suites: {tests} tests ==");
Console.WriteLine($"  same-rule shaped prior points per scenario: min {shapedDepth.Min()} median {Median(shapedDepth)} max {shapedDepth.Max()}; points under an earlier rule still in the window: median {Median(otherRuleDepth)} (sum {otherRuleDepth.Sum():N0})");
Console.WriteLine($"  with a previous shaped point: {withPrevious}; set changed at least once in the same-rule window: {changedOnce} (twice or more: {changedTwice}); 2+ sets in the alternating memory: {memoryTwoSets}");
Console.WriteLine($"  verdicts now: alternating {alternating}, unstable {unstable}, behaviour-changed by set {changedNow}, by count {countVerdict}; earlier-rule notes {quietNotes}");

// ── 2. A move now
int q = 0, qBehaviour = 0;
foreach (var suite in asRun.Suites)
{
    var latest = asRun.LatestRun(suite)!;
    var v = Analyse(asRun, latest with { ShapeVersion = 7 }, options);
    q += v.Scenarios.Count(s => s.Evidence.Contains("fingerprinted by an earlier rule", StringComparison.Ordinal));
    qBehaviour += v.Scenarios.Count(s => s.Verdicts.Overlaps(Behaviour));
}
Console.WriteLine($"== 2. the latest run as version 7: {q} scenarios read the earlier-rule note, {qBehaviour} keep a behaviour verdict ==");

// ── 3. The real 4 -> 6 move
var full = HistoryLedgerReader.Parse(text, 0).Ledger!;
var counterText = Rewrite(rawLines, run => run.ShapeVersion == 4 ? run with { ShapeVersion = 6 } : null);
var counter = HistoryLedgerReader.Parse(counterText, 0).Ledger!;
var touched = new HashSet<(string?, string, int)>();
foreach (var suite in full.Suites)
{
    var runs = full.Runs(suite);
    var lastOld = runs.Last(r => r.ShapeVersion == 4); var firstNew = runs.First(r => r.ShapeVersion == 6);
    var rOld = full.Roster(lastOld.RosterHash)!; var rNew = full.Roster(firstNew.RosterHash)!;
    for (var p = 0; p < rNew.Count; p++)
    {
        var at = rOld.IndexOf(rNew.Ids[p], rNew.Slots[p]);
        if (at >= 0 && lastOld.ShapeSetAt(at) != firstNew.ShapeSetAt(p)) touched.Add((suite, rNew.Ids[p], rNew.Slots[p]));
    }
}
var tally3 = Replay(full, counter, suite => full.Runs(suite).Where(r => r.ShapeVersion == 6).ToList(), touched);
Console.WriteLine($"== 3. the real 4 -> 6 move: {tally3.Runs} v6 runs replayed ({tally3.Cells:N0} scenario readings; {touched.Count} scenarios the move changed) ==");
Print(tally3);

// ── 4. A pure relabel K runs back
var relabel = new HashSet<string>(StringComparer.Ordinal);
foreach (var suite in full.Suites) foreach (var r in full.Runs(suite).TakeLast(k)) relabel.Add((suite ?? "") + "|" + r.Id);
var movedText = Rewrite(rawLines, run => relabel.Contains((run.Suite ?? "") + "|" + run.Id) ? run with { ShapeVersion = 7 } : null);
var moved = HistoryLedgerReader.Parse(movedText, 0).Ledger!;
// Here the moved ledger is the actual history and the original the counterfactual (an exact translation).
var tally4 = Replay(moved, full, suite => moved.Runs(suite).TakeLast(k).ToList(), null);
Console.WriteLine($"== 4. a version move {k} runs back, fingerprints unchanged: {tally4.Runs} runs replayed ({tally4.Cells:N0} readings) ==");
Print(tally4);

static HistoryVerdicts Analyse(HistoryLedger ledger, HistoryRun run, HistoryAnalysisOptions options) =>
    HistoryAnalyzer.Analyse(ledger, ledger.Roster(run.RosterHash)!, run, options, shapes: run.ShapesHash is { } h ? ledger.Shapes(h) : null);

static string Rewrite(string[] lines, Func<HistoryRun, HistoryRun?> change)
{
    var sb = new System.Text.StringBuilder();
    foreach (var line in lines)
    {
        if (line.Length == 0) continue;
        if (line.StartsWith("{\"t\":\"run\"", StringComparison.Ordinal) && HistoryJson.Parse(line).Run is { } run && change(run) is { } changed)
            sb.Append(HistoryJson.RunLine(changed)).Append('\n');
        else
            sb.Append(line).Append('\n');
    }
    return sb.ToString();
}

static Tally Replay(HistoryLedger actual, HistoryLedger counterfactual, Func<string?, List<HistoryRun>> runsOf, HashSet<(string?, string, int)>? touched)
{
    var t = new Tally();
    var options = new HistoryAnalysisOptions();
    foreach (var suite in actual.Suites)
    {
        var counterRuns = counterfactual.Runs(suite).ToDictionary(r => r.Id, StringComparer.Ordinal);
        foreach (var (run, ordinal) in runsOf(suite).Select((r, i) => (r, i)))
        {
            t.Runs++;
            var a = Analyse(actual, run, options);
            var b = Analyse(counterfactual, counterRuns[run.Id], options);
            for (var i = 0; i < a.Scenarios.Count; i++)
            {
                var sa = a.Scenarios[i]; var sb = b.Scenarios[i];
                if (sa.Current == HistoryFormat.NotATest) continue;
                t.Cells++;
                var key = touched is null ? "all" : touched.Contains((suite, sa.StableId, sa.Slot)) ? "move-changed" : "move-untouched";
                var ka = Kinds(sa); var kb = Kinds(sb);
                t.Add($"actual   {ka}", key);
                t.Add($"exact-tr {kb}", key);
                if (ka != kb) { t.Add($"DIFF {ka,-26} where translated {kb}", key); t.Add($"diff at run #{ordinal + 1}", key); }
                if (sa.Evidence.Contains("fingerprinted by an earlier rule", StringComparison.Ordinal)) t.Add("actual   quiet (earlier-rule note)", key);
            }
        }
    }
    return t;
}

static string Kinds(ScenarioHistory s)
{
    var kinds = s.Verdicts.Where(Behaviour.Contains).Select(v => v == HistoryVerdictKind.BehaviourChanged
        ? s.Evidence.Contains("made a different number of times", StringComparison.Ordinal) ? "changed-count" : "changed-set"
        : v == HistoryVerdictKind.Alternating ? "alternating" : "unstable").OrderBy(x => x).ToArray();
    return kinds.Length == 0 ? "none" : string.Join("+", kinds);
}

static void Print(Tally t)
{
    foreach (var group in t.Counts.GroupBy(kv => kv.Key.Item1.Split(' ')[0]))
        foreach (var kv in group.OrderBy(kv => kv.Key.Item1, StringComparer.Ordinal).ThenBy(kv => kv.Key.Item2, StringComparer.Ordinal))
            if (!kv.Key.Item1.EndsWith(" none", StringComparison.Ordinal) || kv.Key.Item1.StartsWith("DIFF", StringComparison.Ordinal))
                Console.WriteLine($"  {kv.Key.Item1,-62} [{kv.Key.Item2}] {kv.Value:N0}");
}

static int Median(List<int> xs) { var a = xs.OrderBy(x => x).ToArray(); return a.Length == 0 ? 0 : a[a.Length / 2]; }

partial class Program
{
    internal static readonly HashSet<HistoryVerdictKind> Behaviour = [HistoryVerdictKind.BehaviourChanged, HistoryVerdictKind.Alternating, HistoryVerdictKind.UnstableShape];
}

sealed class Tally
{
    public int Runs; public int Cells;
    public SortedDictionary<(string, string), int> Counts { get; } = new();
    public void Add(string what, string key) => Counts[(what, key)] = Counts.GetValueOrDefault((what, key)) + 1;
}
