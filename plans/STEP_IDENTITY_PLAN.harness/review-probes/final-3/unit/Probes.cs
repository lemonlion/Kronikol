using Kronikol.Reports;
using Kronikol.ReqNRoll;
using Kronikol.Tests.RealEngines;
using Kronikol.Tracking;
using Reqnroll;

namespace ReviewProbes;

[Collection("Probes")]
public class Probes(ITestOutputHelper output)
{
    private static ScenarioStep[]? Rows(string id, params ReqNRollStepInfo[] steps) =>
        new[]
        {
            new ReqNRollScenarioInfo
            {
                ScenarioId = id,
                ScenarioTitle = "Scenario",
                FeatureTitle = "Feature",
                ScenarioTags = [],
                CombinedTags = [],
                Steps = [..steps],
                ExecutionStatus = ScenarioExecutionStatus.OK,
            }
        }.ToFeatures()[0].Scenarios[0].Steps;

    private static string Test() => "probe-" + Guid.NewGuid();

    private static string Show(IEnumerable<ScenarioStep>? rows, int depth = 0) =>
        string.Join("", (rows ?? []).Select(r => new string(' ', depth * 2) + (r.Keyword ?? "") + " " + r.Text + " id=" + r.CollectedId
            + (r.Attachments is { Length: > 0 } a ? " files=" + string.Join(",", a.Select(f => f.Name)) : "") + "\n" + Show(r.SubSteps, depth + 1)));

    // Fix 2: two hand-built rows that read alike, nothing nested at all.
    [Fact]
    public void U1_two_hand_built_rows_that_read_alike()
    {
        var test = Test();
        foreach (var (keyword, text, under) in new[] { ("Given", "the page refreshes", "first refresh"), ("Given", "the page refreshes", "second refresh"), ("Then", "it shows", "shown") })
        {
            var frame = StepCollector.StartFrame(test, keyword, text);
            StepCollector.AddAssertionSubStep(test, under, passed: true);
            StepCollector.CompleteFrame(test, frame, passed: true);
        }

        var rows = Rows(test,
            new ReqNRollStepInfo("Given", "the page refreshes"),
            new ReqNRollStepInfo("Given", "the page refreshes"),
            new ReqNRollStepInfo("Then", "it shows"))!;

        output.WriteLine(Show(rows));
        Assert.Equal(3, rows.Length);
        Assert.Equal("second refresh", rows[1].SubSteps![0].Text);
        Assert.Equal("shown", rows[2].SubSteps![0].Text);
    }

    // Fix 2: the same, with one row of the list a real one so the list still has a frame to read.
    [Fact]
    public void U1b_a_real_row_and_two_masked_rows_that_read_alike()
    {
        var test = Test();
        var frames = new List<int?>();
        foreach (var (keyword, text) in new[] { ("Given", "a basket"), ("When", "secret one"), ("When", "secret two"), ("Then", "done") })
        {
            var frame = StepCollector.StartFrame(test, keyword, text);
            Files(test, text);
            StepCollector.CompleteFrame(test, frame, passed: true);
            frames.Add(frame);
        }

        var rows = Rows(test,
            new ReqNRollStepInfo("Given", "a basket") { FrameId = frames[0] },
            new ReqNRollStepInfo("When", "***", ScenarioExecutionStatus.OK),
            new ReqNRollStepInfo("When", "***", ScenarioExecutionStatus.OK),
            new ReqNRollStepInfo("Then", "done") { FrameId = frames[3] })!;

        output.WriteLine(Show(rows));
        Assert.Equal(["a basket", "***", "***", "done"], rows.Select(r => r.Text));
        Assert.Equal("done.txt", rows[3].Attachments![0].Name);
    }

    private static void Files(string test, string name) => StepCollector.AddAttachment(test, name + ".txt", name + ".txt");

    // Fix 4.
    private static string[] Paths(ScenarioStep[] steps, params RequestResponseLog[] logs)
    {
        var features = new[] { new Feature { DisplayName = "F", Scenarios = [new Scenario { Id = logs[0].TestId, DisplayName = "S", Steps = steps }] } };
        return ReportGenerator.AttributeInteractionsToStepPaths(logs, features)[logs[0].TestId].Select(p => p ?? "-").ToArray();
    }

    private static RequestResponseLog Log(string test, RequestResponseType type, Guid id, int? open) =>
        new RequestResponseLog("Test", test, HttpMethod.Get, null, new Uri("http://a/x"), [], "S", "C", type, Guid.NewGuid(), id, false) { OpenStep = open };

