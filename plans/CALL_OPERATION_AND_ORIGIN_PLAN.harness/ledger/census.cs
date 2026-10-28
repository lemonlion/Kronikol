#:project ../../../src/Kronikol/Kronikol.csproj
#:property PublishAot=false

// Census of a Kronikol history ledger (tasks 2-4): line kinds, runs per suite and stream, shapeVersion and
// shapeRules history, call-set coverage, the set-fingerprint rebuild from stored call lines, and the
// escape / GraphQL edge cases. Prints aggregates only.

using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using Kronikol.History;

var path = args.Length > 0 ? args[0] : throw new ArgumentException("usage: census.cs <history.jsonl>");
var raw = File.ReadAllLines(path);
Console.WriteLine($"file: {new FileInfo(path).Length:N0} bytes, {raw.Length:N0} lines");

var kinds = new SortedDictionary<string, (int Count, long Bytes)>(StringComparer.Ordinal);
var rosters = new Dictionary<string, HistoryRoster>(StringComparer.Ordinal);
var shapes = new Dictionary<string, HistoryShapes>(StringComparer.Ordinal);
var shapesLineNo = new Dictionary<string, int>(StringComparer.Ordinal);
var runs = new List<(int LineNo, HistoryRun Run, int Bytes, string Text)>();
var damaged = 0;
HistoryLine? header = null;
var lineNo = 0;
foreach (var l in raw)
{
    lineNo++;
    var line = l.TrimEnd('\r');
    if (line.Length == 0) continue;
    HistoryLine parsed;
    try { parsed = HistoryJson.Parse(line); }
    catch (FormatException) { damaged++; continue; }
    var bytes = Encoding.UTF8.GetByteCount(line) + 1;
    var k = parsed.Kind.ToString();
    kinds[k] = kinds.TryGetValue(k, out var c) ? (c.Count + 1, c.Bytes + bytes) : (1, bytes);
    switch (parsed.Kind)
    {
        case HistoryLineKind.Header: header = parsed; break;
        case HistoryLineKind.Roster: rosters[parsed.Roster!.Hash] = parsed.Roster; break;
        case HistoryLineKind.Shapes:
            if (!shapes.ContainsKey(parsed.Shapes!.Hash)) shapesLineNo[parsed.Shapes.Hash] = lineNo;
            shapes[parsed.Shapes.Hash] = parsed.Shapes; break;
        case HistoryLineKind.Run: runs.Add((lineNo, parsed.Run!, bytes, line)); break;
    }
}
Console.WriteLine($"header: historyFormatVersion {header?.Version}, generator {header?.Generator}; damaged lines {damaged}");
foreach (var (k, v) in kinds) Console.WriteLine($"  {k,-7} {v.Count,5} lines {v.Bytes,12:N0} bytes");

// ── Round trip: does the library's writer reproduce each run line byte for byte? (needed for the relabel replay)
var roundTripDiffs = runs.Count(r => !string.Equals(HistoryJson.RunLine(r.Run), r.Text, StringComparison.Ordinal));
Console.WriteLine($"run lines that do not round-trip through HistoryJson.RunLine: {roundTripDiffs}");

// ── Suites and streams
Console.WriteLine();
Console.WriteLine("== runs per suite (streams: runs) ==");
foreach (var suite in runs.GroupBy(r => r.Run.Suite ?? "(null)").OrderBy(g => g.Key, StringComparer.Ordinal))
{
    var streams = suite.GroupBy(r => r.Run.Stream).OrderByDescending(g => g.Count()).ToArray();
    var top = string.Join(", ", streams.Take(3).Select(s => $"{s.Key}: {s.Count()}"));
    var rest = streams.Length > 3 ? $", +{streams.Length - 3} other streams: {streams.Skip(3).Sum(s => s.Count())}" : "";
    Console.WriteLine($"  {suite.Key,-34} {suite.Count(),4} runs  {suite.Min(r => r.Run.At):yyyy-MM-dd}..{suite.Max(r => r.Run.At):yyyy-MM-dd}  roster {rosters.GetValueOrDefault(suite.Last().Run.RosterHash)?.Count}  [{top}{rest}]");
}
var allStreams = runs.GroupBy(r => r.Run.Stream).ToArray();
Console.WriteLine($"  streams overall: {allStreams.Length}; main: {runs.Count(r => r.Run.Stream == "main")}, other: {runs.Count(r => r.Run.Stream != "main")}");
Console.WriteLine($"  shards: {string.Join(", ", runs.GroupBy(r => r.Run.Shards).OrderBy(g => g.Key).Select(g => $"{g.Key}x{g.Count()}"))}; partial=true {runs.Count(r => r.Run.Partial == true)}, null {runs.Count(r => r.Run.Partial is null)}");
Console.WriteLine($"  rosters: {rosters.Count}; positions per roster {rosters.Values.Min(r => r.Count)}..{rosters.Values.Max(r => r.Count)}");

