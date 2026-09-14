using Kronikol.Reports;

namespace Kronikol.Tests.Reports;

/// <summary>
/// The last thing a run says on the console: where the reports are, how big the JSON is, what failed and
/// the command that explains it. Paths, counts, stableIds and scenario names only — CI logs are read far
/// more widely than artifacts, so no message, URI, SQL or body ever travels on this channel.
/// </summary>
public class RunEndPointerTests
{
    // Native to whichever platform is running, because half of what this class pins is how a path is
    // SHAPED on the way to the console: that the directory travels through verbatim, and that the
    // instruction file beside it is joined with the platform's own separator. A Windows literal asserted
    // on a Linux runner tests neither, and the mismatch is invisible until CI runs it.
    private static readonly string Dir = OperatingSystem.IsWindows() ? @"C:\proj\Reports" : "/proj/Reports";

    private static RunSummary Summary(int failures, bool agents = true, string? digest = "Failures.md") => new(
        Dir,
        [new RunSummaryFile("TestRunReport.html", 1_200_000), new RunSummaryFile("TestRunReport.json", 50_540_000), .. digest is null ? Array.Empty<RunSummaryFile>() : [new RunSummaryFile(digest, 12_000)]],
        ScenarioCount: 40,
        Failures: Enumerable.Range(0, failures).Select(i => new RunSummaryFailure(i.ToString("x16"), "Checkout", $"Scenario {i}")).ToArray(),
        AgentInstructionsWritten: agents);

    [Fact]
    public void First_line_names_the_directory_the_files_and_the_json_size()
    {
        var text = RunSummaryConsoleWriter.Build(Summary(0));

        Assert.StartsWith($"Kronikol: reports written to {Dir}  (TestRunReport.html · TestRunReport.json 48.2 MB · Failures.md)", text);
    }

    /// <summary>
    /// <c>kronikol query &lt;dir&gt;</c> finds a <c>TestRunReport.json</c> and nothing else. A merge names
    /// its data file after <c>-o</c>, so a pointer that printed the directory printed a command that fails.
    /// </summary>
    [Fact]
    public void The_pointer_hands_query_the_file_when_a_directory_lookup_would_not_find_it()
    {
        var merged = Path.Combine(Dir, "Combined.json");
        var summary = Summary(2) with
        {
            Files = [new RunSummaryFile("Combined.html", 1_200_000), new RunSummaryFile("Combined.json", 50_540_000), new RunSummaryFile("Failures.md", 12_000)],
            QueryTarget = merged
        };

        var text = RunSummaryConsoleWriter.Build(summary);

        Assert.Contains($"kronikol query failures {merged}", text);
        Assert.Contains("never open Combined.json", text);
        Assert.DoesNotContain("TestRunReport.json", text);
        Assert.Contains($"kronikol query failures {merged}", RunSummaryConsoleWriter.BuildCiSummarySection(summary));
        Assert.Contains("Do not open `Combined.json` or `Combined.html`", RunSummaryConsoleWriter.BuildCiSummarySection(summary));
        Assert.Contains($"kronikol query failures {merged}", RunSummaryConsoleWriter.BuildGitHubNotice(summary)!);
    }

    [Fact]
    public void Zero_failures_is_one_line()
    {
        var lines = RunSummaryConsoleWriter.Build(Summary(0)).TrimEnd().Split('\n');

        Assert.Single(lines);
    }

    [Fact]
    public void Failures_print_the_count_the_command_and_one_line_per_failure()
    {
        var text = RunSummaryConsoleWriter.Build(Summary(3));

        Assert.Contains($"  3 failed — kronikol query failures {Dir}", text);
        Assert.Contains("    0000000000000000  Checkout › Scenario 0", text);
        Assert.Contains("    0000000000000002  Checkout › Scenario 2", text);
    }

    [Fact]
    public void Failure_lines_cap_at_twenty_and_point_at_the_digest_for_the_rest()
    {
        var text = RunSummaryConsoleWriter.Build(Summary(25));

        Assert.Equal(20, text.Split('\n').Count(l => l.Contains("Checkout › Scenario")));
        Assert.Contains("    … and 5 more (see Failures.md)", text);
    }

