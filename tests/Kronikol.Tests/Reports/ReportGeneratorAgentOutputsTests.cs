using System.Text.Json;
using Kronikol.Reports;
using Kronikol.Tracking;

namespace Kronikol.Tests.Reports;

/// <summary>
/// The discovery loop end to end: the two files an agent finds by listing the directory, the instruction
/// files that teach it what to do with them, and the closing console block that points at all of it. Also
/// the rule that governs every output Kronikol writes — one that throws costs the reader that file and
/// nothing else.
/// </summary>
[Collection("DiagramsFetcher")]
public class ReportGeneratorAgentOutputsTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "kronikol-agent-" + Guid.NewGuid().ToString("N"));

    public ReportGeneratorAgentOutputsTests()
    {
        Directory.CreateDirectory(_dir);
        DefaultDiagramsFetcher.Reset();
    }

    public void Dispose()
    {
        DefaultDiagramsFetcher.Reset();
        try { Directory.Delete(_dir, true); } catch { /* best effort */ }
    }

    private static ReportConfigurationOptions Options(string dir) => new()
    {
        ReportsFolderPath = dir,
        InternalFlowTracking = false,
        GenerateComponentDiagram = false,
        GenerateSpecificationsReport = false,
        GenerateSpecificationsData = false,
    };

    private static Feature[] Features(string testId, ExecutionResult result = ExecutionResult.Failed) =>
    [
        new Feature
        {
            DisplayName = "Checkout",
            Scenarios =
            [
                new Scenario
                {
                    Id = testId, DisplayName = "Pay with an expired card", Result = result,
                    ErrorMessage = result == ExecutionResult.Failed ? "Assert.Equal() Failure" : null,
                    Steps = [new ScenarioStep { Keyword = "Then", Text = "the charge succeeds", Status = result, FailureMessage = result == ExecutionResult.Failed ? "declined" : null }]
                }
            ]
        }
    ];

    private string Run(ReportConfigurationOptions options, ExecutionResult result = ExecutionResult.Failed)
    {
        var testId = "agent-" + Guid.NewGuid().ToString("N");
        RequestResponseLogger.LogPair("Pay with an expired card", testId, HttpMethod.Post, new Uri("http://payments/charge"), "payments", "Test");
        ReportGenerator.CreateStandardReportsWithDiagrams(Features(testId, result), DateTime.UtcNow.AddMinutes(-1), DateTime.UtcNow, options);
        return testId;
    }

    [Fact]
    public void The_digest_and_the_instruction_files_ship_by_default()
    {
        Run(Options(_dir));

        foreach (var file in new[] { "Failures.md", "Failures.jsonl", "CLAUDE.md", "AGENTS.md" })
            Assert.True(File.Exists(Path.Combine(_dir, file)), $"expected {file} in {_dir}");

        Assert.Contains("Pay with an expired card", File.ReadAllText(Path.Combine(_dir, "Failures.md")));
        Assert.Equal(
            File.ReadAllText(Path.Combine(_dir, "CLAUDE.md")),
            File.ReadAllText(Path.Combine(_dir, "AGENTS.md")));
    }

    [Fact]
    public void Each_new_output_can_be_turned_off_on_its_own()
    {
        var options = Options(_dir);
        options.GenerateFailuresDigest = false;
        options.WriteAgentInstructions = false;

        Run(options);

        Assert.False(File.Exists(Path.Combine(_dir, "Failures.md")));
        Assert.False(File.Exists(Path.Combine(_dir, "Failures.jsonl")));
        Assert.False(File.Exists(Path.Combine(_dir, "CLAUDE.md")));
        Assert.False(File.Exists(Path.Combine(_dir, "AGENTS.md")));
        Assert.True(File.Exists(Path.Combine(_dir, "TestRunReport.html")));
    }

    [Fact]
    public void The_digest_addresses_match_what_the_query_tool_would_use()
    {
        Run(Options(_dir));

        var failure = Assert.Single(FailuresJsonl.Failures(File.ReadAllText(Path.Combine(_dir, "Failures.jsonl"))));
        var address = failure.GetProperty("address").GetString();

        using var report = JsonDocument.Parse(File.ReadAllText(Path.Combine(_dir, "TestRunReport.json")));
        var ordinal = int.Parse(address![1..]);
        var scenarios = report.RootElement.GetProperty("features").EnumerateArray()
            .SelectMany(f => f.GetProperty("scenarios").EnumerateArray()).ToArray();

        Assert.Equal("Pay with an expired card", scenarios[ordinal].GetProperty("name").GetString());
        Assert.Equal(scenarios[ordinal].GetProperty("stableId").GetString(), failure.GetProperty("stableId").GetString());
    }

    [Fact]
    public void The_digest_addresses_still_match_when_case_decides_the_feature_order()
    {
        // The single-feature fact above cannot see a comparer difference. These two names sort one way
        // under Ordinal ("Order API" first - upper case wins on the third letter) and the other way under
        // the culture-sensitive comparer the JSON writer uses, so a digest that numbers scenarios its own
        // way sends the reader to the wrong one. Both already start capitalised, so CapitaliseTitles - on
        // by default, and the reason a lower-case initial cannot reach this far - leaves them alone.
        var testId = "agent-" + Guid.NewGuid().ToString("N");
        Feature[] features =
        [
            new Feature { DisplayName = "Order API", Scenarios = [new Scenario { Id = "a1", DisplayName = "Place an order", Result = ExecutionResult.Passed }] },
            new Feature { DisplayName = "Order api", Scenarios = [new Scenario { Id = testId, DisplayName = "Cancel an order", Result = ExecutionResult.Failed, ErrorMessage = "Assert.Equal() Failure" }] }
        ];
        ReportGenerator.CreateStandardReportsWithDiagrams(features, DateTime.UtcNow.AddMinutes(-1), DateTime.UtcNow, Options(_dir));

        var caseFailure = Assert.Single(FailuresJsonl.Failures(File.ReadAllText(Path.Combine(_dir, "Failures.jsonl"))));
        var ordinal = int.Parse(caseFailure.GetProperty("address").GetString()![1..]);

        using var report = JsonDocument.Parse(File.ReadAllText(Path.Combine(_dir, "TestRunReport.json")));
        var scenarios = report.RootElement.GetProperty("features").EnumerateArray()
            .SelectMany(f => f.GetProperty("scenarios").EnumerateArray()).ToArray();

        Assert.Equal("Cancel an order", scenarios[ordinal].GetProperty("name").GetString());
    }

    [Fact]
    public void A_digest_that_cannot_be_written_costs_only_the_digest()
    {
        // A directory where the file should go: the write throws, and the isolated output list has to
        // absorb it. Before RunOutputs existed this took the whole report down with it.
        Directory.CreateDirectory(Path.Combine(_dir, "Failures.md"));

        var original = Console.Out;
        var captured = new StringWriter();
        try
        {
            Console.SetOut(captured);
            Run(Options(_dir));
        }
        finally
        {
            Console.SetOut(original);
        }

        // Every other output is intact, including the instruction files that share the isolated list.
        Assert.True(File.Exists(Path.Combine(_dir, "TestRunReport.html")));
        Assert.True(File.Exists(Path.Combine(_dir, "TestRunReport.json")));
        Assert.True(File.Exists(Path.Combine(_dir, "CLAUDE.md")));
        Assert.True(File.Exists(Path.Combine(_dir, "AGENTS.md")));
        // ...and the digest's own two halves are two actions, so the machine-readable one survives the
        // human-readable one. They are written from the same data by the same code, so what they share is
        // generation — but the WRITES fail independently: one file held open by a reader or an editor, one
        // path already taken by a directory. Losing the jsonl because the markdown could not be replaced
        // leaves the PREVIOUS run's jsonl on disk in a directory an agent is about to read: stale,
        // plausible, and describing different failures.
        Assert.True(File.Exists(Path.Combine(_dir, "Failures.jsonl")),
            "the jsonl went down with the markdown");
        Assert.Contains("\"kind\":\"failure\"", File.ReadAllText(Path.Combine(_dir, "Failures.jsonl")), StringComparison.Ordinal);

        // The failure is announced rather than swallowed. It cannot reach the data files' `diagnostics`
        // array: that snapshot is taken before the outputs run, so the HTML and the JSON agree with each
        // other — an output's own failure is necessarily later than the bytes it would have to appear in.
        Assert.Contains("could not write Failures.md", captured.ToString());
        // And the pointer never claims a file that is not on disk.
        Assert.DoesNotContain("Failures.md ", captured.ToString().Split('\n')[0]);
    }

    [Fact]
    public void The_interop_outputs_stay_off_until_they_are_asked_for()
    {
        // Both are written for somebody else - a CI action, a documentation site - so unlike the digest
        // and the instruction files they cost every consumer nothing until a consumer wants them. Which
        // also means the default byte output of a run is exactly what it was before they existed.
        Run(Options(_dir));

        Assert.False(File.Exists(Path.Combine(_dir, "ctrf-report.json")));
        Assert.False(File.Exists(Path.Combine(_dir, "Specifications.md")));
    }

    [Fact]
    public void The_ctrf_report_is_written_when_it_is_asked_for_and_describes_the_same_run()
    {
        var options = Options(_dir);
        options.GenerateCtrfReport = true;

        Run(options);

        using var ctrf = JsonDocument.Parse(File.ReadAllText(Path.Combine(_dir, "ctrf-report.json")));
        var results = ctrf.RootElement.GetProperty("results");
        Assert.Equal("CTRF", ctrf.RootElement.GetProperty("reportFormat").GetString());
        Assert.Equal(1, results.GetProperty("summary").GetProperty("failed").GetInt32());
        var test = results.GetProperty("tests").EnumerateArray().Single();
        Assert.Equal("Pay with an expired card", test.GetProperty("name").GetString());
        Assert.Equal("failed", test.GetProperty("status").GetString());

        // The address it hands a consumer has to be the one kronikol query answers to.
        using var report = JsonDocument.Parse(File.ReadAllText(Path.Combine(_dir, "TestRunReport.json")));
        var ordinal = int.Parse(test.GetProperty("extra").GetProperty("kronikolAddress").GetString()![1..]);
        var scenarios = report.RootElement.GetProperty("features").EnumerateArray()
            .SelectMany(f => f.GetProperty("scenarios").EnumerateArray()).ToArray();
        Assert.Equal("Pay with an expired card", scenarios[ordinal].GetProperty("name").GetString());
    }

    [Fact]
    public void A_ctrf_report_that_cannot_be_written_costs_only_the_ctrf_report()
    {
        Directory.CreateDirectory(Path.Combine(_dir, "ctrf-report.json"));
        var options = Options(_dir);
        options.GenerateCtrfReport = true;

        var original = Console.Out;
        var captured = new StringWriter();
        try
        {
            Console.SetOut(captured);
            Run(options);
        }
        finally
        {
            Console.SetOut(original);
        }

        Assert.True(File.Exists(Path.Combine(_dir, "TestRunReport.html")));
        Assert.True(File.Exists(Path.Combine(_dir, "Failures.md")));
        Assert.Contains("could not write ctrf-report.json", captured.ToString());
    }

    [Fact]
    public void The_specification_narrative_is_written_when_it_is_asked_for()
    {
        var options = Options(_dir);
        options.GenerateSpecificationsMarkdown = true;

        Run(options);

        var markdown = File.ReadAllText(Path.Combine(_dir, "Specifications.md"));
        Assert.Contains("## Checkout", markdown);
        Assert.Contains("Pay with an expired card", markdown);
        // A specification, not a second report: the run was red and nothing here says so.
        Assert.DoesNotContain("Failed", markdown);
        Assert.DoesNotContain("payments/charge", markdown);
    }

    [Fact]
    public void A_specification_narrative_that_cannot_be_written_costs_only_the_narrative()
    {
        Directory.CreateDirectory(Path.Combine(_dir, "Specifications.md"));
        var options = Options(_dir);
        options.GenerateSpecificationsMarkdown = true;

        var original = Console.Out;
        var captured = new StringWriter();
        try
        {
            Console.SetOut(captured);
            Run(options);
        }
        finally
        {
            Console.SetOut(original);
        }

        Assert.True(File.Exists(Path.Combine(_dir, "TestRunReport.html")));
        Assert.True(File.Exists(Path.Combine(_dir, "Failures.md")));
        Assert.Contains("could not write Specifications.md", captured.ToString());
    }

    [Fact]
    public void The_run_ends_by_pointing_at_the_reports()
    {
        var original = Console.Out;
        var captured = new StringWriter();
        try
        {
            Console.SetOut(captured);
            Run(Options(_dir));
        }
        finally
        {
            Console.SetOut(original);
        }

        var text = captured.ToString();
        Assert.Contains($"Kronikol: reports written to {_dir}", text);
        Assert.Contains($"1 failed — kronikol query failures {_dir}", text);
        Assert.Contains("Pay with an expired card", text);
        Assert.Contains("CLAUDE.md", text);
        // Never content: the URI of the call this scenario made is in the report, not in the CI log.
        Assert.DoesNotContain("payments/charge", text);
    }

    [Fact]
    public void The_pointer_can_be_silenced()
    {
        var options = Options(_dir);
        options.WriteRunSummaryToConsole = false;

        var original = Console.Out;
        var captured = new StringWriter();
        try
        {
            Console.SetOut(captured);
            Run(options);
        }
        finally
        {
            Console.SetOut(original);
        }

        Assert.DoesNotContain("Kronikol: reports written to", ConsoleLines.About(captured.ToString(), _dir));
    }

    [Fact]
    public void The_ci_summary_says_how_to_debug_the_run()
    {
        var options = Options(_dir);
        options.WriteCiSummary = true;

        Run(options);

        var summary = File.ReadAllText(Path.Combine(_dir, "CiSummary.md"));
        Assert.Contains("## Debug this run", summary);
        Assert.Contains($"kronikol query failures {_dir}", summary);
        Assert.Contains("Failures.md", summary);
    }

    [Fact]
    public void A_green_run_writes_the_digest_and_says_nothing_on_the_console()
    {
        var original = Console.Out;
        var captured = new StringWriter();
        try
        {
            Console.SetOut(captured);
            Run(Options(_dir), ExecutionResult.Passed);
        }
        finally
        {
            Console.SetOut(original);
        }

        Assert.Contains("# No failures", File.ReadAllText(Path.Combine(_dir, "Failures.md")));
        // A green run writes one header line rather than nothing: an empty file and a missing file are the
        // same bytes to a reader, and the absence of this file is the signal that the run did not finish.
        var greenJsonl = File.ReadAllText(Path.Combine(_dir, "Failures.jsonl"));
        Assert.Equal(0, FailuresJsonl.Header(greenJsonl).GetProperty("failures").GetInt32());
        Assert.Empty(FailuresJsonl.Failures(greenJsonl));
        var own = ConsoleLines.About(captured.ToString(), _dir);
        var pointer = own.Split('\n').Where(l => l.StartsWith("Kronikol: reports written to", StringComparison.Ordinal)).ToArray();
        Assert.Single(pointer);
        Assert.DoesNotContain("failed —", own);
    }
}