// ── shapeVersion / shapeRules over time
Console.WriteLine();
Console.WriteLine("== shapeVersion (runs, first..last at, file lines) ==");
foreach (var g in runs.GroupBy(r => r.Run.ShapeSet is null ? "none" : (r.Run.ShapeVersion ?? 1).ToString(CultureInfo.InvariantCulture)).OrderBy(g => g.Min(r => r.LineNo)))
    Console.WriteLine($"  v{g.Key,-5} {g.Count(),5} runs  {g.Min(r => r.Run.At):yyyy-MM-dd HH:mm}..{g.Max(r => r.Run.At):yyyy-MM-dd HH:mm}  lines {g.Min(r => r.LineNo)}..{g.Max(r => r.LineNo)}  explicit-key {g.Count(r => r.Text.Contains("\"shapeVersion\"", StringComparison.Ordinal))}");
Console.WriteLine("  moves, per suite in append order (from -> to @ first run under the new version):");
var moveCount = 0;
foreach (var suite in runs.GroupBy(r => r.Run.Suite ?? "(null)").OrderBy(g => g.Key, StringComparer.Ordinal))
{
    string? prev = null;
    var moves = new List<string>();
    foreach (var r in suite)
    {
        var v = r.Run.ShapeSet is null ? "none" : (r.Run.ShapeVersion ?? 1).ToString(CultureInfo.InvariantCulture);
        if (prev is not null && v != prev) { moves.Add($"{prev}->{v} {r.Run.At:MM-dd}"); moveCount++; }
        prev = v;
    }
    Console.WriteLine($"    {suite.Key,-34} {(moves.Count == 0 ? "(none)" : string.Join("; ", moves))}");
}
Console.WriteLine($"  total moves across suites: {moveCount}");
Console.WriteLine("== shapeRules ==");
foreach (var g in runs.GroupBy(r => r.Run.ShapeRules ?? "(none)"))
    Console.WriteLine($"  {g.Key}: {g.Count()} runs");

// ── Call sets
Console.WriteLine();
Console.WriteLine("== call lists ==");
var withCallSets = runs.Where(r => r.Run.CallSets is not null).ToList();
var withShapesHash = runs.Where(r => r.Run.ShapesHash is not null).ToList();
Console.WriteLine($"  run lines with callSets: {withCallSets.Count} of {runs.Count}; with a shapes hash: {withShapesHash.Count}; hash resolves: {withShapesHash.Count(r => shapes.ContainsKey(r.Run.ShapesHash!))}");
if (withCallSets.Count > 0)
    Console.WriteLine($"  first run with callSets: {withCallSets.Min(r => r.Run.At):yyyy-MM-dd HH:mm} (line {withCallSets.Min(r => r.LineNo)}); runs with shapeSet but no callSets after it: {runs.Count(r => r.Run.ShapeSet is not null && r.Run.CallSets is null && r.LineNo > withCallSets.Min(x => x.LineNo))}");
