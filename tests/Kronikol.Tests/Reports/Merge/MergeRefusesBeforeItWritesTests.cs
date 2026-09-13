using Kronikol.ComponentDiagram;
using System.Text.Json;
using Kronikol.Reports;
using Kronikol.Tool;

namespace Kronikol.Tests.Reports.Merge;

/// <summary>
/// A refused merge changes nothing on disk.
///
/// <para>`kronikol merge` checked its destination against its inputs — and did it after writing the HTML,
/// so the check could only ever report the damage. Three things followed. The HTML destination was never
/// compared against the inputs at all, only the JSON one derived from it, so
/// <c>merge ./artifacts -o ./artifacts/runner1.json</c> overwrote a 574-byte shard with half a megabyte
/// of HTML and then printed a message saying the shard had been protected. <c>--no-json</c> skipped the
/// helper the check lived in, so that path destroyed the shard in total silence. And a merge that WAS
/// refused still returned 0, leaving a rewritten HTML beside a stale JSON with nothing saying the two
/// disagree — a CI step that tests <c>$?</c> sees success.</para>
///
/// <para>The repo promises otherwise in two places: <c>MergeCommand</c>'s own remark, and the 3.1.0
/// changelog entry. Both say the merge is never written over one of the inputs. It was true only of the
/// <c>.html</c> spelling of <c>-o</c>.</para>
/// </summary>
public class MergeRefusesBeforeItWritesTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "kronikol-merge-guard-" + Guid.NewGuid().ToString("N"));

    public MergeRefusesBeforeItWritesTests() => Directory.CreateDirectory(_dir);

    public void Dispose()
    {
        try { Directory.Delete(_dir, true); } catch { /* best effort */ }
    }

    /// <summary>
    /// The measured data loss: the shard is an input, `-o` names it, and the HTML render reaches it
    /// before any guard runs. The refusal message printed afterwards describes a protection that did not
    /// happen.
    /// </summary>
    [Theory]
    [InlineData("runner1.json")]
    [InlineData("runner1.html")]
    public void An_output_that_is_one_of_the_inputs_is_refused_before_anything_is_written(string output)
    {
        WriteShard("runner1.json", "Orders", "r1s1", "Place order");
        WriteShard("runner2.json", "Inventory", "r2s1", "Adjust stock");
        var shard = Path.Combine(_dir, "runner1.json");
        var before = File.ReadAllText(shard);

        var (exit, _, error) = Merge(Path.Combine(_dir, output));

        Assert.Equal(2, exit);
        Assert.Equal(before, File.ReadAllText(shard));
        Assert.Contains("runner1.json", error, StringComparison.Ordinal);
    }

    /// <summary>
    /// `--no-json` skipped the helper the guard lived inside, so the one path that produced no output
    /// file at all was also the one with no protection and nothing on stderr.
    /// </summary>
    [Fact]
    public void No_json_does_not_remove_the_guard()
    {
        WriteShard("runner1.json", "Orders", "r1s1", "Place order");
        var shard = Path.Combine(_dir, "runner1.json");
        var before = File.ReadAllText(shard);

        var (exit, _, error) = Merge(shard, "--no-json");

        Assert.Equal(2, exit);
        Assert.Equal(before, File.ReadAllText(shard));
        Assert.NotEqual("", error.Trim());
    }

    /// <summary>
    /// A refusal is a refusal. Returning 0 left a rewritten HTML beside a stale JSON, each describing a
    /// different run, with nothing on either saying so and a CI step reading success.
    /// </summary>
    [Fact]
    public void A_refused_merge_does_not_report_success()
    {
        WriteShard("runner1.json", "Orders", "r1s1", "Place order");

        var (exit, _, _) = Merge(Path.Combine(_dir, "runner1.html"));

        Assert.NotEqual(0, exit);
        Assert.False(File.Exists(Path.Combine(_dir, "runner1.html")),
            "a refused merge must not leave a rewritten report behind");
    }

    /// <summary>An ordinary merge is untouched by any of this.</summary>
    [Fact]
    public void A_destination_that_is_not_an_input_still_merges()
    {
        WriteShard("runner1.json", "Orders", "r1s1", "Place order");
        WriteShard("runner2.json", "Inventory", "r2s1", "Adjust stock");

        var (exit, _, error) = Merge(Path.Combine(_dir, "Combined.html"));

        Assert.True(exit == 0, error);
        Assert.True(File.Exists(Path.Combine(_dir, "Combined.html")));
        Assert.True(File.Exists(Path.Combine(_dir, "Combined.json")));
    }

    private (int Exit, string Output, string Error) Merge(string output, params string[] extra)
    {
        var outWriter = new StringWriter();
        var errWriter = new StringWriter();
        var exit = Commands.Dispatch(["merge", _dir, "-o", output, .. extra], outWriter, errWriter);
        return (exit, outWriter.ToString(), errWriter.ToString());
    }

    private void WriteShard(string name, string feature, string scenarioId, string scenarioName)
    {
        var json = ReportGenerator.GenerateMergeableReportJson(
            [
                new Feature
                {
                    DisplayName = feature,
                    Scenarios = [new Scenario { Id = scenarioId, DisplayName = scenarioName, Result = ExecutionResult.Passed }]
                }
            ],
            new DateTime(2026, 1, 1, 10, 0, 0, DateTimeKind.Utc),
            new DateTime(2026, 1, 1, 10, 1, 0, DateTimeKind.Utc),
            diagramLookup: null,
            [new ComponentRelationship("Test", feature + "Api", "HTTP", new HashSet<string> { "GET /" }, 1, 1, "http")],
            internalFlowSegmentData: null, wholeTestFlow: null,
            WholeTestFlowVisualization.None, ciMetadata: null);

        File.WriteAllText(Path.Combine(_dir, name), json);
    }
}
