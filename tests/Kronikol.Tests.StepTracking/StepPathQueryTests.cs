using System.Reflection;
using System.Text.RegularExpressions;
using Kronikol.Query;
using Kronikol.Reports;
using Kronikol.StepTracking;
using Kronikol.Tracking;

namespace Kronikol.Tests.StepTracking;

/// <summary>
/// A call names the deepest step it was made in (#225), and every reader of the path answers for it: woven, run, written to a
/// report and read back through <c>kronikol query</c> and the digest, so the address a stamp gets cannot drift from the one a
/// reader walks.
/// </summary>
[Collection(StepOptionsCollection.Name)]
public class StepPathQueryTests : IDisposable
{
    private const string Source = """
        using System;
        using System.Net.Http;
        using System.Threading.Tasks;
        using Kronikol.Tracking;

        [assembly: TrackSteps]

        public class Steps
        {
            public string Id;

            private void Call(string where) =>
                RequestResponseLogger.LogPair(Id, Id, HttpMethod.Get, new Uri("http://svc.local/" + where), "Svc", "Tests");

            public async Task Run()
            {
                await Basket();
                try { await Payment(); } catch (InvalidOperationException) { }
                try { await Refund(); } catch (InvalidOperationException) { }
                await Caught();
            }

            // Every step passes
            [Step] async Task Basket() { Call("in-basket"); await Item(); await Size(); }
            [Step] async Task Item() { await Task.Yield(); Call("in-item"); }
            [Step] async Task Size() { await Task.Yield(); Call("in-size"); }

            // A sub-step fails, and so its parent does
            [Step] async Task Payment() { Call("in-payment"); await Card(); await Charge(); }
            [Step] async Task Card() { await Task.Yield(); Call("in-card"); }
            [Step] async Task Charge() { await Task.Yield(); Call("in-charge"); throw new InvalidOperationException("declined"); }

            // A parent fails by itself, with every sub-step passed
            [Step] async Task Refund() { await First(); await Second(); throw new InvalidOperationException("refused"); }
            [Step] async Task First() { await Task.Yield(); Call("in-first"); }
            [Step] async Task Second() { await Task.Yield(); Call("in-second"); }

            // A sub-step fails and its parent passes
            [Step] async Task Caught() { Call("in-caught"); try { await Failing(); } catch (InvalidOperationException) { } await Sibling(); }
            [Step] async Task Failing() { await Task.Yield(); Call("in-failing"); throw new InvalidOperationException("caught"); }
            [Step] async Task Sibling() { await Task.Yield(); Call("in-sibling"); }
        }
        """;

    private static readonly Lazy<Type> Woven = new(() =>
    {
        var path = TestAssemblyBuilder.Build("StepPathQuery", Source);
        new StepWeaver().Weave(path, Path.ChangeExtension(path, ".pdb"));
        return Assembly.LoadFrom(path).GetType("Steps")!;
    });

    private readonly string _dir = Path.Combine(Path.GetTempPath(), "kronikol-step-paths-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        try { Directory.Delete(_dir, true); } catch (IOException) { /* best effort */ }
    }

    private async Task<string> WriteReport(DataFormat format = DataFormat.Json, bool compress = true)
    {
        var id = $"step-paths-{Guid.NewGuid():N}";
        using (TestIdentityScope.Begin(id, id))
        {
            var steps = Activator.CreateInstance(Woven.Value)!;
            Woven.Value.GetField("Id")!.SetValue(steps, id);
            await (Task)Woven.Value.GetMethod("Run")!.Invoke(steps, null)!;
        }

        return WriteReport(id, format, compress);
    }

