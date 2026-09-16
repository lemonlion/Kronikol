using Kronikol.History;
using Kronikol.Reports;
using static Kronikol.DefaultDiagramsFetcher;

namespace Kronikol.Tests.EndToEnd;

/// <summary>
/// A report rendered against a seeded cross-run ledger, for the history E2E tests: four scenarios, six
/// earlier runs, and a current run in which one scenario broke, one has been flipping every run
/// (flaky), and two are as they were (stable).
/// </summary>
internal static class HistoryReportHelper
{
    public const string Suite = "E2EHistory";

    public static readonly string PayId = ScenarioStableId.Compute(Suite, "Checkout", "Pay by card");
    public static readonly string RetryId = ScenarioStableId.Compute(Suite, "Checkout", "Retry a payment");
    public static readonly string RefundId = ScenarioStableId.Compute(Suite, "Checkout", "Refund an order");

    private const string PlantUmlSource = """
        @startuml
        actor "Caller" as caller
        participant "Service" as svc
        caller -> svc : GET /api/test
        svc --> caller : 200 OK
        @enduml
        """;

    private static Feature[] Features() =>
    [
        new Feature
        {
            DisplayName = "Checkout",
            Scenarios =
            [
                new Scenario { Id = "h-pay", DisplayName = "Pay by card", Result = ExecutionResult.Failed, Duration = TimeSpan.FromMilliseconds(120), ErrorMessage = "Expected 200 but got 500" },
                new Scenario { Id = "h-refund", DisplayName = "Refund an order", Result = ExecutionResult.Passed, Duration = TimeSpan.FromMilliseconds(40) },
                new Scenario { Id = "h-retry", DisplayName = "Retry a payment", Result = ExecutionResult.Passed, Duration = TimeSpan.FromMilliseconds(80) }
            ]
        },
        new Feature
        {
            DisplayName = "Search",
            Scenarios = [new Scenario { Id = "h-find", DisplayName = "Find a product", Result = ExecutionResult.Passed, Duration = TimeSpan.FromMilliseconds(70) }]
        }
    ];

    /// <summary>Writes the report into <paramref name="tempDir"/>, copies it to <paramref name="outputDir"/>, and returns its file URL.</summary>
    public static string Generate(string tempDir, string outputDir, string fileName)
    {
        var features = Features();
        var at = new DateTimeOffset(2026, 9, 14, 12, 0, 0, TimeSpan.Zero);
        var ledger = Path.Combine(tempDir, "ledger-" + Guid.NewGuid().ToString("N")[..8], "history.jsonl");
        var (roster, run) = HistoryRunBuilder.Build(features, [], Suite, null, at, new HistoryBuildOptions(), "e2e:99:1");

        // Roster positions follow the features ordered by name: Pay, Refund, Retry, then Find. Retry
        // alternates fail/pass across the six earlier runs; everything else passed every time.
        for (var i = 0; i < 6; i++)
        {
            var results = i % 2 == 0 ? "PPFP" : "PPPP";
            var prior = run with
            {
                Id = $"e2e:{i + 1}:1", At = at.AddHours(i - 6), Commit = $"c{i + 1:D6}",
                Results = results, Attempts = "----",
                Durations = [100, 40, 80, 70],
                Errors = results.Select(r => r == 'F' ? "e1" : null).ToArray(),
                ErrorText = results.Contains('F') ? new Dictionary<string, string> { ["e1"] = "gateway timed out" } : new Dictionary<string, string>()
            };
            var appended = HistoryLedgerWriter.Append(ledger, roster, prior, "3.11.0");
            if (appended.Outcome != HistoryAppendOutcome.Appended)
                throw new InvalidOperationException($"seeding the ledger failed: {appended.Message}");
        }

        var held = HistoryLedgerReader.Read(ledger, 50).Ledger ?? throw new InvalidOperationException("the seeded ledger did not read back");
        var verdicts = HistoryAnalyzer.Analyse(held, roster, run, new HistoryAnalysisOptions { MinRuns = 3 });

        var diagrams = features.SelectMany(f => f.Scenarios).Select(s => new DiagramAsCode(s.Id, "", PlantUmlSource)).ToArray();
        var path = ReportGenerator.GenerateHtmlReport(
            diagrams, features,
            at.UtcDateTime.AddMinutes(-1), at.UtcDateTime,
            null, Path.Combine(tempDir, fileName), "History E2E Report", true,
            diagramFormat: DiagramFormat.PlantUml,
            plantUmlRendering: PlantUmlRendering.BrowserJs,
            suite: Suite,
            history: verdicts,
            // These reports are about the section, so they ask for it: it is off in a report that did not.
            showHistorySection: true);

        File.Copy(path, Path.Combine(outputDir, fileName), true);
        return new Uri(path).AbsoluteUri;
    }
}
