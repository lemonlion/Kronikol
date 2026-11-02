// Is the URI idempotent through what a merge does to it? The writer writes log.Uri.ToString(), the reader parses that
// with Uri.TryCreate(RelativeOrAbsolute) and falls back to about:blank, and history keys the parsed URI's ToString().
// So the run's line reads S0 = new Uri(x).ToString() and the merge's reads S1 = Uri.TryCreate(S0).ToString(). Every
// escape and a set of raw characters, in each place a tracker can put one. Published 5.27.13: nothing here touches
// ReadMethod, so the prototype reads the same. Run on each OS, since Uri parses a leading '/' as a file on Unix.
#:package Kronikol@5.27.13
#:property PublishAot=false

using System.Net;
using System.Runtime.InteropServices;
using System.Text;
using Kronikol.History;
using Kronikol.Tracking;

string LineOf(Uri uri, string? category = null)
{
    var id = Guid.NewGuid();
    var req = new RequestResponseLog("t", "t", "GET", null, uri, [], "S", "C", RequestResponseType.Request, id, id, false, DependencyCategory: category);
    var res = req with { Type = RequestResponseType.Response, StatusCode = HttpStatusCode.OK };
    return InteractionShape.Calls([req, res])[0].ToString();
}

var forms = new (string Name, string Text, string? Category)[]
{
    ("http mid path", "http://h/a{X}b", null),
    ("http end of path", "http://h/a{X}", null),
    ("http whole segment", "http://h/x/{X}/y", null),
    ("http query key", "http://h/a?k{X}=v", null),
    ("http query value", "http://h/a?k=v{X}", null),
    ("http fragment", "http://h/a#f{X}", null),
    ("custom scheme path", "custom://h/a{X}b", null),
    ("custom scheme end", "custom://h/a{X}", null),
    ("custom scheme authority", "custom://a{X}b/c", null),
    ("spanner path", "spanner://p/a{X}b", "Spanner"),
    ("s3 bucket", "s3://b{X}x/k", null),
    ("s3 key", "s3://bucket/k{X}x", null),
    ("servicebus queue", "servicebus://q{X}x/", "ServiceBus"),
    ("redis key", "redis://db0/k{X}y", null),
    ("urn", "urn:a:b{X}c", null),
    ("relative mid", "a{X}b", null),
    ("relative end", "a{X}", null),
    ("relative start", "{X}a", null),
    ("rooted relative mid", "/a{X}b", null),
    ("rooted relative end", "/a{X}", null),
    ("double-rooted relative", "//a{X}b/c", null),
};

var values = new List<string>();
for (var b = 0; b < 256; b++)
    values.Add("%" + b.ToString("X2"));
values.AddRange(["%C3%A9", "%C2%A0", "%E2%80%8E", "%E2%80%8F", "%E2%80%AA", "%E2%80%AE", "%EF%BB%BF", "%EF%BF%BD", "%F0%9F%98%80", "%E2%80%A8", "%25", "%2520", "%252F", "%5C%5C"]);
foreach (var c in " !#$&'()*+,-./:;<=>?@[]^_`{|}~")
    values.Add(c.ToString());
foreach (var cp in new[] { 34, 92, 9, 10, 13, 12, 1, 0x7F, 0xA0, 0xE9, 0x200E, 0x200F, 0x202A, 0x202E, 0x2028, 0xFEFF, 0xFFFD, 0x1F600 })
    values.Add(char.ConvertFromUtf32(cp));
values.AddRange([".", "..", "...", "%2E", "%2E%2E", "/../", "/./", "//"]);
values = values.Distinct().ToList();

Console.WriteLine($"OS: {RuntimeInformation.OSDescription}");
Console.WriteLine($"values per form: {values.Count}; forms: {forms.Length}");
var totals = new Dictionary<string, int> { ["cases"] = 0, ["rejected by new Uri"] = 0, ["S1 differs from S0"] = 0, ["line differs"] = 0, ["S0 does not parse (about:blank)"] = 0 };
var byForm = new List<string>();
foreach (var (name, text, category) in forms)
{
    var differs = new List<string>();
    var unparsed = new List<string>();
    var cases = 0;
    foreach (var x in values)
    {
        var input = text.Replace("{X}", x);
        totals["cases"]++;
        cases++;
        // A tracker that means a relative URI says so: on Unix RelativeOrAbsolute already reads a leading '/' as a file.
        var kind = name.Contains("relative") ? UriKind.Relative : UriKind.RelativeOrAbsolute;
        if (!Uri.TryCreate(input, kind, out var u0))
        {
            totals["rejected by new Uri"]++;
            continue;
        }
        string s0, line0;
        try { s0 = u0.ToString(); line0 = LineOf(u0, category); }
        catch (Exception e) { differs.Add($"{Show(x)}: ToString threw {e.GetType().Name}"); continue; }
        var parsed = Uri.TryCreate(s0, UriKind.RelativeOrAbsolute, out var u1);
        if (!parsed) { totals["S0 does not parse (about:blank)"]++; unparsed.Add(Show(x)); u1 = new Uri("about:blank"); }
        var s1 = u1!.ToString();
        if (s0 != s1) totals["S1 differs from S0"]++;
        var line1 = LineOf(u1, category);
        if (line0 != line1)
        {
            totals["line differs"]++;
            differs.Add($"{Show(x)}: [{Show(Target(line0))}] -> [{Show(Target(line1))}]");
        }
    }
    byForm.Add($"== {name} ({text}): {differs.Count} of {cases} lines differ" + (unparsed.Count > 0 ? $"; S0 unparseable: {string.Join(" ", unparsed)}" : ""));
    byForm.AddRange(differs.Take(70).Select(d => "   " + d));
    if (differs.Count > 70) byForm.Add($"   ... and {differs.Count - 70} more");
}
foreach (var (k, v) in totals)
    Console.WriteLine($"{k}: {v}");
foreach (var line in byForm)
    Console.WriteLine(line);

// The call line less its caller, service, verb and status.
static string Target(string line)
{
    var start = line.IndexOf(' ') + 1;
    start = line.IndexOf(' ', start) + 1;
    var end = line.LastIndexOf(' ');
    return end > start ? line[start..end] : line;
}

static string Show(string s)
{
    var b = new StringBuilder();
    foreach (var c in s)
    {
        if (c < 0x20 || c == 0x7f || c == 0x5C || (c >= 0x80 && !char.IsLetterOrDigit(c)))
            b.Append("<U+").Append(((int)c).ToString("X4")).Append('>');
        else
            b.Append(c);
    }
    return b.ToString();
}
