#:project ../../../src/Kronikol/Kronikol.csproj
#:property PublishAot=false

// The premise tested on the ledger's own version move (4 -> 6, 2026-10-19, adjacent runs, Kronikol 4.32.0):
// a shared form S both sides' stored lines translate to, then each position's set fingerprint compared under S.
//  rule 6: a document head's booleans {b}, signs and scalar-list lengths (InteractionShape's regexes, copied);
//  move 5: MongoDB records the command, so a Find head { "filter" : X, ... } reads as X, and an Insert or
//          FindAndModify head (none under v4) is dropped. The line names no dependency category, so a MongoDB
//          line is known here by the consumer's service name, which a real translation could not rely on;
//  re-cut: a head cut at 120 characters is cut again at 100 on both sides (the old cut kept less of the text).

using System.Text.RegularExpressions;
using Kronikol.History;

var ledger = HistoryLedgerReader.Read(args[0], 0).Ledger!;
var deps = ledger.Suites.SelectMany(s => ledger.Runs(s)).SelectMany(r => r.Deps ?? []).ToHashSet(StringComparer.Ordinal);
var boolean = new Regex(@"(?<![0-9A-Za-z_$])(?:true|false)(?![0-9A-Za-z_])", RegexOptions.CultureInvariant);
var sign = new Regex(@"-(?=\{n\})", RegexOptions.CultureInvariant);
var repeated = new Regex(@"(?<=[\[,]\s*)(?<scalar>""\{v\}""|\{b\}|null|\{id\}|\{ts\}|\{n\}(?:\.\{n\})?)(?:\s*,\s*\k<scalar>(?=\s*[,\]]))+(?=\s*[,\]])", RegexOptions.CultureInvariant | RegexOptions.ExplicitCapture);

var variants = new (string Name, bool Rule6, bool Mongo, bool Recut)[]
{
    ("identity (no translation)", false, false, false),
    ("rule 6 only", true, false, false),
    ("rule 6 + MongoDB command", true, true, false),
    ("rule 6 + MongoDB command + re-cut", true, true, true),
};
foreach (var variant in variants)
{
    int both = 0, equal = 0;
    var residue = new Dictionary<string, int>(StringComparer.Ordinal);
    foreach (var suite in ledger.Suites)
    {
        var runs = ledger.Runs(suite);
        var a = runs.Last(r => r.ShapeVersion == 4); var b = runs.First(r => r.ShapeVersion == 6);
        var ra = ledger.Roster(a.RosterHash)!; var rb = ledger.Roster(b.RosterHash)!;
        var sa = ledger.Shapes(a.ShapesHash!)!; var sb = ledger.Shapes(b.ShapesHash!)!;
        for (var p = 0; p < rb.Count; p++)
        {
            var at = ra.IndexOf(rb.Ids[p], rb.Slots[p]);
            if (at < 0 || a.ShapeSetAt(at) == b.ShapeSetAt(p)) continue;   // only the positions the move changed
            both++;
            var la = a.CallSetAt(at)!.Select(i => S(sa.Calls[i], variant)).Distinct(StringComparer.Ordinal).OrderBy(x => x, StringComparer.Ordinal).ToArray();
            var lb = b.CallSetAt(p)!.Select(i => S(sb.Calls[i], variant)).Distinct(StringComparer.Ordinal).OrderBy(x => x, StringComparer.Ordinal).ToArray();
            if (InteractionShape.Hash8(string.Join("\n", la)) == InteractionShape.Hash8(string.Join("\n", lb))) { equal++; continue; }
            foreach (var l in la.Except(lb)) residue["v4 " + Cut(l)] = residue.GetValueOrDefault("v4 " + Cut(l)) + 1;
            foreach (var l in lb.Except(la)) residue["v6 " + Cut(l)] = residue.GetValueOrDefault("v6 " + Cut(l)) + 1;
        }
    }
    Console.WriteLine($"{variant.Name,-38} of {both} positions the move changed, equal under S: {equal} ({100.0 * equal / both:0.0}%)");
    if (variant.Recut || variant == variants[^1])
        foreach (var (l, n) in residue.OrderByDescending(kv => kv.Value)) Console.WriteLine($"    x{n} {l}");
}

string S(string line, (string Name, bool Rule6, bool Mongo, bool Recut) v)
{
    var pair = deps.Where(d => line.StartsWith(d + " ", StringComparison.Ordinal)).OrderByDescending(d => d.Length).FirstOrDefault();
    if (pair is null) return line;
    var rest = line[(pair.Length + 1)..];
    var sp = rest.IndexOf(" /", StringComparison.Ordinal);
    var last = rest.LastIndexOf(' ');
    if (sp < 0 || last <= sp) return line;
    var method = rest[..sp];
    var middle = rest[(sp + 1)..last];
    var status = rest[(last + 1)..];
    var sp2 = middle.IndexOf(' ');
    var uri = sp2 < 0 ? middle : middle[..sp2];
    var head = sp2 < 0 ? "" : middle[(sp2 + 1)..];
    if (head.Length > 0)
    {
        var isDocument = head.StartsWith('{') || head.StartsWith('[');
        if (v.Rule6 && isDocument)
            head = repeated.Replace(sign.Replace(boolean.Replace(head, "{b}"), ""), "${scalar}");
        if (v.Mongo && pair.EndsWith(">MongoDB", StringComparison.Ordinal))
        {
            if (head.StartsWith("{ \"filter\" : ", StringComparison.Ordinal)) head = Balanced(head, "{ \"filter\" : ".Length) ?? head;
            else if (method.StartsWith("Insert", StringComparison.Ordinal) || method.StartsWith("FindAndModify", StringComparison.Ordinal)) head = "";
        }
        if (v.Recut && (head.EndsWith('\u2026') || head.Length > 100)) head = head[..Math.Min(100, head.Length)];
    }
    return $"{pair} {method} {uri}{(head.Length > 0 ? " " + head : "")} {status}";
}

static string? Balanced(string text, int start)
{
    if (start >= text.Length || text[start] != '{') return null;
    var depth = 0; var inString = false;
    for (var i = start; i < text.Length; i++)
    {
        var c = text[i];
        if (inString) { if (c == '\\') i++; else if (c == '"') inString = false; continue; }
        if (c == '"') inString = true;
        else if (c == '{') depth++;
        else if (c == '}' && --depth == 0) return text[start..(i + 1)];
    }
    return null;   // the head was cut before the filter closed
}

static string Cut(string s) => s.Length > 150 ? s[..150] + "..." : s;
