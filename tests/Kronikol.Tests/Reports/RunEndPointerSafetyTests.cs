using Kronikol.Reports;

namespace Kronikol.Tests.Reports;

/// <summary>
/// The pointer is a one-line-per-thing channel, and three of the four things it prints come from the run
/// rather than from Kronikol: the reports directory, and every failing feature and scenario name. The
/// digest's own documentation leans on the pointer carrying "no captured content at all" — true of
/// payloads, never true of names, which producers take from feature files, theory arguments and
/// parameterised titles.
///
/// <para>A line ending is the whole problem. <see cref="RunSummaryConsoleWriter.Write"/> splits the built
/// text on newlines and writes one line per call, so a newline inside a name becomes an extra line the
/// writer never composed — and on GitHub Actions a line the run controls, starting at column zero, is a
/// workflow command. A scenario called <c>"a\n::error::gone"</c> does not produce a pointer with an odd
/// name in it; it produces a failing annotation attributed to Kronikol.</para>
///
/// <para>The other half is the copy-paste contract. Every one of these lines ends in a command the reader
/// is meant to run, and an unquoted path with a space in it is a command that fails — which is the whole
/// value of printing it.</para>
/// </summary>
public class RunEndPointerSafetyTests
{
    private static RunSummary Summary(string directory, string feature, string scenario) => new(
        directory,
        [new RunSummaryFile("TestRunReport.html", 1_200_000), new RunSummaryFile("Failures.md", 12_000)],
        ScenarioCount: 40,
        Failures: [new RunSummaryFailure("00000000000000ff", feature, scenario)],
        AgentInstructionsWritten: true);

    [Theory]
    [InlineData("a\n::error::forged")]
    [InlineData("a\r\n::error::forged")]
    [InlineData("a\r::error::forged")]
    [InlineData("a\u2028::error::forged")]
    public void Pointer_collapses_CR_LF_in_every_run_derived_string(string hostile)
    {
        var text = RunSummaryConsoleWriter.Build(Summary("/proj/Reports", hostile, hostile));

        // Every line the pointer emits is one it composed. Counting them is the assertion: the shape is
        // fixed — directory, "1 failed", the failure, the agents line — so an extra line is an injected one.
        var lines = text.TrimEnd('\n').Split('\n');
        Assert.Equal(4, lines.Length);
        Assert.DoesNotContain(lines, l => l.StartsWith("::", StringComparison.Ordinal));

        // A bare CR never splits the string, so counting lines cannot see it — and on a terminal it
        // returns the cursor to column zero and the rest of the name overwrites the line it was in.
        // U+2028 and U+2029 do the same in most editors and in anything reading the file back.
        Assert.DoesNotContain(lines, l => l.Any(c => c is '\r' or '\u000c' or '\u000b' or '\u0085' or '\u2028' or '\u2029'));

        // ...and the name is still reported, rather than dropped to make the count work.
        Assert.Contains(lines, l => l.Contains("::error::forged", StringComparison.Ordinal));
    }

    [Fact]
    public void Pointer_collapses_CR_LF_in_the_github_notice_too()
    {
        // One line, by contract — it is a workflow command, and a second line inside it is a second
        // command. The directory is the run-derived string that reaches it.
        var notice = RunSummaryConsoleWriter.BuildGitHubNotice(Summary("/proj\n::error::forged", "Checkout", "Pay"));

        Assert.NotNull(notice);
        Assert.DoesNotContain('\n', notice);
        Assert.DoesNotContain('\r', notice);
    }

    [Theory]
    [InlineData("/home/ci/My Reports")]
    [InlineData(@"C:\Program Files\proj\Reports")]
    public void Pointer_quotes_a_path_containing_a_space(string directory)
    {
        var text = RunSummaryConsoleWriter.Build(Summary(directory, "Checkout", "Pay"));

        // The command line is the point of the line. Unquoted, `kronikol query failures /home/ci/My
        // Reports` passes two arguments and the tool reads the first, which is not a report directory.
        Assert.Contains($"kronikol query failures \"{directory}\"", text, StringComparison.Ordinal);
    }

    [Fact]
    public void Pointer_leaves_a_path_without_a_space_unquoted()
    {
        // Non-vacuity, and the common case: quoting everything would make every pointer noisier to read
        // for the one path in a hundred that needs it.
        var text = RunSummaryConsoleWriter.Build(Summary("/proj/Reports", "Checkout", "Pay"));

        Assert.Contains("kronikol query failures /proj/Reports\n", text, StringComparison.Ordinal);
    }

    [Fact]
    public void Ci_summary_quotes_a_path_containing_a_space()
    {
        // Same contract, and this one survives runners that swallow stdout — so it is the copy-paste
        // command that actually gets used.
        var markdown = RunSummaryConsoleWriter.BuildCiSummarySection(Summary("/home/ci/My Reports", "Checkout", "Pay"));

        Assert.Contains("kronikol query summary \"/home/ci/My Reports\"", markdown, StringComparison.Ordinal);
        Assert.Contains("kronikol query failures \"/home/ci/My Reports\"", markdown, StringComparison.Ordinal);
    }
}

/// <summary>
/// The pointer is the only place a run says where it put things, so a name in it is a promise the file is
/// there. <see cref="RunSummaryConsoleWriter.Summarise"/> is handed the outputs the configuration ASKED
/// for; the ones that reached disk are a different set, because <c>RunOutputs</c> isolates each output and
/// a throwing one is recorded as a diagnostic while the run carries on.
/// </summary>
public class RunOutputNamingTests
{
    [Fact]
    public void Pointer_names_only_outputs_that_reached_disk()
    {
        var directory = Path.Combine(Path.GetTempPath(), "kronikol-pointer-" + Guid.NewGuid().ToString("n"));
        Directory.CreateDirectory(directory);
        try
        {
            File.WriteAllText(Path.Combine(directory, "TestRunReport.html"), "<html></html>");
            // TestRunReport.json is asked for and never written — the shape of an output whose action threw.

            var summary = RunSummaryConsoleWriter.Summarise(
                [], directory, ["TestRunReport.html", "TestRunReport.json", "Failures.md"],
                agentInstructionsWritten: false);

            Assert.Equal(["TestRunReport.html"], summary.Files.Select(f => f.Name));
            Assert.DoesNotContain("TestRunReport.json", RunSummaryConsoleWriter.Build(summary), StringComparison.Ordinal);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }
}
