#:project ../../../src/Kronikol/Kronikol.csproj
#:property PublishAot=false

// Task 6: what the call lists cost to load, with the library's own reader (HistoryLedgerReader.Read).
//   usage: loadtime.cs <history.jsonl> <work dir> [cold <variant>]
// The work dir receives a copy of the ledger without call lists (no shapes lines, no callSets/shapes members).
// Warm mode times every variant in one process (3 warm-ups, 15 timed); cold mode times one variant once, in a
// fresh process, as a test run pays it.

using System.Diagnostics;
using System.Text;
using Kronikol.History;

var path = args[0];
var work = args[1];
Directory.CreateDirectory(work);
var stripped = Path.Combine(work, "history-no-call-lists.jsonl");
if (!File.Exists(stripped))
{
    using var w = new StreamWriter(stripped, false, new UTF8Encoding(false)) { NewLine = "\n" };
    foreach (var line in File.ReadAllLines(path))
    {
        if (line.StartsWith("{\"t\":\"shapes\"", StringComparison.Ordinal)) continue;
        if (line.StartsWith("{\"t\":\"run\"", StringComparison.Ordinal) && HistoryJson.Parse(line).Run is { CallSets: not null } run)
        {
            w.WriteLine(HistoryJson.RunLine(run with { CallSets = null, ShapesHash = null }));
            continue;
        }
        w.WriteLine(line);
    }
}

var variants = new (string Name, Func<object> Act)[]
{
    ("read50  with call lists", () => Read(path, 50)),
    ("read50  without", () => Read(stripped, 50)),
    ("read50  + resolve every windowed call list + rebuild (naive)", () => Rebuild(Read(path, 50), memo: false)),
    ("read50  + rebuild memoised by stored fingerprint", () => Rebuild(Read(path, 50), memo: true)),
    ("read50  + analyse latest x18, with (re-reads file per suite)", () => AnalyseAll(Read(path, 50))),
    ("read50  + analyse latest x18, without (re-reads file per suite)", () => AnalyseAll(Read(stripped, 50))),
    ("read51  + analyse latest run x18, with call lists (no re-read)", () => AnalyseAll(Read(path, 51))),
    ("read51  + analyse latest run x18, without (no re-read)", () => AnalyseAll(Read(stripped, 51))),
    ("read0   (every run) with call lists", () => Read(path, 0)),
    ("read0   (every run) without", () => Read(stripped, 0)),
};

if (args.Length > 3 && args[2] == "cold")
{
    var v = variants[int.Parse(args[3])];
    var sw = Stopwatch.StartNew();
    var result = v.Act();
    sw.Stop();
    Console.WriteLine($"cold {v.Name}: {sw.Elapsed.TotalMilliseconds:0} ms {Describe(result)}");
    return;
}

Console.WriteLine($"ledger {new FileInfo(path).Length:N0} bytes; without call lists {new FileInfo(stripped).Length:N0} bytes");
foreach (var (name, act) in variants)
{
    for (var i = 0; i < 3; i++) act();
    var times = new List<double>();
    long allocated = 0;
    object? last = null;
    for (var i = 0; i < 15; i++)
    {
        GC.Collect(); GC.WaitForPendingFinalizers(); GC.Collect();
        var before = GC.GetAllocatedBytesForCurrentThread();
        var sw = Stopwatch.StartNew();
        last = act();
        sw.Stop();
        allocated = GC.GetAllocatedBytesForCurrentThread() - before;
        times.Add(sw.Elapsed.TotalMilliseconds);
    }
    times.Sort();
    Console.WriteLine($"  {name,-62} median {times[times.Count / 2],7:0.0} ms  min {times[0],7:0.0} ms  alloc {allocated / 1048576.0,6:0.0} MB  {Describe(last!)}");
}

static HistoryLedger Read(string file, int window) => HistoryLedgerReader.Read(file, window).Ledger!;

static object Rebuild(HistoryLedger ledger, bool memo)
{
    // Every position of every windowed run: its indices resolved to lines and its set fingerprint rebuilt, the
    // work a translation adds when it rebuilds the window's fingerprints. Memoised: a set fingerprint is a function
    // of its line set, so one rebuild per distinct (shapes list, stored fingerprint) is enough.
    var cache = new Dictionary<(string, string), string>();
    long positions = 0, rebuilt = 0, lines = 0;
    foreach (var suite in ledger.Suites)
        foreach (var run in ledger.Runs(suite))
        {
            if (run.CallSets is null || run.ShapesHash is null || ledger.Shapes(run.ShapesHash) is not { } shapes) continue;
            for (var p = 0; p < run.CallSets.Count; p++)
            {
                positions++;
                var key = (run.ShapesHash, run.ShapeSetAt(p) ?? "");
                if (memo && cache.ContainsKey(key)) continue;
                var set = run.CallSets[p];
                var resolved = new string[set.Count];
                for (var i = 0; i < set.Count; i++) resolved[i] = shapes.Calls[set[i]];
                lines += resolved.Length;
                var hash = InteractionShape.Hash8(string.Join("\n", resolved));
                rebuilt++;
                if (memo) cache[key] = hash;
            }
        }
    return (positions, rebuilt, lines);
}

static object AnalyseAll(HistoryLedger ledger)
{
    var options = new HistoryAnalysisOptions();
    var verdicts = 0;
    foreach (var suite in ledger.Suites)
    {
        var run = ledger.LatestRun(suite)!;
        var v = HistoryAnalyzer.Analyse(ledger, ledger.Roster(run.RosterHash)!, run, options, shapes: run.ShapesHash is { } h ? ledger.Shapes(h) : null);
        verdicts += v.Scenarios.Count;
    }
    return verdicts;
}

static string Describe(object o) => o switch
{
    HistoryLedger l => $"[parsed {l.Stats.LinesParsed}, runs {l.Stats.RunsKept}, shapes {l.Stats.ShapesKept}, reader {l.Stats.Elapsed.TotalMilliseconds:0} ms]",
    ValueTuple<long, long, long> t => $"[positions {t.Item1:N0}, rebuilt {t.Item2:N0}, lines joined {t.Item3:N0}]",
    int n => $"[scenarios {n}]",
    _ => ""
};