    private string WriteReport(string id, DataFormat format, bool compress)
    {
        ReportGenerator.CreateStandardReportsWithDiagrams(
            [
                new Feature
                {
                    DisplayName = "Checkout",
                    Scenarios =
                    [
                        new Scenario
                        {
                            Id = id, DisplayName = "Pay", Result = ExecutionResult.Failed, ErrorMessage = "declined",
                            Steps = StepCollector.GetSteps(id)
                        }
                    ]
                }
            ],
            DateTime.UtcNow.AddMinutes(-1), DateTime.UtcNow,
            new ReportConfigurationOptions
            {
                ReportsFolderPath = _dir,
                InternalFlowTracking = false,
                GenerateComponentDiagram = false,
                GenerateSpecificationsReport = false,
                GenerateSpecificationsData = false,
                TestRunReportDataFormat = format,
                CompressTestRunReport = compress,
            });
        return _dir;
    }

    // A step holds its sub-steps inside it, two JSON levels each, and a JSON reader stops at 64 unless told otherwise: 28 steps deep.
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void A_step_tree_deeper_than_a_json_readers_default_reaches_the_report_and_its_readers(bool compress)
    {
        const int levels = 60;
        var id = $"step-paths-deep-{Guid.NewGuid():N}";
        using (TestIdentityScope.Begin(id, id))
        {
            for (var level = 0; level < levels; level++)
                StepCollector.StartStep(id, null, $"level {level}", null, null);
            RequestResponseLogger.LogPair("deepest", id, HttpMethod.Get, new Uri("http://svc.local/deepest"), "svc", "Tests");
            for (var level = 0; level < levels; level++)
                StepCollector.CompleteStep(id, true);
        }

        var reports = WriteReport(id, DataFormat.Json, compress);

        Assert.Contains($"Level {levels - 1}", Query("steps", reports, "s0", "--max-bytes", "100000"));
        Assert.Contains("/deepest", Query("flow", reports, "s0/" + string.Join('.', Enumerable.Repeat('0', levels))));
        Assert.Contains("Pay", Query("summary", reports));
        Query("sizes", reports);
    }

    // The walks over a tree on the way to a report are recursive, and a stack overflow takes the test process with it, report and all.
    [Fact]
    public async Task A_step_tree_too_deep_to_walk_is_cut_and_says_so_and_the_report_is_written()
    {
        const int levels = 500;
        var id = $"step-paths-too-deep-{Guid.NewGuid():N}";
        using (TestIdentityScope.Begin(id, id))
        {
            for (var level = 0; level < levels; level++)
                StepCollector.StartStep(id, null, $"level {level}", null, null);
            for (var level = 0; level < levels; level++)
                StepCollector.CompleteStep(id, true);
        }

        var reports = await Task.Run(() => WriteReport(id, DataFormat.Json, compress: true));
        var steps = Query("steps", reports, "s0", "--max-bytes", "2000000");

        Assert.Contains("Level 199", steps);
        Assert.DoesNotContain("Level 200", steps);
        Assert.Contains("steps below this one are not listed", StepCollector.GetSteps(id).Single().Flatten().Last().Comments!.Single());
    }

    private static string Query(params string[] args)
    {
        var output = new StringWriter();
        var error = new StringWriter();
        Assert.True(QueryCommand.Run(args, output, error) == 0, error.ToString());
        return output.ToString();
    }

    private static string[] Lines(string text) => text.Split('\n').Select(line => line.TrimEnd('\r')).ToArray();

    private static string RowOf(string steps, string path) =>
        Lines(steps).First(line => Regex.IsMatch(line, $@"^\s*[✗~· ]\s{Regex.Escape(path)}\s"));

    private static int Mentions(string text, string call) => Regex.Matches(text, $@"/{Regex.Escape(call)}\b").Count;

    [Fact]
    public async Task Steps_a_top_level_row_keeps_its_count_and_a_sub_step_row_shows_its_own()
    {
        var steps = Query("steps", await WriteReport(), "s0");

        Assert.Contains("3 calls", RowOf(steps, "0"));
        Assert.Matches(@"\[i\d+\]", RowOf(steps, "0.0"));
        Assert.Matches(@"\[i\d+\]", RowOf(steps, "0.1"));
        Assert.Contains("3 calls", RowOf(steps, "1"));
        Assert.Contains("2 calls", RowOf(steps, "2"));
    }

