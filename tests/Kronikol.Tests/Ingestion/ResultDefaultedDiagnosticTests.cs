using System.Text.Json;
using Kronikol.Ingestion;
using Kronikol.Reports;
using Kronikol.Tool;
using Kronikol.Tracking;

namespace Kronikol.Tests.Ingestion;

/// <summary>
/// <c>ResultWhenUnknown</c> defaults to <see cref="ExecutionResult.Passed"/>, so a test whose process died
/// mid-run renders green. That is a deliberate compatibility choice — a capture with no verdicts at all
/// must not read as a catastrophe — but until now nothing in the report said the default had been applied,
/// so no reader could tell a pass from a silence. These facts pin the detection: the fact is recorded, it
/// reaches the data file, and every consumer that reads the file says so before it answers anything.
/// </summary>
[Collection("DiagramsFetcher")]
public class ResultDefaultedDiagnosticTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "kronikol-defaulted-" + Guid.NewGuid().ToString("N"));
    private static readonly DateTimeOffset T0 = new(2026, 9, 12, 10, 0, 0, TimeSpan.Zero);

    public ResultDefaultedDiagnosticTests()
    {
        Directory.CreateDirectory(_dir);
        DefaultDiagramsFetcher.Reset();
    }

    public void Dispose()
    {
        DefaultDiagramsFetcher.Reset();
        try { Directory.Delete(_dir, true); } catch { /* best effort */ }
    }

    /// <summary>A worker that started two tests, finished one, and died.</summary>
    private IngestResult Ingest(ExecutionResult resultWhenUnknown = ExecutionResult.Passed, string subdirectory = "out")
    {
        var options = IngestPipeline.DefaultOptions();
        options.ReportsFolderPath = Path.Combine(_dir, subdirectory);
        options.GenerateComponentDiagram = false;

        return IngestPipeline.Run(new IngestRequest
        {
            Options = options,
            ResultWhenUnknown = resultWhenUnknown,
            TestRecords =
            [
                new TestRunRecord { Event = "start", TestId = "t1", TestName = "finishes", Feature = "Checkout", Timestamp = T0 },
                new TestRunRecord { Event = "end", TestId = "t1", Status = "passed", Timestamp = T0.AddSeconds(1) },
                new TestRunRecord { Event = "start", TestId = "t2", TestName = "never ends", Feature = "Checkout", Timestamp = T0 },
            ],
        });
    }

    [Fact]
    public void A_scenario_that_never_ended_is_recorded_as_defaulted()
    {
        var result = Ingest();

        var entry = Assert.Single(result.Diagnostics, d => d.Kind == DiagnosticKind.ResultDefaulted);
        Assert.Contains("1 scenario(s) recorded no result", entry.Message);
        Assert.Contains("Passed", entry.Message);
        Assert.Contains("ResultWhenUnknown", entry.Message);
    }

    [Fact]
    public void The_configured_value_is_named_whatever_it_is()
    {
        var result = Ingest(ExecutionResult.Failed, "failed");

        Assert.Contains("Failed", Assert.Single(result.Diagnostics, d => d.Kind == DiagnosticKind.ResultDefaulted).Message);
    }

    /// <summary>A run where every test that started also ended, in the same two features.</summary>
    private IngestResult CleanRun(string subdirectory = "clean")
    {
        var options = IngestPipeline.DefaultOptions();
        options.ReportsFolderPath = Path.Combine(_dir, subdirectory);
        options.GenerateComponentDiagram = false;

        return IngestPipeline.Run(new IngestRequest
        {
            Options = options,
            TestRecords =
            [
                new TestRunRecord { Event = "start", TestId = "t1", TestName = "finishes", Feature = "Checkout", Timestamp = T0 },
                new TestRunRecord { Event = "end", TestId = "t1", Status = "passed", Timestamp = T0.AddSeconds(1) },
                new TestRunRecord { Event = "start", TestId = "t2", TestName = "never ends", Feature = "Checkout", Timestamp = T0 },
                new TestRunRecord { Event = "end", TestId = "t2", Status = "failed", Timestamp = T0.AddSeconds(1) },
            ],
        });
    }

    [Fact]
    public void A_run_where_every_test_ended_records_nothing()
    {
        Assert.DoesNotContain(CleanRun().Diagnostics, d => d.Kind == DiagnosticKind.ResultDefaulted);
    }

    [Fact]
    public void It_reaches_the_data_file_and_every_query_answer()
    {
        var result = Ingest();
        var report = Path.Combine(result.ReportsDirectory, "TestRunReport.json");

        using (var document = JsonDocument.Parse(File.ReadAllText(report)))
        {
            Assert.Contains(document.RootElement.GetProperty("diagnostics").EnumerateArray(),
                d => d.GetProperty("kind").GetString() == nameof(DiagnosticKind.ResultDefaulted));
        }

        // The banner is written before the answer, on every command — a defaulted result changes how any
        // answer should be read, not only the summary's.
        foreach (var command in new[] { "summary", "failures", "scenarios" })
        {
            var output = new StringWriter();
            var error = new StringWriter();
            Assert.Equal(0, QueryCommand.Run([command, report], output, error));
            Assert.Contains("! 1 scenario(s) recorded no result", output.ToString());
        }
    }

    /// <summary>
    /// `diff` was the one verb that returned early from the provenance header, because a note with two
    /// reports in scope does not say which one it is about. The consequence is the worst reading of a
    /// defaulted run there is: a scenario that died mid-run is recorded as Passed, so against a baseline
    /// where it genuinely failed, `diff` reports it under <c>Fixed</c> — a regression printed as a
    /// success. The side is what was missing, so the side is what the note now names.
    /// </summary>
    [Fact]
    public void Diff_says_which_side_had_its_verdicts_defaulted()
    {
        var old = Path.Combine(CleanRun().ReportsDirectory, "TestRunReport.json");
        var current = Path.Combine(Ingest(subdirectory: "new").ReportsDirectory, "TestRunReport.json");

        var output = new StringWriter();
        var error = new StringWriter();
        Assert.Equal(0, QueryCommand.Run(["diff", old, current], output, error));

        var text = output.ToString();
        Assert.Contains("recorded no result", text);
        Assert.Contains("new:", text);
        Assert.DoesNotContain("old:", text);
    }

    [Fact]
    public void The_digest_warns_even_though_nothing_failed()
    {
        // The trap this closes: a crashed worker produces a green run, and `Failures.md` saying
        // "No failures" is exactly the reassurance nobody should get.
        var result = Ingest();

        var digest = File.ReadAllText(Path.Combine(result.ReportsDirectory, "Failures.md"));
        Assert.Contains("# No failures", digest);
        Assert.Contains("Read that carefully", digest);
        Assert.Contains("recorded no result", digest);
    }
}