Console.WriteLine($"  shapes lines: {shapes.Count}; calls per list {shapes.Values.Min(s => s.Count)}..{shapes.Values.Max(s => s.Count)} (median {Median(shapes.Values.Select(s => s.Count))}); runs per list {string.Join(",", withShapesHash.GroupBy(r => r.Run.ShapesHash).Select(g => g.Count()).OrderByDescending(x => x).Take(12))}...");
var selfHashBad = shapes.Values.Count(s => !string.Equals(HistoryShapes.ComputeHash(s.Calls), s.Hash, StringComparison.Ordinal));
var unsorted = shapes.Values.Count(s => !s.Calls.SequenceEqual(s.Calls.Distinct(StringComparer.Ordinal).OrderBy(x => x, StringComparer.Ordinal), StringComparer.Ordinal));
Console.WriteLine($"  shapes lines not hashing their own calls: {selfHashBad}; not distinct+ordinal-sorted: {unsorted}");
var unionLines = new HashSet<string>(shapes.Values.SelectMany(s => s.Calls), StringComparer.Ordinal);
Console.WriteLine($"  distinct call lines across all shapes lines: {unionLines.Count}");
var callSetBytes = 0L;
foreach (var r in withCallSets) callSetBytes += r.Bytes - (Encoding.UTF8.GetByteCount(HistoryJson.RunLine(r.Run with { CallSets = null, ShapesHash = null })) + 1);
Console.WriteLine($"  bytes the callSets+shapes members add to run lines: {callSetBytes:N0} (+ shapes lines {kinds.GetValueOrDefault("Shapes").Bytes:N0})");

// ── Task 3: rebuild every set fingerprint from the stored lines
Console.WriteLine();
Console.WriteLine("== set fingerprint rebuilt from callSets + shapes vs stored shapeSet ==");
var emptyHash = InteractionShape.Hash8("");
long positions = 0, match = 0, blankStored = 0, unresolved = 0, badIndex = 0, unsortedIdx = 0, countBelowSet = 0, countZeroButSet = 0, hash8Disagree = 0;
var mismatch = new List<(string Run, string? Suite, int Pos, string Stored, string Rebuilt, int SetSize, int? Calls, int? Version, int Shards, string Class)>();
var byVersion = new SortedDictionary<int, (long Pos, long Match)>();
foreach (var (no, run, _, _) in withCallSets)
{
    if (run.ShapesHash is null || !shapes.TryGetValue(run.ShapesHash, out var list)) { unresolved++; continue; }
    var count = run.CallSets!.Count;
    for (var p = 0; p < count; p++)
    {
        var set = run.CallSets[p];
        var stored = run.ShapeSetAt(p);
        positions++;
        if (set.Any(i => i < 0 || i >= list.Count)) badIndex++;
        for (var i = 1; i < set.Count; i++) if (set[i] <= set[i - 1]) { unsortedIdx++; break; }
        var lines = set.Select(list.At).Where(x => x is not null).Select(x => x!).Distinct(StringComparer.Ordinal).OrderBy(x => x, StringComparer.Ordinal).ToArray();
        var joined = string.Join("\n", lines);
        var rebuilt = InteractionShape.Hash8(joined);
        var independent = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(joined)))[..8].ToLowerInvariant();
        if (rebuilt != independent) hash8Disagree++;
        if (run.CallsAt(p) is { } calls)
        {
            if (calls < lines.Length) countBelowSet++;
            if (calls == 0 && lines.Length > 0) countZeroButSet++;
        }
        var v = run.ShapeVersion ?? 1;
        var bv = byVersion.GetValueOrDefault(v);
        if (stored is null || stored.Length == 0) { blankStored++; byVersion[v] = (bv.Pos + 1, bv.Match); continue; }
        if (string.Equals(stored, rebuilt, StringComparison.Ordinal)) { match++; byVersion[v] = (bv.Pos + 1, bv.Match + 1); continue; }
        byVersion[v] = (bv.Pos + 1, bv.Match);
        var cls = set.Count == 0 && stored != emptyHash ? "empty call set, fingerprint of a non-empty set"
            : set.Count > 0 && stored == emptyHash ? "call set present, fingerprint of no calls"
            : "other";
        mismatch.Add((run.Id, run.Suite, p, stored, rebuilt, set.Count, run.CallsAt(p), run.ShapeVersion, run.Shards, cls));
    }
}
Console.WriteLine($"  positions checked: {positions:N0} in {withCallSets.Count} runs (unresolved shapes: {unresolved}); blank stored fingerprint: {blankStored}");
var comparable = positions - blankStored;
Console.WriteLine($"  MATCH {match:N0} of {comparable:N0} = {(comparable == 0 ? 0 : 100.0 * match / comparable):0.0000}%; mismatches {mismatch.Count}");
Console.WriteLine($"  per shapeVersion: {string.Join("; ", byVersion.Select(kv => $"v{kv.Key}: {kv.Value.Match:N0}/{kv.Value.Pos:N0}"))}");
Console.WriteLine($"  integrity: index out of range {badIndex}, unsorted index lists {unsortedIdx}, call count below distinct set {countBelowSet}, count 0 with calls {countZeroButSet}, Hash8 vs independent SHA-256 disagreements {hash8Disagree}");
foreach (var g in mismatch.GroupBy(m => m.Class))
{
    Console.WriteLine($"  mismatch class '{g.Key}': {g.Count()} positions in {g.Select(m => m.Run).Distinct().Count()} runs, suites {string.Join(",", g.Select(m => m.Suite).Distinct())}, shards {string.Join(",", g.Select(m => m.Shards).Distinct())}, versions {string.Join(",", g.Select(m => m.Version).Distinct())}");
    foreach (var m in g.Take(5))
        Console.WriteLine($"    {m.Run} s{m.Pos}: stored {m.Stored} rebuilt {m.Rebuilt} set {m.SetSize} calls {m.Calls}");
}