    [Fact]
    public void U4a_a_stamped_response_whose_request_has_none_reads_the_step_before_it_and_moves_the_walk()
    {
        var id = Guid.NewGuid();
        var result = Paths(
            [new ScenarioStep { CollectedId = 1, Text = "a" }, new ScenarioStep { CollectedId = 2, Text = "b" }],
            Log("t4a", RequestResponseType.Request, Guid.NewGuid(), 1),
            Log("t4a", RequestResponseType.Request, id, null),
            Log("t4a", RequestResponseType.Response, id, 2),
            Log("t4a", RequestResponseType.Request, Guid.NewGuid(), null));
        output.WriteLine(string.Join(" ", result));
        Assert.Equal(["0", "0", "0", "1"], result);
    }

    // Rows by kind: ids not ascending down the rows, as a BDDfy list reads.
    [Fact]
    public void U4b_rows_in_another_order_than_their_ids()
    {
        var result = Paths(
            [new ScenarioStep { CollectedId = 1, Text = "w1" }, new ScenarioStep { CollectedId = 3, Text = "w2" }, new ScenarioStep { CollectedId = 2, Text = "t1" }, new ScenarioStep { CollectedId = 4, Text = "t2" }],
            Log("t4b", RequestResponseType.Request, Guid.NewGuid(), 1),
            Log("t4b", RequestResponseType.Request, Guid.NewGuid(), 2),
            Log("t4b", RequestResponseType.Request, Guid.NewGuid(), null),
            Log("t4b", RequestResponseType.Request, Guid.NewGuid(), 3),
            Log("t4b", RequestResponseType.Request, Guid.NewGuid(), null),
            Log("t4b", RequestResponseType.Request, Guid.NewGuid(), 4));
        output.WriteLine(string.Join(" ", result));
        Assert.Equal(["0", "2", "2", "1", "1", "3"], result);
    }

    // A parent step's call after its sub-step, then a call with nothing open: the parent is the step before it, and both read its row.
    [Fact]
    public void U4c_a_parents_call_after_its_sub_step_then_one_with_nothing_open()
    {
        var result = Paths(
            [new ScenarioStep { CollectedId = 1, Text = "a", SubSteps = [new ScenarioStep { CollectedId = 2, Text = "under a" }] }, new ScenarioStep { CollectedId = 3, Text = "b" }],
            Log("t4c", RequestResponseType.Request, Guid.NewGuid(), 2),
            Log("t4c", RequestResponseType.Request, Guid.NewGuid(), 1),
            Log("t4c", RequestResponseType.Request, Guid.NewGuid(), null),
            Log("t4c", RequestResponseType.Request, Guid.NewGuid(), 3));
        output.WriteLine(string.Join(" ", result));
        Assert.Equal(["0", "0", "0", "1"], result);
    }

    // Fix 3 and fix 4: a story run in a step of another, the outer step's own call after the inner ones, then one with nothing open.
    [Fact]
    public void U4d_a_held_call_of_a_frame_no_row_names_then_one_with_nothing_open()
    {
        var result = Paths(
            [new ScenarioStep { CollectedId = 2, Text = "inner one" }, new ScenarioStep { CollectedId = 3, Text = "inner two" }],
            Log("t4d", RequestResponseType.Request, Guid.NewGuid(), 2),
            Log("t4d", RequestResponseType.Request, Guid.NewGuid(), 3),
            Log("t4d", RequestResponseType.Request, Guid.NewGuid(), 1),
            Log("t4d", RequestResponseType.Request, Guid.NewGuid(), null));
        output.WriteLine(string.Join(" ", result));
    }

    // Reads the report of one fixture run, through ReportRows. Set REVIEW_REPORTS and REVIEW_OUT.
    [Fact]
    public void Dump()
    {
        var directory = Environment.GetEnvironmentVariable("REVIEW_REPORTS");
        var outFile = Environment.GetEnvironmentVariable("REVIEW_OUT");
        if (directory is null || outFile is null)
            return;

        var rows = ReportRows.Read(ReportRows.DataFile(directory)!);
        var text = new System.Text.StringBuilder();
        foreach (var name in rows.ScenarioNames.Where(n => n.StartsWith("RV", StringComparison.Ordinal) || n.StartsWith("OR2", StringComparison.Ordinal) || n.StartsWith("HS3", StringComparison.Ordinal) || n.StartsWith("AN4", StringComparison.Ordinal)).OrderBy(n => n, StringComparer.Ordinal))
        {
            text.AppendLine("=== " + name);
            text.AppendLine(rows.Of(name));
            text.AppendLine("all files: " + string.Join(", ", rows.AllFilesOf(name)));
            text.AppendLine("scenario files: " + string.Join(", ", rows.ScenarioFilesOf(name)));
        }
        File.WriteAllText(outFile, text.ToString());
    }
}
