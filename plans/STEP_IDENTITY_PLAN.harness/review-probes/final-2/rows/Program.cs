using System.Text.Json;
using Kronikol.Tests.RealEngines;

// Prints a run's report the way ReportRows reads it, scenario by scenario, one line for a step and one for the calls.
// Prints each file's place, the scenario's result, the diagnostics' kinds. The file itself is never printed.
var directory = args[0];
var file = ReportRows.DataFile(directory);
if (file is null)
{
    Console.WriteLine("NO REPORT in " + directory);
    return 1;
}

var rows = ReportRows.Read(file);
var names = rows.ScenarioNames;
var seen = new Dictionary<string, int>();

using var content = Kronikol.Compression.ReportFileFormat.Open(File.OpenRead(file));
using var decoded = new MemoryStream();
content.Stream.CopyTo(decoded);
using var document = JsonDocument.Parse(decoded.ToArray());

var scenarios = new List<JsonElement>();
foreach (var feature in document.RootElement.GetProperty("features").EnumerateArray())
    if (feature.TryGetProperty("scenarios", out var list))
        scenarios.AddRange(list.EnumerateArray());

IEnumerable<string> FilesOn(JsonElement step, string address)
{
    if (step.TryGetProperty("attachments", out var attachments))
        foreach (var attachment in attachments.EnumerateArray())
            yield return address + ":" + (attachment.TryGetProperty("name", out var n) ? n.GetString() : "?");
    if (step.TryGetProperty("subSteps", out var subs))
    {
        var i = 0;
        foreach (var sub in subs.EnumerateArray())
        {
            foreach (var found in FilesOn(sub, address + "." + i))
                yield return found;
            i++;
        }
    }
}

var filter = args.Length > 1 ? args[1] : null;
for (var index = 0; index < names.Count; index++)
{
    var name = names[index];
    if (filter is not null && !name.Contains(filter))
        continue;

    // ReportRows.Of finds by start of name: count how many earlier scenarios that start would also find.
    var occurrence = names.Take(index).Count(n => n.StartsWith(name, StringComparison.Ordinal));
    var scenario = scenarios[index];
    var result = scenario.TryGetProperty("result", out var r) ? r.ToString() : scenario.TryGetProperty("status", out var s) ? s.ToString() : "?";

    Console.WriteLine($"=== {name} [{result}]");
    Console.WriteLine(rows.Of(name, occurrence));

    var files = new List<string>();
    if (scenario.TryGetProperty("attachments", out var own))
        foreach (var attachment in own.EnumerateArray())
            files.Add("scenario:" + (attachment.TryGetProperty("name", out var n) ? n.GetString() : "?"));
    foreach (var (list, prefix) in new[] { ("backgroundSteps", "b"), ("steps", "") })
        if (scenario.TryGetProperty(list, out var steps))
        {
            var i = 0;
            foreach (var step in steps.EnumerateArray())
            {
                files.AddRange(FilesOn(step, prefix + i));
                i++;
            }
        }
    if (files.Count > 0)
        Console.WriteLine("files: " + string.Join(", ", files));
}

if (filter is null)
{
    foreach (var property in document.RootElement.EnumerateObject())
    {
        if (property.NameEquals("diagnostics") && property.Value.ValueKind == JsonValueKind.Array)
        {
            var kinds = property.Value.EnumerateArray().GroupBy(d => d.TryGetProperty("kind", out var k) ? k.ToString() : "?")
                .Select(g => $"{g.Key}={g.Count()}");
            Console.WriteLine("diagnostics: " + string.Join(", ", kinds));
            foreach (var d in property.Value.EnumerateArray())
                if (d.TryGetProperty("kind", out var kd) && kd.ToString() == "StepAttributionMismatch")
                    Console.WriteLine("  mismatch: " + (d.TryGetProperty("message", out var m) ? m.ToString() : "?") + " | " + (d.TryGetProperty("testId", out var t) ? t.ToString() : ""));
        }
    }
}

return 0;
