using System.Text;
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
        "^\"\\$\\(find (?<dir>\\S+) -name (?<name>'[^']*'|\\S+) -not -path '\\*/runs/\\*' -not -path '\\*/baseline/\\*' -print -quit\\)\"(?<more>.*)$");

    /// <summary>
    /// What bash does with one <c>kronikol query</c> line in <paramref name="cwd"/>: a folder is relative to it, and
    /// <c>"$(find …)"</c> is the first file of that name outside <c>runs/</c> and <c>baseline/</c>.
    /// </summary>
    private static (int Exit, string Output, string Error) Shell(string line, string cwd)
    {
        var rest = line["kronikol query ".Length..];
        var verb = rest[..rest.IndexOf(' ', StringComparison.Ordinal)];
        rest = rest[(verb.Length + 1)..];
        string path;
        string[] more;
        if (Find.Match(rest) is { Success: true } find)
        {
            var folder = Path.Combine(cwd, Word(find.Groups["dir"].Value));
            var name = Literal(Word(find.Groups["name"].Value));
            path = Directory.EnumerateFiles(folder, "*", SearchOption.AllDirectories)
                .First(file => string.Equals(Path.GetFileName(file), name, StringComparison.Ordinal) && !ReportFolders.IsReserved(folder, file));
            more = Words(find.Groups["more"].Value);
        }
        else
        {
            var words = Words(rest);
            path = Path.Combine(cwd, words[0]);
            more = words[1..];
        }

        return Query([verb, path, .. more]);
    }

    /// <summary>
    /// A line split as bash splits it: single quotes keep every character, a backslash outside them keeps the next one,
    /// and any other character bash reads as more than itself (an expansion, a glob, a separator) fails the fact.
    /// </summary>
    private static string[] Words(string text)
    {
        var words = new List<string>();
        var word = new StringBuilder();
        var inWord = false;
        for (var i = 0; i < text.Length; i++)
        {
            var c = text[i];
            if (c == ' ')
            {
                if (inWord)
                    words.Add(word.ToString());
                word.Clear();
                inWord = false;
                continue;
            }

            inWord = true;
            if (c == '\'')
            {
                var end = text.IndexOf('\'', i + 1);
                Assert.True(end > i, $"an unclosed quote in: {text}");
                word.Append(text, i + 1, end - i - 1);
                i = end;
            }
            else if (c == '\\')
            {
                word.Append(text[++i]);
            }
            else
            {
                Assert.True("\"$`;&|<>()*?[]!#~{}".IndexOf(c) < 0, $"bash reads '{c}' as more than a character in: {text}");
                word.Append(c);
            }
        }

        if (inWord)
            words.Add(word.ToString());
        return words.ToArray();
    }

    private static string Word(string text) => Assert.Single(Words(text));

    /// <summary>A <c>find -name</c> pattern that matches one name: every pattern character escaped.</summary>
    private static string Literal(string pattern)
    {
        var name = new StringBuilder();
        for (var i = 0; i < pattern.Length; i++)
        {
            Assert.True(pattern[i] is not ('*' or '?' or '['), $"an unescaped pattern character in: {pattern}");
            name.Append(pattern[i] == '\\' ? pattern[++i] : pattern[i]);
        }

        return name.ToString();
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
        Assert.Matches(@"reports-checkout[\\/]TestRunReport\.krkl  [\d.]+ (B|KB|MB) · suite Checkout · 1 of 2 failed", error);
        Assert.Matches(@"reports-payments[\\/]TestRunReport\.krkl  [\d.]+ (B|KB|MB) · suite Payments · 2 of 3 failed", error);
    }

    // ─── R1: the data file the commands are handed (F11) ───────

    [Fact]
    public void A_custom_data_file_name_is_named_by_the_pointer_the_section_and_the_annotation()
    {
        var reports = Path.Combine(_root, "custom", "Reports");
        var console = Run(reports, GitHub(runId: null), o => o.HtmlTestRunReportFileName = "Checkout");
        var data = Path.Combine(reports, "Checkout.krkl");

        var pointer = console.Split('\n').Single(l => l.Contains("1 failed — kronikol query failures ", StringComparison.Ordinal)).TrimEnd('\r');
        var command = pointer[(pointer.IndexOf("kronikol query failures ", StringComparison.Ordinal) + "kronikol query failures ".Length)..];
        Assert.Equal(data, command.Trim('"'));
        // The section's block is bash, which names the same file as bash reads it.
        var sectionLine = QueryLines(File.ReadAllText(StepSummary)).Single(l => l.StartsWith("kronikol query failures ", StringComparison.Ordinal));
        Assert.Equal(data, Assert.Single(Words(sectionLine["kronikol query failures ".Length..])));
        Assert.Contains(console.Split('\n'), l => l.StartsWith("::notice title=Kronikol::", StringComparison.Ordinal)
                                                  && l.Contains($"kronikol query failures {command}", StringComparison.Ordinal));

        // ...and the command answers, where "kronikol query failures <folder>" said "No TestRunReport.json under".
        var (exit, output, error) = Query("failures", command.Trim('"'));
        Assert.True(exit == 0, error);
        Assert.Contains("Pay with an expired card", output);
    }

    /// <summary>
    /// The line that finds a data file by name had run against a download only for a merge. A run with its own report
    /// name prints it too on CI, with a name bash and <c>find</c> both read characters of, and its previous-run line
    /// names the kept run, which <c>--run</c> has to open under that name.
    /// </summary>
    [Fact]
    public void A_custom_data_file_name_is_found_in_the_download_and_its_kept_run_opens()
    {
        // On CI a run keeps the one before it only when asked to (KRONIKOL_KEEP_RUNS).
        Func<string, string?> Keeping(string runId) => name => name == "KRONIKOL_KEEP_RUNS" ? "3" : GitHub(runId)(name);
        var reports = Path.Combine(_root, "custom-ci", "Reports");
        Run(reports, Keeping("111"), o => o.HtmlTestRunReportFileName = "Checkout [linux]");
        File.Delete(StepSummary);
        // A second process, as the next CI run is: one process rotates a folder once.
        RunRotation.ForgetForTests(reports);
        Run(reports, Keeping(RunId), o => o.HtmlTestRunReportFileName = "Checkout [linux]");
        var section = File.ReadAllText(StepSummary);
        CopyDirectory(reports, Path.Combine(Download, "test-reports"));

        var lines = QueryLines(section);
        Assert.Equal(2, lines.Length);
        Assert.All(lines, line => Assert.Contains(@"-name 'Checkout \[linux\].krkl'", line));
        var previous = Regex.Match(section, "`--run ([^`]+)` opens it");
        Assert.True(previous.Success, section);
        var kept = previous.Groups[1].Value;
        Assert.Equal("gh_111_1", kept);
        foreach (var line in lines.Append(lines[^1] + " --run " + kept))
        {
            var (exit, output, error) = Shell(line, Reader);
            Assert.True(exit == 0, $"{line}\n{error}");
            Assert.Contains("Pay with an expired card", output);
        }
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
        Assert.Contains("\n\n**AI agents:** do not open `TestRunReport.krkl` or `TestRunReport.html` — ", markdown[debug..]);
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
