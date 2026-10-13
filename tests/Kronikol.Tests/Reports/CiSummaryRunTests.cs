using System.Text.Json;
using System.Text.RegularExpressions;
using Kronikol.History;
using Kronikol.Query;
using Kronikol.Reports;
using Kronikol.Tool;
using Kronikol.Tracking;

namespace Kronikol.Tests.Reports;

/// <summary>
/// The CI text a real run and a real merge write, read the way its readers read it (plans/CI_SUMMARY_AUDIENCE_PLAN.md):
/// the section from the job summary, its commands pasted into a shell in a folder that holds what
/// <c>gh run download</c> gives (each artifact in a folder of its own name), and each <c>kronikol query</c> line run
/// through the tool. <see cref="CiSummaryAudienceTests"/> pins the text; this checks that the text works.
///
/// <para>Each run is handed its own environment, a GitHub Actions run whose job summary is a file of this class's.
/// In the DiagramsFetcher collection, as every class that drives a run end to end with the CI summary on is.</para>
/// </summary>
[Collection("DiagramsFetcher")]
public class CiSummaryRunTests : IDisposable
{
    private const string RunId = "123456789";

    private readonly string _root = Directory.CreateTempSubdirectory("kronikol-ci-audience").FullName;

    public CiSummaryRunTests() => DefaultDiagramsFetcher.Reset();

    public void Dispose()
    {
        DefaultDiagramsFetcher.Reset();
        try { Directory.Delete(_root, recursive: true); } catch (IOException) { } catch (UnauthorizedAccessException) { }
        GC.SuppressFinalize(this);
    }

    private string StepSummary => Path.Combine(_root, "step-summary.md");

    /// <summary>Where the reader pastes the commands: <c>gh run download</c> makes <c>kronikol-&lt;run&gt;</c> here.</summary>
    private string Reader => Path.Combine(_root, "reader");

    private string Download => Path.Combine(Reader, $"kronikol-{RunId}");

    private static Func<string, string?> Env(params (string Name, string? Value)[] variables) =>
        name => variables.LastOrDefault(v => v.Name == name) is { Name: not null } found ? found.Value
            : name == HistoryFormat.EnvironmentVariable ? HistoryFormat.EnvironmentOff
            : null;

    /// <summary>
    /// Run 123456789 of acme/shop on GitHub Actions, with this class's folder as its workspace. Without
    /// <paramref name="runId"/> there is nothing to download, and the text keeps today's commands on the folder.
    /// </summary>
    private Func<string, string?> GitHub(string? runId = RunId) => Env(
        ("GITHUB_ACTIONS", "true"), ("GITHUB_STEP_SUMMARY", StepSummary), ("GITHUB_WORKSPACE", _root),
        ("GITHUB_SERVER_URL", "https://github.com"), ("GITHUB_REPOSITORY", "acme/shop"), ("GITHUB_RUN_ID", runId), ("GITHUB_RUN_ATTEMPT", "1"));

    /// <summary>
    /// A run of <paramref name="failing"/> failing scenarios and one passing into <paramref name="reports"/>. Returns the
    /// console lines that name that folder: another class's run may print to the same console meanwhile.
    /// </summary>
    private static string Run(string reports, Func<string, string?> env, Action<ReportConfigurationOptions>? configure = null,
        ExecutionResult result = ExecutionResult.Failed, string suite = "Checkout", int failing = 1)
    {
        using var isolation = RequestResponseLogger.IsolateForTests();
        var options = new ReportConfigurationOptions
        {
            ReportsFolderPath = reports,
            SuiteName = suite,
            InternalFlowTracking = false,
            GenerateComponentDiagram = false,
            GenerateSpecificationsReport = false,
            GenerateSpecificationsData = false,
        };
        configure?.Invoke(options);

        var features = new[]
        {
            new Feature
            {
                DisplayName = "Checkout",
                Scenarios =
                [
                    .. Enumerable.Range(1, failing).Select(i => new Scenario
                    {
                        Id = "audience-" + Guid.NewGuid().ToString("N"),
                        DisplayName = failing == 1 ? "Pay with an expired card" : $"Pay with an expired card {i}",
                        Result = result,
                        ErrorMessage = result == ExecutionResult.Failed ? "Expected 200 but got 500" : null
                    }),
                    new Scenario { Id = "audience-" + Guid.NewGuid().ToString("N"), DisplayName = "Pay with a valid card", Result = ExecutionResult.Passed }
                ]
            }
        };
        foreach (var scenario in features.SelectMany(f => f.Scenarios))
            RequestResponseLogger.LogPair(scenario.DisplayName, scenario.Id, HttpMethod.Post, new Uri("http://payments/charge"), "payments", "Test");

        var original = Console.Out;
        var captured = new StringWriter();
        try
        {
            Console.SetOut(captured);
            DefaultDiagramsFetcher.Reset();
            ReportGenerator.CreateStandardReportsWithDiagramsInEnvironment(features, DateTime.UtcNow.AddMinutes(-1), DateTime.UtcNow, options, null, env);
        }
        finally
        {
            Console.SetOut(original);
        }

        return ConsoleLines.About(captured.ToString(), reports);
    }

