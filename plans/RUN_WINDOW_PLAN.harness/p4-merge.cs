#:package Kronikol@5.2.3
#:property PublishAot=false
#:property JsonSerializerIsReflectionEnabledByDefault=true
// P4: what `kronikol merge` keeps of a scenario that is not a test. P1's browser run is ingested with a fold and
// GenerateMergeableData on, and its own history ledger written; the script then prints the ledger's run line. Merging
// the mergeable file with the published tool, into a second ledger, is done by p4-merge.sh beside this file.
//   dotnet run --file p4-merge.cs -- <P1's output folder> <empty folder>
using Kronikol.Ingestion;

var p1 = Path.GetFullPath(args[0]);
var dir = Path.GetFullPath(args[1]);
Directory.CreateDirectory(dir);
var ledger = Path.Combine(dir, "ingest-history.jsonl");
Environment.SetEnvironmentVariable("KRONIKOL_HISTORY", ledger);

var options = IngestPipeline.DefaultOptions() with { GenerateMergeableData = true };
options.ReportsFolderPath = Path.Combine(dir, "shard");
var result = IngestPipeline.Run(new IngestRequest
{
    InteractionFiles = [Path.Combine(p1, "capture.ndjson")], TestsFile = Path.Combine(p1, "browser.tests.ndjson"), Options = options,
    AttributeByTestWindow = true, WindowAttributionFallbackId = "session", DropOutsideRunWindow = true,
    FoldUnknownTestsInto = new UnknownTestFold("Traffic outside any test"),
});
foreach (var scenario in result.Features.SelectMany(f => f.Scenarios))
    Console.WriteLine($"in the model: {scenario.DisplayName} NotATest={scenario.NotATest} {scenario.Result}");
Console.WriteLine($"shard files: {string.Join(", ", Directory.GetFiles(options.ReportsFolderPath).Select(Path.GetFileName))}");
Console.WriteLine(File.Exists(ledger) ? $"ingest ledger:\n{File.ReadAllText(ledger)}" : "no ingest ledger written (KRONIKOL_HISTORY did not name a path)");
