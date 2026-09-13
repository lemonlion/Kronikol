using Kronikol.Reports;
using Kronikol.Tracking;

namespace Kronikol.Tests.Reports;

/// <summary>
/// The pointer is the only place a run says where it put things, and after an output failed it was
/// naming, sizing and routing the reader to the <b>previous</b> run's file.
///
/// <para><c>Summarise</c> was given the outputs the configuration asked for and kept the ones that
/// exist — and a file left behind by an earlier run exists. So the worst case reads exactly like the best
/// one: <c>TestRunReport.json 48.2 MB</c> on the pointer, a query tool that opens it happily, and an
/// answer about a different run. Nothing said the write had failed except one warning already scrolled
/// past.</para>
///
/// <para>There is a second mode with the same ending. On <see cref="IOException"/> — a file held open by a
/// reader, a viewer, a previous process — <c>WriteFile</c> quietly wrote <c>&lt;name&gt;2.&lt;ext&gt;</c>
/// and returned as though it had succeeded, so the run was never marked at all and the canonical name on
/// disk still held the older bytes.</para>
///
/// <para><b>Both failures have to be provoked the way they actually happen.</b> Putting a directory in the
/// way looks like the obvious setup and tests neither: <c>File.WriteAllText</c> answers a directory with
/// <see cref="UnauthorizedAccessException"/>, which the fallback does not catch, and a directory is not a
/// file so the pointer already skipped it. A read-only file and a file held with
/// <see cref="FileShare.None"/> are the real shapes.</para>
/// </summary>
[Collection("DiagramsFetcher")]
public class StaleOutputTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "kronikol-stale-" + Guid.NewGuid().ToString("N"));

    public StaleOutputTests()
    {
        Directory.CreateDirectory(_dir);
        DefaultDiagramsFetcher.Reset();
    }

    public void Dispose()
    {
        DefaultDiagramsFetcher.Reset();
        foreach (var file in Directory.EnumerateFiles(_dir, "*", SearchOption.AllDirectories))
        {
            try { File.SetAttributes(file, FileAttributes.Normal); } catch { /* best effort */ }
        }
        try { Directory.Delete(_dir, true); } catch { /* best effort */ }
    }

    private string Run()
    {
        var testId = "stale-" + Guid.NewGuid().ToString("N");
        RequestResponseLogger.LogPair("Pay", testId, HttpMethod.Post, new Uri("http://payments/charge"), "payments", "Test");

        var original = Console.Out;
        var captured = new StringWriter();
        try
        {
            Console.SetOut(captured);
            ReportGenerator.CreateStandardReportsWithDiagrams(
                [
                    new Feature
                    {
                        DisplayName = "Checkout",
                        Scenarios = [new Scenario { Id = testId, DisplayName = "Pay", Result = ExecutionResult.Failed, ErrorMessage = "Assert.Equal() Failure" }]
                    }
                ],
                DateTime.UtcNow.AddMinutes(-1), DateTime.UtcNow,
                new ReportConfigurationOptions
                {
                    ReportsFolderPath = _dir,
                    InternalFlowTracking = false,
                    GenerateComponentDiagram = false,
                    GenerateSpecificationsReport = false,
                    GenerateSpecificationsData = false,
                });
        }
        finally
        {
            Console.SetOut(original);
        }

        return captured.ToString();
    }

    private static string Pointer(string console) =>
        console.Split('\n').First(l => l.StartsWith("Kronikol: reports written to", StringComparison.Ordinal));

    [Fact]
    public void The_pointer_does_not_name_the_previous_runs_file_when_this_run_could_not_replace_it()
    {
        var stale = Path.Combine(_dir, "TestRunReport.json");
        File.WriteAllText(stale, "{\"from\":\"an earlier run\"}");
        File.SetAttributes(stale, FileAttributes.ReadOnly);

        var console = Run();

        // The write failed and was announced...
        Assert.Contains("could not write TestRunReport.json", console, StringComparison.Ordinal);
        // ...so the pointer must not hand the reader the bytes of a different run.
        Assert.DoesNotContain("TestRunReport.json", Pointer(console), StringComparison.Ordinal);
        // And what this run DID write is still named — this must not become a pointer that names nothing.
        Assert.Contains("Failures.md", Pointer(console), StringComparison.Ordinal);
    }

    [Fact]
    public void A_write_that_falls_back_to_a_second_name_marks_the_run()
    {
        var held = Path.Combine(_dir, "Failures.md");
        File.WriteAllText(held, "# Failures — from an earlier run\n");

        using (new FileStream(held, FileMode.Open, FileAccess.Read, FileShare.None))
        {
            var console = Run();

            // Silence was the defect: the fallback wrote Failures2.md and returned as though nothing had
            // happened, leaving the previous run's Failures.md on disk under the name everything uses.
            Assert.Contains("Failures.md", console, StringComparison.Ordinal);
            Assert.Contains("could not write", console, StringComparison.Ordinal);
            Assert.DoesNotContain("Failures.md", Pointer(console), StringComparison.Ordinal);
        }
    }

    [Fact]
    public void A_run_where_everything_writes_still_names_everything()
    {
        // Non-vacuity: a pointer that has learned to omit things must not omit the ordinary case.
        var console = Run();

        Assert.Contains("TestRunReport.html", Pointer(console), StringComparison.Ordinal);
        Assert.Contains("TestRunReport.json", Pointer(console), StringComparison.Ordinal);
        Assert.Contains("Failures.md", Pointer(console), StringComparison.Ordinal);
        Assert.DoesNotContain("could not write", console, StringComparison.Ordinal);
    }
}