    private static (int Exit, string Output, string Error) Query(params string[] args)
    {
        var output = new StringWriter();
        var error = new StringWriter();
        var exit = QueryCommand.Run(args, output, error, _ => null);
        return (exit, output.ToString(), error.ToString());
    }

    /// <summary>The <c>kronikol query</c> lines of the section's bash block, as a reader pastes them.</summary>
    private static string[] QueryLines(string section)
    {
        var block = Regex.Match(section, "```bash\n(.*?)\n```", RegexOptions.Singleline);
        Assert.True(block.Success, $"no bash block in:\n{section}");
        return block.Groups[1].Value.Split('\n').Where(l => l.StartsWith("kronikol query ", StringComparison.Ordinal)).ToArray();
    }

    private static readonly Regex Find = new(
        "^\"\\$\\(find (?<dir>\\S+) -name (?<name>\\S+) -not -path '\\*/runs/\\*' -not -path '\\*/baseline/\\*' -print -quit\\)\"$");

    /// <summary>
    /// What bash does with one <c>kronikol query</c> line in <paramref name="cwd"/>: a folder is relative to it, and
    /// <c>"$(find …)"</c> is the first file of that name outside <c>runs/</c> and <c>baseline/</c>.
    /// </summary>
    private static (int Exit, string Output, string Error) Shell(string line, string cwd)
    {
        var words = line["kronikol query ".Length..].Split(' ', 2);
        var argument = words[1];
        string path;
        if (Find.Match(argument) is { Success: true } find)
        {
            var folder = Path.Combine(cwd, find.Groups["dir"].Value);
            path = Directory.EnumerateFiles(folder, find.Groups["name"].Value, SearchOption.AllDirectories)
                .First(file => !ReportFolders.IsReserved(folder, file));
        }
        else
        {
            // One plain word, as bash reads it: a space here would be two arguments.
            Assert.DoesNotContain(' ', argument);
            path = Path.Combine(cwd, argument);
        }

        return Query(words[0], path);
    }

    private static void CopyDirectory(string from, string to)
    {
        foreach (var directory in Directory.EnumerateDirectories(from, "*", SearchOption.AllDirectories))
            Directory.CreateDirectory(Path.Combine(to, Path.GetRelativePath(from, directory)));
        Directory.CreateDirectory(to);
        foreach (var file in Directory.EnumerateFiles(from, "*", SearchOption.AllDirectories))
            File.Copy(file, Path.Combine(to, Path.GetRelativePath(from, file)));
    }

    // ─── R2: the commands answer where the reader is ───────────

    [Fact]
    public void The_printed_commands_answer_on_the_layout_gh_run_download_gives()
    {
        var reports = Path.Combine(_root, "shop", "Reports");
        Run(reports, GitHub());
        var section = File.ReadAllText(StepSummary);
        CopyDirectory(reports, Path.Combine(Download, "test-reports"));

        Assert.Contains($"test -d kronikol-{RunId} || gh run download {RunId} --repo acme/shop --dir kronikol-{RunId}\n", section);
        var lines = QueryLines(section);
        Assert.Equal([$"kronikol query summary kronikol-{RunId}", $"kronikol query failures kronikol-{RunId}"], lines);
        foreach (var line in lines)
        {
            var (exit, output, error) = Shell(line, Reader);
            Assert.True(exit == 0, $"{line}\n{error}");
            Assert.Contains("Pay with an expired card", output);
        }
    }

