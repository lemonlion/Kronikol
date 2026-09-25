#:project ../../src/Kronikol/Kronikol.csproj
#:property PublishAot=false
#:property JsonSerializerIsReflectionEnabledByDefault=true
// A run report carrying every section outside the features, each with the long tokens a real run gives
// them: failure clusters (an exception type and a URL in the shared message, a test reported by its
// method's full name), the History section (a Dependabot branch as its stream, that name in its lists),
// Report diagnostics, Background calls (a long path), dependency and category chips named after types, long CI
// strings, the timeline and the component diagram. Writes out-sections/Sections.html and a violet copy
// (OUT_DIR overrides the folder). The E2E fixture ReportTestHelper.GenerateReportWithEverySection is the
// same page, committed as the guard's.
using System.Net;
using Kronikol;
using Kronikol.History;
using Kronikol.Reports;
using Kronikol.Tracking;
using static Kronikol.DefaultDiagramsFetcher;

Environment.SetEnvironmentVariable("KRONIKOL_HISTORY", "off");
Environment.SetEnvironmentVariable("KRONIKOL_KEEP_RUNS", "off");
var outDir = Path.Combine(AppContext.GetData("EntryPointFileDirectoryPath") as string ?? ".", Environment.GetEnvironmentVariable("OUT_DIR") ?? "out-sections");
Directory.CreateDirectory(outDir);

const string Suite = "BreakfastProvider.Tests.Component.LightBDD.xUnit3.OrderReconciliationAcrossRegions";
const string Error = "System.InvalidOperationException: Sequence contains no matching element in BreakfastProvider.Api.Services.OrderReconciliation.NightlySettlementBatchReconciler.ReconcileAcrossRegionsAsync calling https://breakfast-provider-integration-tests.example.com/api/v1/orders/reconciliation/nightly-settlement-batches?region=eu-west-1";
string LongName(int i) => $"Reconciling the nightly settlement batches across every region settles order {i} exactly once even when the downstream ledger service retries";

var features = new[]
{
    new Feature
    {
        DisplayName = "Order reconciliation across regions",
        Scenarios = [.. Enumerable.Range(1, 4).Select(i => new Scenario
        {
            Id = $"s{i}", DisplayName = i == 2 ? "BreakfastProvider.Tests.Component.Orders.OrderReconciliationTests.Reconciling_the_nightly_settlement_batches_settles_each_order_exactly_once" : LongName(i), Result = i == 4 ? ExecutionResult.Passed : ExecutionResult.Failed,
            Duration = TimeSpan.FromMilliseconds(200 * i), ErrorMessage = i == 4 ? null : Error,
            Labels = ["Regression", "BreakfastProvider.Tests.Component.Traits.NightlySettlementReconciliation"], Categories = ["BreakfastProvider.Tests.Categories.CrossRegionSettlement"],
            Steps = [new ScenarioStep { Keyword = "Given", Text = "the ledger service is available", Status = ExecutionResult.Passed }]
        })]
    },
    new Feature
    {
        DisplayName = "Payments",
        Scenarios = [new Scenario { Id = "p1", DisplayName = "Pay by card", Result = ExecutionResult.Passed, Duration = TimeSpan.FromMilliseconds(90) }]
    }
};

var ci = new CiMetadata(CiEnvironment.GitHubActions, "20260924.17",
    "dependabot/nuget/src/BreakfastProvider.Api/Microsoft.Extensions.Http.Resilience-9.10.0", "0123456789abcdef0123456789abcdef01234567",
    "https://github.com/my-organisation/breakfast-provider-integration-tests/actions/runs/1234567890",
    "my-organisation/breakfast-provider-integration-tests", "1234567890", "1");