// ── Task 4: edge cases over the distinct call lines
Console.WriteLine();
Console.WriteLine("== edge cases over the distinct call lines ==");
var deps = new HashSet<string>(runs.SelectMany(r => r.Run.Deps ?? []), StringComparer.Ordinal);
Console.WriteLine($"  caller>service pairs in deps: {deps.Count}; with a space: {deps.Count(d => d.Contains(' '))}");
var pctEscape = new Regex("%[0-9A-Fa-f]{2}");
var cutEscape = new Regex(@"%\{(n|id|ts|bin)\}");
var parsedLines = unionLines.Select(Split).ToArray();
var unparsed = parsedLines.Count(p => p.Pair is null);
Console.WriteLine($"  lines parsed into pair/method/uri/rest/status by known deps prefix: {parsedLines.Length - unparsed} of {parsedLines.Length}");
var withEscape = parsedLines.Where(p => pctEscape.IsMatch(p.Line)).ToArray();
var escInUri = parsedLines.Where(p => p.Uri is not null && pctEscape.IsMatch(p.Uri)).ToArray();
var escInHead = parsedLines.Where(p => p.Head is { Length: > 0 } && pctEscape.IsMatch(p.Head)).ToArray();
Console.WriteLine($"  lines with a %XX escape anywhere: {withEscape.Length}; in the path+query: {escInUri.Length}; in a statement head: {escInHead.Length}");
var escapes = escInUri.SelectMany(p => pctEscape.Matches(p.Uri!).Select(m => m.Value.ToUpperInvariant())).GroupBy(x => x).OrderByDescending(g => g.Count());
Console.WriteLine($"  escapes in paths: {string.Join(", ", escapes.Select(g => $"{g.Key} x{g.Count()}"))}");
var cut = parsedLines.Where(p => cutEscape.IsMatch(p.Line)).ToArray();
Console.WriteLine($"  lines with %{{n}}/%{{id}}/%{{ts}}/%{{bin}} (an escape the templater cut): {cut.Length} [{string.Join(", ", cut.SelectMany(p => cutEscape.Matches(p.Line).Select(m => m.Value)).GroupBy(x => x).Select(g => $"{g.Key} x{g.Count()}"))}]");
Console.WriteLine($"  lines with {{bin}} anywhere: {parsedLines.Count(p => p.Line.Contains("{bin}", StringComparison.Ordinal))}");
// An escape kept whole that decodes to a separator next to an alphanumeric: unescape alone and unescape-then-template differ.
var retemplate = escInUri.Count(p => !string.Equals(Uri.UnescapeDataString(p.Uri!), InteractionShape.Template(Uri.UnescapeDataString(p.Uri!)), StringComparison.Ordinal));
Console.WriteLine($"  path lines whose unescaped path templates differently (translation must re-template): {retemplate}");
var graphql = parsedLines.Where(p => p.Line.Contains("/graphql", StringComparison.OrdinalIgnoreCase)).ToArray();
Console.WriteLine($"  GraphQL lines (/graphql): {graphql.Length}: {string.Join(" | ", graphql.Select(p => p.Pair + " " + p.Method + " " + p.Uri + " " + p.Status).GroupBy(x => x).Select(g => g.Key).Take(12))}");
var statementLines = parsedLines.Count(p => p.Head is { Length: > 0 });
var truncated = parsedLines.Count(p => p.Head is { Length: > 0 } h && h.Contains('\u2026'));
Console.WriteLine($"  lines with a statement head: {statementLines}; head cut at 120 chars (ends in an ellipsis): {truncated}");
Console.WriteLine($"  lines with no path found after the pair: {parsedLines.Count(p => p.Uri is null)}; methods holding a space: {parsedLines.Where(p => p.Method is not null).Select(p => p.Method!).Distinct().Count(m => m.Contains(' '))} of {parsedLines.Where(p => p.Method is not null).Select(p => p.Method!).Distinct().Count()} distinct");