    [Fact]
    public void The_printed_commands_find_a_merged_report_by_its_name()
    {
        var shards = Path.Combine(_root, "shards");
        Directory.CreateDirectory(shards);
        WriteShard(Path.Combine(shards, "runner1.json"), "Pay with an expired card", "Failed");
        var merged = Path.Combine(_root, "merged");
        Directory.CreateDirectory(merged);

        var mergeOutput = new StringWriter();
        var mergeError = new StringWriter();
        var mergeExit = MergeCommand.Run([shards, "-o", Path.Combine(merged, "Combined.html")], mergeOutput, mergeError, GitHub());
        Assert.True(mergeExit == 0, mergeError.ToString());

        var section = File.ReadAllText(StepSummary);
        CopyDirectory(merged, Path.Combine(Download, "merged-report"));

        var lines = QueryLines(section);
        Assert.Equal(2, lines.Length);
        Assert.All(lines, line => Assert.Contains($"-name Combined.json", line));
        foreach (var line in lines)
        {
            var (exit, output, error) = Shell(line, Reader);
            Assert.True(exit == 0, $"{line}\n{error}");
            Assert.Contains("Pay with an expired card", output);
        }

        // The merge's own log copy and annotation say the same.
        Assert.Contains("## Debug this run", mergeOutput.ToString());
        Assert.Contains($"::notice title=Kronikol::1 failed of 1 scenarios. Reports in merged (people: Combined.html or Failures.md; agents: gh run download {RunId} --repo acme/shop --dir kronikol-{RunId}, then kronikol query failures \"$(find kronikol-{RunId} -name Combined.json",
            mergeOutput.ToString());
    }

    /// <summary>
    /// Every matrix and sharded run's download holds several reports, and the folder lookup lists them: with their suites
    /// and failure counts, from the Run.json beside each, so the reader can tell the legs apart (F12, Q12).
    /// </summary>
    [Fact]
    public void Several_reports_in_a_download_are_listed_with_their_suites_and_counts()
    {
        var checkout = Path.Combine(_root, "checkout", "Reports");
        var payments = Path.Combine(_root, "payments", "Reports");
        Run(checkout, GitHub(), suite: "Checkout");
        var section = File.ReadAllText(StepSummary);
        Run(payments, GitHub(), suite: "Payments", failing: 2);
        CopyDirectory(checkout, Path.Combine(Download, "reports-checkout"));
        CopyDirectory(payments, Path.Combine(Download, "reports-payments"));

        var (exit, _, error) = Shell(QueryLines(section).Last(), Reader);

        Assert.Equal(2, exit);
        Assert.StartsWith("Several reports under ", error);
        Assert.Matches(@"reports-checkout[\\/]TestRunReport\.json  [\d.]+ (B|KB|MB) · suite Checkout · 1 of 2 failed", error);
        Assert.Matches(@"reports-payments[\\/]TestRunReport\.json  [\d.]+ (B|KB|MB) · suite Payments · 2 of 3 failed", error);
    }

    // ─── R1: the data file the commands are handed (F11) ───────

