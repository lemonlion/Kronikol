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

        var jsonl = File.ReadAllText(Path.Combine(_dir, "Failures.jsonl")).TrimEnd('\n');
        using var document = JsonDocument.Parse(jsonl);
        var address = document.RootElement.GetProperty("address").GetString();

        using var report = JsonDocument.Parse(File.ReadAllText(Path.Combine(_dir, "TestRunReport.json")));
        var ordinal = int.Parse(address![1..]);
        var scenarios = report.RootElement.GetProperty("features").EnumerateArray()
            .SelectMany(f => f.GetProperty("scenarios").EnumerateArray()).ToArray();

        Assert.Equal("Pay with an expired card", scenarios[ordinal].GetProperty("name").GetString());
        Assert.Equal(scenarios[ordinal].GetProperty("stableId").GetString(), document.RootElement.GetProperty("stableId").GetString());
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
        // The two halves of the digest are one action, so the jsonl goes with the markdown.
        Assert.False(File.Exists(Path.Combine(_dir, "Failures.jsonl")));

        // The failure is announced rather than swallowed. It cannot reach the data files' `diagnostics`
        // array: that snapshot is taken before the outputs run, so the HTML and the JSON agree with each
        // other — an output's own failure is necessarily later than the bytes it would have to appear in.
        Assert.Contains("could not write Failures.md", captured.ToString());
        // And the pointer never claims a file that is not on disk.
        Assert.DoesNotContain("Failures.md ", captured.ToString().Split('\n')[0]);
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

        Assert.DoesNotContain("Kronikol: reports written to", captured.ToString());
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
        Assert.Equal("", File.ReadAllText(Path.Combine(_dir, "Failures.jsonl")));
        var pointer = captured.ToString().Split('\n').Where(l => l.StartsWith("Kronikol: reports written to", StringComparison.Ordinal)).ToArray();
        Assert.Single(pointer);
        Assert.DoesNotContain("failed —", captured.ToString());
    }
}