// Latest run per suite: positions whose set holds an escaped / cut / GraphQL line
Console.WriteLine();
Console.WriteLine("== latest run per suite: positions whose set holds an affected line ==");
foreach (var suite in runs.GroupBy(r => r.Run.Suite ?? "(null)").OrderBy(g => g.Key, StringComparer.Ordinal))
{
    var latest = suite.Last().Run;
    if (latest.CallSets is null || latest.ShapesHash is null || !shapes.TryGetValue(latest.ShapesHash, out var list)) { Console.WriteLine($"  {suite.Key}: no call lists"); continue; }
    int esc = 0, gql = 0, cutp = 0, any = 0;
    for (var p = 0; p < latest.CallSets.Count; p++)
    {
        var ls = latest.CallSets[p].Select(list.At).Where(x => x is not null).ToArray();
        var e = ls.Any(x => pctEscape.IsMatch(x!)); var g = ls.Any(x => x!.Contains("/graphql", StringComparison.OrdinalIgnoreCase)); var c2 = ls.Any(x => cutEscape.IsMatch(x!));
        if (e) esc++; if (g) gql++; if (c2) cutp++; if (e || g || c2) any++;
    }
    Console.WriteLine($"  {suite.Key,-34} positions {latest.CallSets.Count,4}: escape {esc}, cut escape {cutp}, graphql {gql}, any {any}");
}

static int Median(IEnumerable<int> xs) { var a = xs.OrderBy(x => x).ToArray(); return a.Length == 0 ? 0 : a[a.Length / 2]; }

(string Line, string? Pair, string? Method, string? Uri, string? Head, string? Status) Split(string line)
{
    // caller>service is matched against the known deps pairs (longest first), since a name may hold a space.
    var pair = deps.Where(d => line.StartsWith(d + " ", StringComparison.Ordinal)).OrderByDescending(d => d.Length).FirstOrDefault();
    if (pair is null) return (line, null, null, null, null, null);
    var rest = line[(pair.Length + 1)..];
    // The method may hold spaces too (MongoDB "Find ← chef_notes", gRPC "Name (duplex-stream)", Kafka "Produce → topic"),
    // so the path is taken to start at the first " /" after the pair.
    var sp = rest.IndexOf(" /", StringComparison.Ordinal);
    if (sp < 0) return (line, pair, null, null, null, null);
    var method = rest[..sp];
    rest = rest[(sp + 1)..];
    var last = rest.LastIndexOf(' ');
    if (last < 0) return (line, pair, method, null, null, null);
    var status = rest[(last + 1)..];
    var middle = rest[..last];
    var sp2 = middle.IndexOf(' ');
    var uri = sp2 < 0 ? middle : middle[..sp2];
    var head = sp2 < 0 ? "" : middle[(sp2 + 1)..];
    return (line, pair, method, uri, head, status);
}
