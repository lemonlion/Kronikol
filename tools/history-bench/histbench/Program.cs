using System.Diagnostics;
using System.Text;
using System.Text.Json;

int scenarios = args.Length > 0 ? int.Parse(args[0]) : 5000;
int runs      = args.Length > 1 ? int.Parse(args[1]) : 50;
int window    = args.Length > 2 ? int.Parse(args[2]) : 50;
int extraRuns = args.Length > 3 ? int.Parse(args[3]) : 0; // lines beyond the window, to prove bounded reads
string path = Path.Combine(Path.GetTempPath(), $"hist-{scenarios}x{runs + extraRuns}.jsonl");

// ---- generate ----
if (!File.Exists(path))
{
    var rnd = new Random(11);
    using var w = new StreamWriter(path, false, new UTF8Encoding(false));
    w.Write("{\"t\":\"header\",\"historyFormatVersion\":1,\"window\":" + window + "}\n");
    var ids = new string[scenarios]; var names = new string[scenarios];
    for (var i = 0; i < scenarios; i++) { ids[i] = i.ToString("x16"); names[i] = "Scenario number " + i + " does a thing"; }
    w.Write("{\"t\":\"roster\",\"hash\":\"r1\",\"suite\":\"S\",\"ids\":" + JsonSerializer.Serialize(ids)
          + ",\"names\":" + JsonSerializer.Serialize(names) + "}\n");
    var res = new char[scenarios]; var dur = new int[scenarios]; var shp = new string[scenarios];
    for (var r = 0; r < runs + extraRuns; r++)
    {
        for (var i = 0; i < scenarios; i++)
        { res[i] = rnd.Next(100) < 4 ? 'F' : 'P'; dur[i] = rnd.Next(80, 9000); shp[i] = (i % 64).ToString("x4"); }
        w.Write("{\"t\":\"run\",\"id\":\"gh:" + r + ":1\",\"suite\":\"S\",\"branch\":\"main\",\"roster\":\"r1\",\"results\":\""
              + new string(res) + "\",\"durations\":" + JsonSerializer.Serialize(dur)
              + ",\"shapeSet\":" + JsonSerializer.Serialize(shp) + "}\n");
    }
}
var bytes = new FileInfo(path).Length;

// ---- read (window-bounded) + analyse ----
for (var pass = 0; pass < 3; pass++)
{
    var sw = Stopwatch.StartNew();
    var lines = new List<string>();
    long linesSeen = 0, linesParsed = 0;
    string? rosterLine = null;
    foreach (var line in File.ReadLines(path))
    {
        linesSeen++;
        if (line.StartsWith("{\"t\":\"roster\"")) { rosterLine = line; continue; }
        if (!line.StartsWith("{\"t\":\"run\"")) continue;
        lines.Add(line);
        if (lines.Count > window) lines.RemoveAt(0);   // keep last `window`
    }
    var readMs = sw.Elapsed.TotalMilliseconds;

    var results = new List<string>(lines.Count);
    var durations = new List<int[]>(lines.Count);
    foreach (var line in lines)
    {
        linesParsed++;
        using var doc = JsonDocument.Parse(line);
        results.Add(doc.RootElement.GetProperty("results").GetString()!);
        var d = doc.RootElement.GetProperty("durations");
        var arr = new int[d.GetArrayLength()]; var k = 0;
        foreach (var v in d.EnumerateArray()) arr[k++] = v.GetInt32();
        durations.Add(arr);
    }
    var parseMs = sw.Elapsed.TotalMilliseconds - readMs;

    int flaky = 0, failing = 0, broke = 0;
    for (var i = 0; i < scenarios; i++)
    {
        int flips = 0, fails = 0; char prev = '.';
        for (var r = 0; r < results.Count; r++)
        {
            var c = results[r][i];
            if (c == 'F') fails++;
            if (prev is 'P' or 'F' && c != prev) flips++;
            prev = c;
        }
        if (flips >= 2) flaky++;
        else if (fails == results.Count) failing++;
        if (results.Count >= 2 && results[^1][i] == 'F' && results[^2][i] == 'P') broke++;
    }
    var totalMs = sw.Elapsed.TotalMilliseconds;
    if (pass == 2)
        Console.WriteLine($"{scenarios,6} scenarios x {runs + extraRuns,3} lines (window {window,3})  file {bytes / 1024.0 / 1024.0,6:F2} MB  "
            + $"read {readMs,6:F1}ms  parse {parseMs,6:F1}ms  analyse {totalMs - readMs - parseMs,5:F1}ms  TOTAL {totalMs,6:F1}ms  "
            + $"[lines seen {linesSeen}, parsed {linesParsed}]  flaky={flaky} always-failing={failing} broke={broke}");
}