    [Fact]
    public void Agents_line_points_at_the_instruction_file_and_forbids_the_json()
    {
        var text = RunSummaryConsoleWriter.Build(Summary(1));

        Assert.Contains($"  agents: read {Path.Combine(Dir, "CLAUDE.md")} first; never open TestRunReport.json", text);
    }

    [Fact]
    public void Without_instruction_files_the_agents_line_names_the_help()
    {
        var text = RunSummaryConsoleWriter.Build(Summary(1, agents: false));

        Assert.DoesNotContain("CLAUDE.md", text);
        Assert.Contains("kronikol query --help", text);
    }

    [Fact]
    public void Summarise_reads_sizes_from_disk_and_carries_no_content()
    {
        var dir = Directory.CreateTempSubdirectory("kronikol-pointer").FullName;
        try
        {
            File.WriteAllText(Path.Combine(dir, "TestRunReport.json"), new string('x', 2048));
            File.WriteAllText(Path.Combine(dir, "TestRunReport.html"), "<html></html>");
            var features = new[]
            {
                new Feature
                {
                    DisplayName = "Checkout",
                    Scenarios =
                    [
                        new Scenario { Id = "t1", DisplayName = "Pay with an expired card", Result = ExecutionResult.Failed, ErrorMessage = "Refused by https://user:s3cr3t-token@payments.example/charge" },
                        new Scenario { Id = "t2", DisplayName = "Pay with a valid card", Result = ExecutionResult.Passed }
                    ]
                }
            };

            var summary = RunSummaryConsoleWriter.Summarise(features, dir, ["TestRunReport.html", "TestRunReport.json", "Failures.md"], agentInstructionsWritten: true);
            var text = RunSummaryConsoleWriter.Build(summary);

            Assert.Equal(2, summary.ScenarioCount);
            Assert.Contains("TestRunReport.json 2 KB", text);
            Assert.DoesNotContain("Failures.md", text.Split('\n')[0]); // not on disk, so not claimed
            Assert.Contains("Checkout › Pay with an expired card", text);
            Assert.Equal(ScenarioStableId.Compute(null, "Checkout", "Pay with an expired card"), summary.Failures.Single().StableId);
            Assert.DoesNotContain("s3cr3t", text);
            Assert.DoesNotContain("payments.example", text);
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public void GitHub_notice_is_one_line_with_counts_and_paths_only()
    {
        var notice = RunSummaryConsoleWriter.BuildGitHubNotice(Summary(3));

        Assert.NotNull(notice);
        Assert.StartsWith("::notice title=Kronikol::", notice);
        Assert.Contains("3 failed", notice);
        Assert.Contains($"kronikol query failures {Dir}", notice);
        Assert.DoesNotContain('\n', notice);
        Assert.Null(RunSummaryConsoleWriter.BuildGitHubNotice(Summary(0)));
    }

    [Fact]
    public void Write_emits_the_notice_only_on_github_actions()
    {
        var onGitHub = new List<string>();
        var elsewhere = new List<string>();
        var onAzure = new List<string>();

        RunSummaryConsoleWriter.Write(Summary(2), CiEnvironment.GitHubActions, onGitHub.Add);
        RunSummaryConsoleWriter.Write(Summary(2), CiEnvironment.None, elsewhere.Add);
        RunSummaryConsoleWriter.Write(Summary(2), CiEnvironment.AzureDevOps, onAzure.Add);

        Assert.Contains(onGitHub, l => l.StartsWith("::notice title=Kronikol::", StringComparison.Ordinal));
        Assert.DoesNotContain(elsewhere, l => l.StartsWith("::notice", StringComparison.Ordinal));
        Assert.DoesNotContain(onAzure, l => l.StartsWith("::notice", StringComparison.Ordinal));
        Assert.Contains(elsewhere, l => l.StartsWith("Kronikol: reports written to", StringComparison.Ordinal));
    }
}