    [Fact]
    public void A_custom_data_file_name_is_named_by_the_pointer_the_section_and_the_annotation()
    {
        var reports = Path.Combine(_root, "custom", "Reports");
        var console = Run(reports, GitHub(runId: null), o => o.HtmlTestRunReportFileName = "Checkout");
        var data = Path.Combine(reports, "Checkout.json");

        var pointer = console.Split('\n').Single(l => l.Contains("1 failed — kronikol query failures ", StringComparison.Ordinal)).TrimEnd('\r');
        var command = pointer[(pointer.IndexOf("kronikol query failures ", StringComparison.Ordinal) + "kronikol query failures ".Length)..];
        Assert.Equal(data, command.Trim('"'));
        Assert.Contains($"kronikol query failures {command}\n", File.ReadAllText(StepSummary));
        Assert.Contains(console.Split('\n'), l => l.StartsWith("::notice title=Kronikol::", StringComparison.Ordinal)
                                                  && l.Contains($"kronikol query failures {command}", StringComparison.Ordinal));

        // ...and the command answers, where "kronikol query failures <folder>" said "No TestRunReport.json under".
        var (exit, output, error) = Query("failures", command.Trim('"'));
        Assert.True(exit == 0, error);
        Assert.Contains("Pay with an expired card", output);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void A_run_without_a_json_data_file_prints_no_query_command(bool xml)
    {
        var reports = Path.Combine(_root, xml ? "xml" : "none", "Reports");
        var console = Run(reports, GitHub(runId: null), o =>
        {
            if (xml)
                o.TestRunReportDataFormat = DataFormat.Xml;
            else
                o.GenerateTestRunReportData = false;
        });

        var section = File.ReadAllText(StepSummary);
        Assert.Contains("## Debug this run", section);
        Assert.DoesNotContain("kronikol query", section);
        Assert.DoesNotContain("kronikol query", console);
        Assert.Contains(xml ? "never open TestRunReport.xml" : "never open TestRunReport.html", console);
        Assert.Contains(xml ? "do not open `TestRunReport.xml` or `TestRunReport.html` — " : "do not open `TestRunReport.html` — ", section);
    }

    // ─── R3: the full summary on a green run ───────────────────

    [Fact]
    public void A_green_run_s_full_summary_ends_with_the_report_for_people()
    {
        var reports = Path.Combine(_root, "green", "Reports");
        Run(reports, GitHub(), o => o.WriteCiSummary = true, ExecutionResult.Passed);

        var markdown = File.ReadAllText(Path.Combine(reports, "CiSummary.md"));
        Assert.DoesNotContain("## Debug this run", markdown);
        Assert.DoesNotContain("AI agents", markdown);
        Assert.EndsWith(
            "\n## The report\n\n**People:** open `TestRunReport.html` in a browser to read the whole run, diagrams included. "
            + $"This job wrote it to `green/Reports`; it is in this run's [artifacts](https://github.com/acme/shop/actions/runs/{RunId}#artifacts) when the job uploads that folder.\n\n",
            markdown);
        Assert.Equal(markdown, File.ReadAllText(StepSummary));
    }

    [Fact]
    public void A_failing_run_s_full_summary_still_ends_with_how_to_debug_it()
    {
        var reports = Path.Combine(_root, "red", "Reports");
        Run(reports, GitHub(), o => o.WriteCiSummary = true);

        var markdown = File.ReadAllText(Path.Combine(reports, "CiSummary.md"));
        var debug = markdown.IndexOf("\n## Debug this run\n\n**People:** open `TestRunReport.html`", StringComparison.Ordinal);
        Assert.True(debug > 0, markdown);
        Assert.Contains("\n\n**AI agents:** do not open `TestRunReport.json` or `TestRunReport.html` — ", markdown[debug..]);
        Assert.DoesNotContain("## The report", markdown);
    }

    /// <summary>One mergeable shard: a scenario with the given result, as a test project writes it for <c>kronikol merge</c>.</summary>
    private static void WriteShard(string path, string scenarioName, string result) =>
        File.WriteAllText(path, $$"""
            {
              "mergeableFormatVersion": 1,
              "kronikolVersion": "4.19.0",
              "startTime": "2026-01-01T10:00:00Z",
              "endTime": "2026-01-01T10:01:00Z",
              "features": [
                { "name": "Checkout", "scenarios": [
                  { "id": "0-1002",
                    "name": {{JsonSerializer.Serialize(scenarioName)}},
                    "result": "{{result}}",
                    "durationSeconds": 1.0,
                    "errorMessage": "Expected 200 but got 500",
                    "diagrams": ["@startuml\nTests->Pricing\n@enduml"],
                    "httpInteractions": [] }
                ] }
              ]
            }
            """);
}
