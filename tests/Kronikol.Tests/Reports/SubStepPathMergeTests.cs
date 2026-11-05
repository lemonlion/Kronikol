using System.Text.RegularExpressions;
using Kronikol.Query;
using Kronikol.Reports;
using Kronikol.Tool;
using Kronikol.Tracking;

namespace Kronikol.Tests.Reports;

/// <summary>
/// A merge keeps the sub-step a call was made in (#225), and reads a shard written before a call could name one beside a
/// shard written after: each scenario's calls read as their own shard wrote them.
/// </summary>
[Collection("DiagramsFetcher")]
public class SubStepPathMergeTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "kronikol-substep-merge-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        DefaultDiagramsFetcher.Reset();
        try { Directory.Delete(_root, true); } catch (IOException) { /* best effort */ }
    }

    private string Shard(string name)
    {
        var testId = $"{name}-{Guid.NewGuid():N}";
        StepCollector.StartStep(testId, "When", "paying", null, null);
        StepCollector.StartStep(testId, null, "charging", null, null);
        RequestResponseLogger.LogPair(name, testId, HttpMethod.Post, new Uri($"http://payments/{name}"), "payments", "Test");
        StepCollector.CompleteStep(testId, true);
        StepCollector.CompleteStep(testId, true);

        return WriteShard(name, testId);
    }

    private string WriteShard(string name, string testId, bool compress = false)
    {
        var folder = Path.Combine(_root, "shards", name);
        ReportGenerator.CreateStandardReportsWithDiagrams(
            [
                new Feature
                {
                    DisplayName = "Checkout " + name,
                    Scenarios = [new Scenario { Id = testId, DisplayName = "Pay " + name, Result = ExecutionResult.Passed, Steps = StepCollector.GetSteps(testId) }]
                }
            ],
            DateTime.UtcNow.AddMinutes(-1), DateTime.UtcNow,
            new ReportConfigurationOptions
            {
                ReportsFolderPath = folder,
                InternalFlowTracking = false,
                GenerateComponentDiagram = false,
                GenerateSpecificationsReport = false,
                GenerateSpecificationsData = false,
                GenerateMergeableData = true,
                CompressTestRunReport = compress,
            });
        return Path.Combine(folder, compress ? "TestRunReport.krkl" : "TestRunReport.json");
    }

    [Fact]
    public void A_step_tree_deeper_than_a_json_readers_default_merges()
    {
        const int levels = 60;
        var testId = $"deep-{Guid.NewGuid():N}";
        for (var level = 0; level < levels; level++)
            StepCollector.StartStep(testId, null, $"level {level}", null, null);
        RequestResponseLogger.LogPair("deepest", testId, HttpMethod.Get, new Uri("http://svc.local/deepest"), "svc", "Tests");
        for (var level = 0; level < levels; level++)
            StepCollector.CompleteStep(testId, true);
        WriteShard("deep", testId);
        Shard("level");

        var merged = Merged();

        Assert.Contains("Checkout deep", Query("steps", merged, "s0"));
        Assert.Contains("/deepest", Query("flow", merged, "s0/" + string.Join('.', Enumerable.Repeat('0', levels))));
    }

    [Fact]
    public void A_step_tree_deeper_than_a_json_readers_default_decodes()
    {
        var testId = $"deep-decode-{Guid.NewGuid():N}";
        for (var level = 0; level < 60; level++)
            StepCollector.StartStep(testId, null, $"level {level}", null, null);
        for (var level = 0; level < 60; level++)
            StepCollector.CompleteStep(testId, true);
        var file = WriteShard("deep", testId, compress: true);

        var output = new StringWriter();
        var error = new StringWriter();
        var exit = DecodeCommand.Run([file], output, error);

        Assert.True(exit == 0, $"exit {exit}: {error}");
        Assert.Contains("level 59", output.ToString(), StringComparison.OrdinalIgnoreCase);
    }

    private string Merged()
    {
        var output = new StringWriter();
        var error = new StringWriter();
        var merged = Path.Combine(_root, "merged");
        var exit = MergeCommand.Run([Path.Combine(_root, "shards"), "-o", Path.Combine(merged, "TestRunReport.html")], output, error, _ => null);
        Assert.True(exit == 0, $"exit {exit}: {error}");
        return merged;
    }

    private static string Query(params string[] args)
    {
        var output = new StringWriter();
        var error = new StringWriter();
        Assert.True(QueryCommand.Run(args, output, error) == 0, error.ToString());
        return output.ToString();
    }

    [Fact]
    public void A_merge_keeps_the_sub_step_a_call_was_made_in()
    {
        Shard("newer");

        Assert.Equal("0.0", DeepestRowWithACall(Merged(), "newer"));
    }

    [Fact]
    public void A_merge_reads_a_shard_from_before_sub_steps_beside_one_from_after()
    {
        Shard("newer");
        var older = Shard("older");
        // What a tool before this release wrote for the same call: the top-level step.
        var text = File.ReadAllText(older);
        var rewritten = Regex.Replace(text, @"""stepPath""\s*:\s*""0\.0""", @"""stepPath"":""0""");
        Assert.NotEqual(text, rewritten);
        File.WriteAllText(older, rewritten);

        var merged = Merged();

        Assert.Equal("0.0", DeepestRowWithACall(merged, "newer"));
        Assert.Equal("0", DeepestRowWithACall(merged, "older"));
    }

    // Which row counts the scenario's one call last: a parent's row counts what is under it, so the deepest row that shows it is the call's own.
    private static string DeepestRowWithACall(string reports, string scenarioName)
    {
        var address = Regex.Match(Query("scenarios", reports), $@"\b(s\d+)\b[^\n]*Pay {scenarioName}").Groups[1].Value;
        var rows = Regex.Matches(Query("steps", reports, address), @"^\s*[✗~· ]\s(\S+)\s.*\[i\d+\]\s*$", RegexOptions.Multiline);
        return rows[^1].Groups[1].Value;
    }
}