    [Fact]
    public async Task A_step_address_returns_the_calls_of_that_sub_step_and_no_others()
    {
        var reports = await WriteReport();

        var flow = Query("flow", reports, "s0/1.1");
        Assert.Equal(1, Mentions(flow, "in-charge"));
        Assert.Equal(0, Mentions(flow, "in-card"));
        Assert.Equal(0, Mentions(flow, "in-payment"));

        var interactions = Query("interactions", reports, "s0", "--step", "0.1");
        Assert.Equal(1, Mentions(interactions, "in-size"));
        Assert.Equal(0, Mentions(interactions, "in-item"));
    }

    [Fact]
    public async Task A_parents_address_answers_for_its_sub_steps()
    {
        var flow = Query("flow", await WriteReport(), "s0/0");

        Assert.Equal(1, Mentions(flow, "in-basket"));
        Assert.Equal(1, Mentions(flow, "in-item"));
        Assert.Equal(1, Mentions(flow, "in-size"));
        Assert.Equal(0, Mentions(flow, "in-card"));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Failures_keeps_every_call_of_a_failing_parent_and_lists_no_call_twice(bool json)
    {
        var reports = await WriteReport();
        var failures = json ? Query("failures", reports, "--json") : Query("failures", reports);

        foreach (var call in new[] { "in-payment", "in-card", "in-charge", "in-first", "in-second", "in-failing" })
            Assert.Equal(1, Mentions(failures, call));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Failures_drops_the_passed_siblings_of_a_failing_sub_step_under_a_passing_parent(bool json)
    {
        var reports = await WriteReport();
        var failures = json ? Query("failures", reports, "--json") : Query("failures", reports);

        Assert.Equal(1, Mentions(failures, "in-failing"));
        Assert.Equal(0, Mentions(failures, "in-sibling"));
        Assert.Equal(0, Mentions(failures, "in-basket"));
    }

    [Fact]
    public async Task The_digest_reads_a_failing_chain_the_same_way()
    {
        var digest = File.ReadAllText(Path.Combine(await WriteReport(), "Failures.md"));

        Assert.True(Mentions(digest, "in-charge") >= 1, digest);
        Assert.True(Mentions(digest, "in-first") >= 1, digest);
        Assert.True(Mentions(digest, "in-second") >= 1, digest);
        Assert.True(Mentions(digest, "in-failing") >= 1, digest);
        // What a passing parent did itself is the most that can be said of its failing sub-step's start, as it always was.
        Assert.True(Mentions(digest, "in-caught") >= 1, digest);
        Assert.Equal(0, Mentions(digest, "in-sibling"));
        Assert.Equal(0, Mentions(digest, "in-basket"));
    }

    [Fact]
    public async Task Every_path_a_call_reads_is_a_step_of_its_scenario()
    {
        var reports = await WriteReport();
        var steps = Query("steps", reports, "s0");
        var interactions = Query("interactions", reports, "s0", "--json");

        var read = Regex.Matches(interactions, @"""stepPath""\s*:\s*""([^""]+)""").Select(match => match.Groups[1].Value).Distinct().ToArray();
        Assert.NotEmpty(read);
        Assert.Contains("1.1", read);
        foreach (var path in read)
            Assert.Contains(path + " ", RowOf(steps, path));
    }

    [Theory]
    [InlineData(DataFormat.Xml, "TestRunReport.xml")]
    [InlineData(DataFormat.Yaml, "TestRunReport.yml")]
    public async Task Xml_and_yaml_carry_the_deep_path(DataFormat format, string file)
    {
        var text = File.ReadAllText(Path.Combine(await WriteReport(format, compress: false), file));

        Assert.Contains("1.1", text);
    }
}

internal static class StepTreeExtensions
{
    public static IEnumerable<ScenarioStep> Flatten(this ScenarioStep step)
    {
        var current = step;
        while (current is not null)
        {
            yield return current;
            current = current.SubSteps?.FirstOrDefault();
        }
    }
}