// History: six earlier runs in which scenario 4 alternated, and the first three passed every time, so
// this run's failures are new and scenario 4 is flaky.
var at = new DateTimeOffset(2026, 9, 24, 12, 0, 0, TimeSpan.Zero);
var ledger = Path.Combine(Path.GetTempPath(), "kronikol-sections-" + Guid.NewGuid().ToString("N")[..8], "history.jsonl");
var (roster, run, _) = HistoryRunBuilder.Build(features, [], Suite, ci, at, new HistoryBuildOptions(), "audit:99:1");
for (var i = 0; i < 6; i++)
{
    var results = i % 2 == 0 ? "PPPFP" : "PPPPP";
    var prior = run with
    {
        Id = $"audit:{i + 1}:1", At = at.AddHours(i - 6), Commit = $"c{i + 1:D6}", Results = results,
        Attempts = new string('-', results.Length), Durations = [100, 200, 300, 400, 90],
        Errors = results.Select(r => r == 'F' ? "e1" : null).ToArray(),
        ErrorText = results.Contains('F') ? new Dictionary<string, string> { ["e1"] = Error } : new Dictionary<string, string>()
    };
    var appended = HistoryLedgerWriter.Append(ledger, roster, prior, "3.29.3");
    if (appended.Outcome != HistoryAppendOutcome.Appended) throw new InvalidOperationException(appended.Message);
}
var held = HistoryLedgerReader.Read(ledger, 50).Ledger ?? throw new InvalidOperationException("ledger did not read back");
var verdicts = HistoryAnalyzer.Analyse(held, roster, run, new HistoryAnalysisOptions { MinRuns = 3 });

var diagnostics = new List<DiagnosticEntry>
{
    new(DiagnosticKind.RenderFailure, "The PlantUML engine gave up on a diagram of 14,203 lines: BreakfastProvider.Api.Services.OrderReconciliation.NightlySettlementBatchReconciler", "s1"),
    new(DiagnosticKind.MalformedLine, "Skipped a capture line at C:/agents/_work/1/s/tests/BreakfastProvider.Tests.Component/bin/Release/net10.0/Reports/captures/interactions.ndjson:1234"),
};

var logs = Enumerable.Range(0, 3).Select(i => new RequestResponseLog(
    LongName(1), "s1", HttpMethod.Post, null,
    new Uri("https://ledger.example.com/api/v1/orders/reconciliation/nightly-settlement-batches/2026-09-24/regions/eu-west-1/retry-after-timeout"),
    [], "Nightly Settlement Ledger Service (eu-west-1)", "Caller", RequestResponseType.Request, Guid.NewGuid(), Guid.NewGuid(), false)
    { }).ToList();
var background = new BackgroundCalls(3, [new BackgroundCallGroup("s1", LongName(1), 3, at)], logs);

const string Source = "@startuml\nactor \"Caller\" as caller\nparticipant \"Nightly Settlement Ledger Service\" as l\nparticipant \"BreakfastProvider.Api.Clients.NightlySettlementLedgerHttpClient\" as l2\ncaller -> l : POST /api/v1/orders/reconciliation\ncaller -> l2 : GET /settlements\nl --> caller : 500\n@enduml\n";
var diagrams = features.SelectMany(f => f.Scenarios).Select(s => new DiagramAsCode(s.Id, "", Source)).ToArray();
const string Component = "@startuml\ncomponent \"Breakfast Provider API\" as api\ncomponent \"Nightly Settlement Ledger Service\" as l\napi --> l\n@enduml\n";

foreach (var (name, sheet) in new[] { ("Sections.html", (string?)null), ("Sections_violet.html", Stylesheets.VioletThemeStyleSheet) })
{
    var path = ReportGenerator.GenerateHtmlReport(diagrams, features, at.UtcDateTime.AddMinutes(-1), at.UtcDateTime,
        sheet, Path.Combine(outDir, name), "Sections", true,
        diagramFormat: DiagramFormat.PlantUml, plantUmlRendering: PlantUmlRendering.BrowserJs,
        ciMetadata: ci, componentDiagramPlantUml: Component, diagnostics: diagnostics, background: background,
        suite: Suite, history: verdicts, showHistorySection: true, showReportDiagnostics: true);
    Console.WriteLine($"wrote {path} ({new FileInfo(path).Length} bytes)");
}
