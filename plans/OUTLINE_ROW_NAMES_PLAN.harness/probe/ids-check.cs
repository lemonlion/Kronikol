#:package Kronikol@5.4.0
#:property PublishAot=false
// Two checks on the probe's run, from what Kronikol 5.4.0 wrote (results/before-5.4.0/ids.tsv, the Reqnroll lane) and
// what the same run's Cucumber messages give when ingested (results/cucumber-lane-5.4.0/ids.tsv: pickle names):
//  1. which inputs reproduce the stable ids 5.4.0 wrote for the Reqnroll rows;
//  2. that Gherkin's rule (one ordinal replace per column, left to right, on the result so far) applied to the title and
//     the row's values gives the pickle's name, and what each row's id would be if that name were hashed.
// Both tables come from `kronikol query scenarios --json --max-bytes 0` through tsv.py. Run from this folder:
// dotnet run --file ids-check.cs
using System.Text.Json;
using Kronikol.Reports;

static List<(string Feature, Dictionary<string, string>? Values, string Name, string Id)> Load(string path) =>
    File.ReadAllLines(path).Select(l => l.Split('\t')).Select(p => (p[0],
        p[1] is "null" or "{}" ? null : JsonSerializer.Deserialize<Dictionary<string, string>>(p[1]),
        p[2].Replace("\\n", "\n"), p[3])).ToList();

// The values in tsv.py's JSON are sorted by key, so the column order is lost there; take it from the title instead:
// a column's placeholder is filled in the order the columns were written, which for every probe outline is the order
// the placeholders first appear (checked by hand against Edges.feature; E02 is the one where the order matters).
static string Fill(string title, Dictionary<string, string> values, string[] columnOrder)
{
    foreach (var header in columnOrder)
        if (values.TryGetValue(header, out var value))
            title = title.Replace("<" + header + ">", value, StringComparison.Ordinal);
    return title;
}

// Header order of each probe outline as written in the feature files (Baskets.feature, Edges.feature).
var columns = new Dictionary<string, string[]>
{
    ["A basket is weighed on the UK scale (<case>)"] = ["case", "count", "grams"],
    ["E01 a title without a placeholder"] = ["a"],
    ["E02 <a> then <b>, a value that names a later column"] = ["a", "b"],
    ["E03 weighs <count> on <missing>"] = ["count"],
    ["E04 the <fruit name> in <größe> and <class>"] = ["fruit name", "größe", "class"],
    ["E05 <fruit> by block"] = ["fruit"],
    ["E06 the same name for <fruit>"] = ["fruit", "n"],
    ["E07 escapes [<v>]"] = ["v"],
    ["E08 markup <v>"] = ["v"],
    ["E10 <fruit> under a rule"] = ["fruit"],
    ["E11 <a> and <a> again"] = ["a"],
    ["<fruit> starts E12"] = ["fruit"],
    ["E13 <v>"] = ["v"],
};

var reqnroll = Load("results/before-5.4.0/ids.tsv");
var cucumber = Load("results/cucumber-lane-5.4.0/ids.tsv");
const string Suite = "Probe101.xUnit2";

foreach (var suite in new string?[] { null, Suite })
{
    var misses = reqnroll.Where(r => ScenarioStableId.Compute(suite, r.Feature, r.Name, r.Values is null ? null : r.Name, r.Values) != r.Id).ToList();
    Console.WriteLine($"suite {suite ?? "(none)"}: Compute(suite, feature, title, outlineId: title, values) reproduces {reqnroll.Count - misses.Count} of {reqnroll.Count}"
        + (misses.Count is > 0 and < 5 ? "; not: " + string.Join(", ", misses.Select(m => m.Name.Replace("\n", "\\n"))) : ""));
}
var plainMisses = reqnroll.Where(r => r.Values is null && ScenarioStableId.Compute(Suite, r.Feature, r.Name) != r.Id).Count();
Console.WriteLine($"plain scenarios (no outline): Compute(suite, feature, name) misses {plainMisses}");

Console.WriteLine();
Console.WriteLine("title | filled by Gherkin's rule | pickle name found | id 5.4.0 wrote | id if the filled name were hashed");
int moved = 0, matched = 0, rows = 0;
foreach (var r in reqnroll.Where(r => r.Values is not null))
{
    rows++;
    var filled = Fill(r.Name, r.Values!, columns[r.Name]);
    // ingest upper-cases a title's first letter by default, as a Reqnroll report's CapitaliseTitles does
    var shown = filled.Length > 0 ? char.ToUpperInvariant(filled[0]) + filled[1..] : filled;
    var pickle = cucumber.Any(c => c.Feature == r.Feature && c.Name == shown
        && c.Values!.OrderBy(e => e.Key).SequenceEqual(r.Values!.OrderBy(e => e.Key)));
    if (pickle) matched++;
    var ifFilled = ScenarioStableId.Compute(Suite, r.Feature, shown, r.Name, r.Values);
    if (ifFilled != r.Id) moved++;
    Console.WriteLine($"{r.Name} | {shown.Replace("\n", "\\n")} | {(pickle ? "yes" : "NO")} | {r.Id} | {(ifFilled == r.Id ? "same" : ifFilled)}");
}
Console.WriteLine();
Console.WriteLine($"{matched} of {rows} outline rows: Gherkin's rule gives the pickle's own name (Cucumber lane, same run)");
Console.WriteLine($"{moved} of {rows} outline rows would get a new id if the filled name were hashed; the rest fill to their title");
