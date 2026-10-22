using System.Text.Json;
using Kronikol.Ingestion;
using Kronikol.Reports;
using Kronikol.Tracking;

namespace Kronikol.Tests.Ingestion;

/// <summary>
/// An ingest's fold scenario ("Traffic outside any test") is not a test, and every output of the ingest counts it as
/// none (plans/RUN_WINDOW_PLAN.md R1, F6): harness P1's browser report read "Scenarios 3, Passed 3" for two tests, and its
/// Failures.md "All 3 scenarios passed.".
/// </summary>
[Collection("DiagramsFetcher")] // shares the process-global tracking store with every class that runs the pipeline
public class NotATestIngestTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "kronikol-not-a-test-ingest-" + Guid.NewGuid().ToString("N"));
    private static readonly DateTimeOffset T0 = new(2026, 10, 21, 10, 0, 0, TimeSpan.Zero);

    public NotATestIngestTests()
    {
        Directory.CreateDirectory(_dir);
        RequestResponseLogger.Redaction = null;
    }

    public void Dispose()
    {
        RequestResponseLogger.Redaction = null;
        DefaultDiagramsFetcher.Reset();
        try { Directory.Delete(_dir, true); } catch { /* best effort */ }
    }

    private static InteractionRecord Call(string testId, string pairId, double atSecond, string type = "Request") => new()
    {
        Type = type,
        Method = "GET",
        Uri = "http://orders/api/orders",
        ServiceName = "orders",
        CallerName = "browser",
        TestId = testId,
        RequestResponseId = pairId,
        Timestamp = T0.AddSeconds(atSecond),
    };

    [Fact]
    public void Every_output_of_an_ingest_counts_its_fold_scenario_as_no_test()
    {
        var options = IngestPipeline.DefaultOptions();
        options.ReportsFolderPath = Path.Combine(_dir, "Reports");
        options.GenerateComponentDiagram = false;
        options.GenerateCtrfReport = true;

        var result = IngestPipeline.Run(new IngestRequest
        {
            Interactions =
            [
                // the global login, which names no test, and one call of each test
                Call("session", "login", 0.5), Call("session", "login", 0.6, "Response"),
                Call("b1", "b1", 1.5), Call("b1", "b1", 1.6, "Response"),
                Call("b2", "b2", 3.5), Call("b2", "b2", 3.6, "Response"),
            ],
            TestRecords =
            [
                new TestRunRecord { Event = "start", TestId = "b1", TestName = "Orders › lists orders", Timestamp = T0.AddSeconds(1) },
                new TestRunRecord { Event = "end", TestId = "b1", Status = "passed", Timestamp = T0.AddSeconds(2) },
                new TestRunRecord { Event = "start", TestId = "b2", TestName = "Orders › shows the menu", Timestamp = T0.AddSeconds(3) },
                new TestRunRecord { Event = "end", TestId = "b2", Status = "passed", Timestamp = T0.AddSeconds(4) },
            ],
            Options = options,
            FoldUnknownTestsInto = new UnknownTestFold("Traffic outside any test"),
        });

        var fold = Assert.Single(result.Features.SelectMany(f => f.Scenarios), s => s.DisplayName == "Traffic outside any test");
        Assert.True(fold.NotATest);

        var reports = result.ReportsDirectory;
        Assert.Contains("<div class=\"run-stat\"><dt>Scenarios</dt><dd>2</dd></div>", File.ReadAllText(Path.Combine(reports, "TestRunReport.html")));
        Assert.Contains("All 2 scenarios passed.", File.ReadAllText(Path.Combine(reports, "Failures.md")));
        Assert.Equal(2, RunManifest.TryRead(reports)!.Scenarios);

        using var ctrf = JsonDocument.Parse(File.ReadAllText(Path.Combine(reports, CtrfReportGenerator.FileName)));
        Assert.Equal(2, ctrf.RootElement.GetProperty("results").GetProperty("summary").GetProperty("tests").GetInt32());

        // The flag reaches the data file, so a merge or a query of it knows too.
        using var data = JsonDocument.Parse(ReportFile.ReadAny(Path.Combine(reports, "TestRunReport.json")));
        var scenario = data.RootElement.GetProperty("features").EnumerateArray()
            .SelectMany(f => f.GetProperty("scenarios").EnumerateArray())
            .Single(s => s.GetProperty("name").GetString() == "Traffic outside any test");
        Assert.True(scenario.GetProperty("notATest").GetBoolean());
    }

    /// <summary>
    /// Found while writing the fact above: a host whose capture names every call it could not attribute by the fold's own
    /// id (the issue's host gives them "session", and folds into "session") got a fold scenario named after the id, since
    /// only the records the fold renamed took its name. Every record of the fold is the fold's, so each takes its name.
    /// </summary>
    [Fact]
    public void A_fold_whose_id_the_records_already_carry_is_still_named_as_the_fold()
    {
        var options = IngestPipeline.DefaultOptions();
        options.ReportsFolderPath = Path.Combine(_dir, "FoldId");
        options.GenerateComponentDiagram = false;

        var result = IngestPipeline.Run(new IngestRequest
        {
            Interactions = [Call("session", "login", 0.5), Call("session", "login", 0.6, "Response"), Call("b1", "b1", 1.5), Call("b1", "b1", 1.6, "Response")],
            TestRecords =
            [
                new TestRunRecord { Event = "start", TestId = "b1", TestName = "Orders \u203a lists orders", Timestamp = T0.AddSeconds(1) },
                new TestRunRecord { Event = "end", TestId = "b1", Status = "passed", Timestamp = T0.AddSeconds(2) },
            ],
            Options = options,
            FoldUnknownTestsInto = new UnknownTestFold("Traffic outside any test", "session"),
        });

        var fold = Assert.Single(result.Features.SelectMany(f => f.Scenarios), s => s.Id == "session");
        Assert.Equal("Traffic outside any test", fold.DisplayName);
        Assert.True(fold.NotATest);
    }

    /// <summary>
    /// A fold whose id is a test's: applied, the unattributed traffic joined the test, and the test was then made the fold,
    /// passed and not a test, so its failure left every count. It is not applied, as a setup window with a test's id is not,
    /// and the traffic is drawn as it is with no fold.
    /// </summary>
    [Fact]
    public void A_fold_whose_id_is_a_tests_is_not_applied_and_the_test_keeps_its_verdict()
    {
        var options = IngestPipeline.DefaultOptions();
        options.ReportsFolderPath = Path.Combine(_dir, "FoldTestId");
        options.GenerateComponentDiagram = false;

        var result = IngestPipeline.Run(new IngestRequest
        {
            Interactions = [Call("session", "login", 0.5), Call("session", "login", 0.6, "Response"), Call("b1", "b1", 1.5), Call("b1", "b1", 1.6, "Response")],
            TestRecords =
            [
                new TestRunRecord { Event = "start", TestId = "b1", TestName = "Orders › lists orders", Timestamp = T0.AddSeconds(1) },
                new TestRunRecord { Event = "end", TestId = "b1", Status = "failed", Timestamp = T0.AddSeconds(2) },
            ],
            Options = options,
            FoldUnknownTestsInto = new UnknownTestFold("Traffic outside any test", "b1"),
        });

        Assert.Contains("FoldUnknownTestsInto: its scenario id \"b1\" is a test's in the tests records; it was not applied.",
            result.Diagnostics.Where(d => d.Kind == DiagnosticKind.Other).Select(d => d.Message));
        var scenarios = result.Features.SelectMany(f => f.Scenarios).ToArray();
        var test = Assert.Single(scenarios, s => s.Id == "b1");
        Assert.Equal(ExecutionResult.Failed, test.Result);
        Assert.False(test.NotATest);
        Assert.Contains(scenarios, s => s.Id == "session");
    }

    [Fact]
    public void A_fold_that_is_not_applied_leaves_DropUnattributed_as_it_is_with_no_fold()
    {
        // Not applied, the fold is no fold: a record whose id names no test is drawn as a scenario of its own, and is not
        // one DropUnattributed is asked about, as with no fold.
        var options = IngestPipeline.DefaultOptions();
        options.ReportsFolderPath = Path.Combine(_dir, "FoldTestIdDrop");
        options.GenerateComponentDiagram = false;
        var asked = new List<string>();

        IngestPipeline.Run(new IngestRequest
        {
            Interactions = [Call("session", "login", 0.5), Call("session", "login", 0.6, "Response"), Call("b1", "b1", 1.5), Call("b1", "b1", 1.6, "Response")],
            TestRecords =
            [
                new TestRunRecord { Event = "start", TestId = "b1", TestName = "Orders › lists orders", Timestamp = T0.AddSeconds(1) },
                new TestRunRecord { Event = "end", TestId = "b1", Status = "failed", Timestamp = T0.AddSeconds(2) },
            ],
            Options = options,
            FoldUnknownTestsInto = new UnknownTestFold("Traffic outside any test", "b1"),
            DropUnattributed = record => { asked.Add(record.TestId); return false; },
        });

        Assert.Empty(asked);
    }
}
