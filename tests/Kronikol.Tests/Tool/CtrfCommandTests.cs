using System.Text.Json;
using Kronikol.Reports;
using Kronikol.Tool;
using Kronikol.Tracking;

namespace Kronikol.Tests.Tool;

/// <summary>
/// <c>kronikol ctrf</c> converts a report that has already been written. It exists because the run that
/// produced the report is usually over by the time somebody wants CTRF out of it — a downloaded CI
/// artifact, a report from a colleague, a suite whose options nobody wants to change.
///
/// <para>The fact that matters most here is the last one: the document this verb produces and the
/// document the run itself would have written are the same bytes. Two writers for one format is how a
/// format quietly forks, and the only defence is a test that reads both.</para>
/// </summary>
[Collection("DiagramsFetcher")]
public class CtrfCommandTests : IDisposable
{
    private readonly string _directory = Directory.CreateTempSubdirectory("kronikol-ctrf").FullName;

    public CtrfCommandTests() => DefaultDiagramsFetcher.Reset();

    public void Dispose()
    {
        DefaultDiagramsFetcher.Reset();
        try { Directory.Delete(_directory, recursive: true); } catch (IOException) { }
        GC.SuppressFinalize(this);
    }

    private static (string Out, string Err, int Exit) Run(params string[] args)
    {
        var output = new StringWriter();
        var error = new StringWriter();
        var exit = Commands.Dispatch(["ctrf", .. args], output, error);
        return (output.ToString(), error.ToString(), exit);
    }

    /// <summary>A real run, written to disk with the CTRF output switched on so both writers can be compared.</summary>
    private void WriteRun()
    {
        var testId = "ctrf-" + Guid.NewGuid().ToString("N");
        RequestResponseLogger.LogPair("Pay with an expired card", testId, HttpMethod.Post, new Uri("http://payments/charge"), "payments", "Test");
        Feature[] features =
        [
            new Feature
            {
                DisplayName = "Checkout",
                SourceFile = "Features/Checkout.feature",
                Scenarios =
                [
                    new Scenario
                    {
                        Id = testId, DisplayName = "Pay with an expired card", Result = ExecutionResult.Failed,
                        Duration = TimeSpan.FromMilliseconds(1250), ErrorMessage = "Assert.Equal() Failure",
                        ErrorStackTrace = "at Checkout.Pay()", Labels = ["smoke"], Categories = ["payments"],
                        SourceFile = "Features/Checkout.feature", SourceLine = 12
                    },
                    new Scenario
                    {
                        Id = "ctrf-pass", DisplayName = "Pay with a valid card", Result = ExecutionResult.Passed,
                        Duration = TimeSpan.FromMilliseconds(300), Attempt = 2, Labels = ["retry 1"]
                    },
                    new Scenario { Id = "ctrf-skip", DisplayName = "Pay in yen", Result = ExecutionResult.Skipped }
                ]
            },
            new Feature
            {
                DisplayName = "Catalogue",
                Scenarios = [new Scenario { Id = "ctrf-browse", DisplayName = "Browse", Result = ExecutionResult.Passed, Duration = TimeSpan.FromSeconds(2) }]
            }
        ];

        ReportGenerator.CreateStandardReportsWithDiagrams(features,
            new DateTime(2026, 1, 1, 10, 0, 0, DateTimeKind.Utc),
            new DateTime(2026, 1, 1, 10, 0, 30, DateTimeKind.Utc),
            new ReportConfigurationOptions
            {
                ReportsFolderPath = _directory,
                InternalFlowTracking = false,
                GenerateComponentDiagram = false,
                GenerateSpecificationsReport = false,
                GenerateSpecificationsData = false,
                GenerateCtrfReport = true
            });
    }

    private string Report => Path.Combine(_directory, "TestRunReport.json");

    // ─── The conversion ────────────────────────────────────────

    [Fact]
    public void The_report_becomes_a_ctrf_document_on_stdout()
    {
        WriteRun();

        var (output, _, exit) = Run(Report);

        Assert.Equal(0, exit);
        using var document = JsonDocument.Parse(output);
        Assert.Equal("CTRF", document.RootElement.GetProperty("reportFormat").GetString());
        Assert.Equal(4, document.RootElement.GetProperty("results").GetProperty("summary").GetProperty("tests").GetInt32());
    }

    [Fact]
    public void The_converted_document_is_the_one_the_run_would_have_written()
    {
        // Two writers, one format. The run's own writer maps Feature[]; this verb maps what the scanner
        // read back out of the file. They share the envelope, the summary and the key names by
        // construction — this is the fact that says the mapping halves agree too.
        WriteRun();

        var (output, _, exit) = Run(Report);

        Assert.Equal(0, exit);
        Assert.Equal(File.ReadAllText(Path.Combine(_directory, "ctrf-report.json")), output.TrimEnd('\r', '\n'));
    }

    [Fact]
    public void The_out_flag_writes_the_document_where_it_is_asked_to()
    {
        WriteRun();
        var target = Path.Combine(_directory, "elsewhere.json");

        var (output, _, exit) = Run(Report, "--out", target);

        Assert.Equal(0, exit);
        Assert.Equal("", output.Trim());
        Assert.Equal(File.ReadAllText(Path.Combine(_directory, "ctrf-report.json")), File.ReadAllText(target));
    }

    [Fact]
    public void A_directory_resolves_the_report_inside_it()
    {
        WriteRun();

        var (output, _, exit) = Run(_directory);

        Assert.Equal(0, exit);
        Assert.Contains("\"reportFormat\": \"CTRF\"", output);
    }

    [Fact]
    public void The_ctrf_file_beside_a_report_is_never_mistaken_for_a_report()
    {
        // ctrf-report.json is a second .json in the reports directory. Report discovery matches on the
        // TestRunReport.json suffix, so it cannot become a candidate — but nothing said so until now, and
        // a CTRF file offered as a report and then failing to parse is worse than not being offered.
        WriteRun();

        Assert.Equal(0, Run(_directory).Exit);

        var query = new StringWriter();
        var queryError = new StringWriter();
        Assert.Equal(0, Commands.Dispatch(["query", "summary", _directory], query, queryError));
        Assert.Contains("4 scenarios", query.ToString());
    }

    // ─── Usage ─────────────────────────────────────────────────

    [Fact]
    public void With_no_arguments_it_says_how_to_use_it()
    {
        var (_, error, exit) = Run();

        Assert.Equal(2, exit);
        Assert.Contains("Usage: kronikol ctrf", error);
    }

    [Fact]
    public void Help_goes_to_stdout_and_exits_zero()
    {
        var (output, _, exit) = Run("--help");

        Assert.Equal(0, exit);
        Assert.Contains("Usage: kronikol ctrf", output);
    }

    [Fact]
    public void An_unknown_option_is_a_usage_error()
    {
        WriteRun();

        var (_, error, exit) = Run(Report, "--ctrf-everything");

        Assert.Equal(2, exit);
        Assert.Contains("Unknown option: --ctrf-everything", error);
    }

    [Fact]
    public void A_second_report_is_refused_rather_than_silently_ignored()
    {
        WriteRun();

        var (_, error, exit) = Run(Report, Report);

        Assert.Equal(2, exit);
        Assert.Contains("one report", error);
    }

    [Fact]
    public void A_path_that_is_not_there_is_a_usage_error_naming_the_path()
    {
        var missing = Path.Combine(_directory, "nowhere.json");

        var (_, error, exit) = Run(missing);

        Assert.Equal(2, exit);
        Assert.Contains(missing, error);
    }
}
