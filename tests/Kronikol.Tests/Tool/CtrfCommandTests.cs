using System.Text.Json;
using System.Text.Json.Nodes;
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
    private void WriteRun(bool json = false, string? serviceId = null)
    {
        var testId = "ctrf-" + Guid.NewGuid().ToString("N");
        RequestResponseLogger.LogPair("Pay with an expired card", testId, HttpMethod.Post, new Uri("http://payments/charge"), "payments", "Test");
        Feature[] features =
        [
            new Feature
            {
                DisplayName = "Checkout",
                SourceFile = "Features/Checkout.feature",
                Tags = ["checkout"],
                Scenarios =
                [
                    new Scenario
                    {
                        Id = testId, DisplayName = "Pay with an expired card", Result = ExecutionResult.Failed,
                        Duration = TimeSpan.FromMilliseconds(1250), ErrorMessage = "Assert.Equal() Failure",
                        ErrorStackTrace = "at Checkout.Pay()", Tags = ["smoke", "payments"],
                        SourceFile = "Features/Checkout.feature", SourceLine = 12
                    },
                    new Scenario
                    {
                        Id = "ctrf-pass", DisplayName = "Pay with a valid card", Result = ExecutionResult.Passed,
                        Duration = TimeSpan.FromMilliseconds(300), Attempt = 2
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

        var options = new ReportConfigurationOptions
        {
            ReportsFolderPath = _directory,
            InternalFlowTracking = false,
            GenerateComponentDiagram = false,
            GenerateSpecificationsReport = false,
            GenerateSpecificationsData = false,
            GenerateCtrfReport = true,
            ServiceId = serviceId
        };
        // A fact about what a 4.x writer wrote asks for its file, TestRunReport.json; 5.0.0 writes the .krkl.
        if (json)
            options.CompressTestRunReport = false;
        ReportGenerator.CreateStandardReportsWithDiagrams(features,
            new DateTime(2026, 1, 1, 10, 0, 0, DateTimeKind.Utc),
            new DateTime(2026, 1, 1, 10, 0, 30, DateTimeKind.Utc),
            options);
    }

    /// <summary>The run's data file, in the form it wrote.</summary>
    private string Report => ReportFile.Resolve(Path.Combine(_directory, "TestRunReport.json"));

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
        Assert.Equal(ReportHtml.Read(Path.Combine(_directory, "ctrf-report.json")), output.TrimEnd('\r', '\n'));
    }

    // #190 R1, Q16: the declared service is CTRF's environment.appName, from both writers, on CI and off it.
    [Fact]
    public void A_declared_service_is_the_documents_app_name_from_both_writers()
    {
        WriteRun(serviceId: "checkout-api");

        var (output, _, exit) = Run(Report);

        Assert.Equal(0, exit);
        var written = ReportHtml.Read(Path.Combine(_directory, "ctrf-report.json"));
        Assert.Equal(written, output.TrimEnd('\r', '\n'));
        using var document = JsonDocument.Parse(written);
        Assert.Equal("checkout-api", document.RootElement.GetProperty("results").GetProperty("environment").GetProperty("appName").GetString());
    }

    [Fact]
    public void A_run_that_declares_no_service_names_no_app()
    {
        WriteRun();

        Assert.DoesNotContain("appName", Run(Report).Out);
        Assert.DoesNotContain("appName", ReportHtml.Read(Path.Combine(_directory, "ctrf-report.json")));
    }

    [Fact]
    public void The_out_flag_writes_the_document_where_it_is_asked_to()
    {
        WriteRun();
        var target = Path.Combine(_directory, "elsewhere.json");

        var (output, _, exit) = Run(Report, "--out", target);

        Assert.Equal(0, exit);
        Assert.Equal("", output.Trim());
        Assert.Equal(ReportHtml.Read(Path.Combine(_directory, "ctrf-report.json")), ReportHtml.Read(target));
    }

    // ─── Tags ──────────────────────────────────────────────────

    /// <summary>The CTRF tests the verb makes of <see cref="Report"/>, by name.</summary>
    private Dictionary<string, JsonElement> ConvertedTests()
    {
        var (output, error, exit) = Run(Report);
        Assert.True(exit == 0, $"exit {exit}: {error}");
        using var document = JsonDocument.Parse(output);
        return document.RootElement.GetProperty("results").GetProperty("tests").EnumerateArray()
            .ToDictionary(t => t.GetProperty("name").GetString()!, t => t.Clone());
    }

    private static string?[] TagsOf(JsonElement test) =>
        test.TryGetProperty("tags", out var tags) ? [.. tags.EnumerateArray().Select(t => t.GetString())] : [];

    private static bool HasExtraCategories(JsonElement test) =>
        test.TryGetProperty("extra", out var extra) && extra.TryGetProperty("categories", out _);

    [Fact]
    public void A_scenario_s_tags_are_its_feature_s_then_its_own()
    {
        // plans/TAGS_PLAN.md §4.12: a category is a tag, and a feature's tags are each of its scenarios'
        WriteRun();

        var tests = ConvertedTests();

        Assert.Equal(["checkout", "smoke", "payments"], TagsOf(tests["Pay with an expired card"]));
        Assert.Equal(["checkout"], TagsOf(tests["Pay with a valid card"]));
        Assert.Empty(TagsOf(tests["Browse"]));
        Assert.DoesNotContain(tests.Values, HasExtraCategories);
    }

    [Fact]
    public void A_4x_report_s_labels_and_categories_are_its_tags_less_the_retry_labels_its_attempts_account_for()
    {
        // what a 4.x writer wrote: a feature's labels, a scenario's labels and categories, and a retry N label an ingest
        // lane made for each earlier attempt (§4.6, §4.13)
        WriteRun(json: true);
        var json = JsonNode.Parse(ReportHtml.Read(Report))!;
        var feature = QueryTagsTests.WithLabels(json["features"]!.AsArray(), "Checkout", "checkout");
        var scenarios = feature["scenarios"]!.AsArray().Select(s => s!.AsObject()).ToArray();
        foreach (var scenario in scenarios)
            scenario.Remove("tags");
        var expired = scenarios.Single(s => (string?)s["name"] == "Pay with an expired card");
        expired["labels"] = new JsonArray("smoke", "retry 2");
        expired["categories"] = new JsonArray("payments");
        scenarios.Single(s => (string?)s["id"] == "ctrf-pass")["labels"] = new JsonArray("retry 1", "slow");
        File.WriteAllText(Report, json.ToJsonString());

        var tests = ConvertedTests();

        // The expired card ran once, so its retry 2 is its author's; the valid card's retry 1 is its first attempt.
        Assert.Equal(["checkout", "smoke", "retry 2", "payments"], TagsOf(tests["Pay with an expired card"]));
        Assert.Equal(["checkout", "slow"], TagsOf(tests["Pay with a valid card"]));
        Assert.DoesNotContain(tests.Values, HasExtraCategories);
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
