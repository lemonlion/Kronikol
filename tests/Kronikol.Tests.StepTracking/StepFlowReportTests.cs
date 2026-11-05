using System.Reflection;
using System.Text.RegularExpressions;
using Kronikol.Query;
using Kronikol.Reports;
using Kronikol.StepTracking;
using Kronikol.Tracking;

namespace Kronikol.Tests.StepTracking;

/// <summary>
/// What a reader is told where steps overlap (#225): a step still running reads as neither passed nor skipped, and a call that reads no
/// step is not printed under the step above it.
/// </summary>
[Collection(StepOptionsCollection.Name)]
public class StepFlowReportTests : IDisposable
{
    private const string Source = """
        using System;
        using System.Net.Http;
        using System.Threading;
        using System.Threading.Tasks;
        using Kronikol.Tracking;

        [assembly: TrackSteps]

        public class Steps
        {
            public string Id;
            private readonly TaskCompletionSource _go = new();
            private readonly TaskCompletionSource _both = new();
            private readonly TaskCompletionSource _never = new();

            private void Call(string where) => RequestResponseLogger.LogPair(Id, Id, HttpMethod.Get, new Uri("http://svc.local/" + where), "Svc", "Tests");

            public async Task LeftRunning() { await Ends(); _ = Hangs(); await Task.Yield(); }
            [Step] async Task Ends() { await Task.Yield(); Call("in-ends"); }
            [Step] async Task Hangs() => await _never.Task;

            public async Task NoStepsCall()
            {
                var a = Beside(); var b = BesideToo();
                await _both.Task;
                Task service;
                using (ExecutionContext.SuppressFlow())
                    service = Task.Run(() => RequestResponseLogger.LogPair(Id, Id, HttpMethod.Get, new Uri("http://svc.local/from-the-service"), "Svc", "Svc2"));
                await service;
                _go.SetResult();
                await Task.WhenAll(a, b);
            }
            [Step] async Task Beside() { await _go.Task; Call("in-beside"); }
            [Step] async Task BesideToo() { Call("in-beside-too"); _both.SetResult(); await _go.Task; }
        }
        """;

    private static readonly Lazy<Type> Woven = new(() =>
    {
        var path = TestAssemblyBuilder.Build("StepFlowReport", Source);
        new StepWeaver().Weave(path, Path.ChangeExtension(path, ".pdb"));
        return Assembly.LoadFrom(path).GetType("Steps")!;
    });

    private readonly string _dir = Path.Combine(Path.GetTempPath(), "kronikol-step-flow-report-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        try { Directory.Delete(_dir, true); } catch (IOException) { /* best effort */ }
    }

    private async Task<string> WriteReport(string shape, ExecutionResult result)
    {
        var id = $"step-flow-report-{Guid.NewGuid():N}";
        using (TestIdentityScope.Begin(id, id))
        {
            var steps = Activator.CreateInstance(Woven.Value)!;
            Woven.Value.GetField("Id")!.SetValue(steps, id);
            await (Task)Woven.Value.GetMethod(shape)!.Invoke(steps, null)!;
        }

        return WriteReport(id, shape, result, StepCollector.GetSteps(id));
    }

    private string WriteReport(string id, string shape, ExecutionResult result, ScenarioStep[] rows)
    {
        ReportGenerator.CreateStandardReportsWithDiagrams(
            [
                new Feature
                {
                    DisplayName = "Overlaps",
                    Scenarios =
                    [
                        new Scenario
                        {
                            Id = id, DisplayName = shape, Result = result, ErrorMessage = result == ExecutionResult.Failed ? "timed out" : null,
                            Steps = rows
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
            });
        return _dir;
    }

    private static string Query(params string[] args)
    {
        var output = new StringWriter();
        var error = new StringWriter();
        Assert.True(QueryCommand.Run(args, output, error) == 0, error.ToString());
        return output.ToString();
    }

    private static string[] Lines(string text) => text.Split('\n').Select(line => line.TrimEnd('\r')).ToArray();

    [Fact]
    public async Task Steps_marks_a_step_still_running_apart_from_one_that_passed()
    {
        var lines = Lines(Query("steps", await WriteReport("LeftRunning", ExecutionResult.Passed), "s0"));

        Assert.Contains(lines, line => line.Contains("Ends", StringComparison.Ordinal) && line.TrimStart().StartsWith('0'));
        Assert.Contains(lines, line => line.Contains("Hangs", StringComparison.Ordinal) && line.TrimStart().StartsWith("? 1", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Failures_says_a_step_was_still_running_where_none_is_marked_failed(bool json)
    {
        var reports = await WriteReport("LeftRunning", ExecutionResult.Failed);
        var failures = json ? Query("failures", reports, "--json") : Query("failures", reports);

        Assert.Contains("Hangs", failures);
        Assert.DoesNotContain("outside a tracked step", failures);
    }

    [Fact]
    public async Task The_digest_names_a_step_that_was_still_running_and_does_not_call_it_failing()
    {
        var digest = File.ReadAllText(Path.Combine(await WriteReport("LeftRunning", ExecutionResult.Failed), "Failures.md"));

        Assert.Contains("**Still running when the report was written**", digest);
        Assert.Contains("Hangs", digest);
        Assert.DoesNotContain("**Failing step**", digest);
    }

    [Fact]
    public async Task A_step_still_running_is_told_apart_from_a_failing_step_in_the_json()
    {
        var reports = await WriteReport("LeftRunning", ExecutionResult.Failed);

        Assert.Contains("\"stillRunning\":true", Query("failures", reports, "--json").Replace(" ", ""));
        Assert.Contains("\"stillRunning\":true", File.ReadAllText(Path.Combine(reports, "Failures.jsonl")).Replace(" ", ""));
    }

    // A capture need not say how its steps ended, and neither must rows built by hand: that is no hang.
    [Fact]
    public void Steps_that_carry_no_results_at_all_read_as_what_they_are()
    {
        var id = $"step-flow-report-{Guid.NewGuid():N}";
        var reports = WriteReport(id, "NoResults", ExecutionResult.Failed, [new ScenarioStep { Keyword = "Given", Text = "a customer" }, new ScenarioStep { Keyword = "When", Text = "an order" }]);

        var failures = Query("failures", reports);
        var steps = Query("steps", reports, "s0");
        var digest = File.ReadAllText(Path.Combine(reports, "Failures.md"));

        Assert.DoesNotContain("still running", failures, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("none of its steps carries a result", failures);
        Assert.DoesNotContain(Lines(steps), line => line.TrimStart().StartsWith('?'));
        Assert.DoesNotContain("Still running", digest);
        Assert.DoesNotContain("**Failing step**", digest);
    }

    [Fact]
    public async Task Flow_does_not_print_a_call_with_no_step_under_the_step_above_it()
    {
        var lines = Lines(Query("flow", await WriteReport("NoStepsCall", ExecutionResult.Passed), "s0"));

        var call = Array.FindIndex(lines, line => line.Contains("/from-the-service", StringComparison.Ordinal));
        Assert.True(call >= 0, string.Join("\n", lines));
        var header = Array.FindLastIndex(lines, call, line => line.TrimStart().StartsWith("──", StringComparison.Ordinal));
        Assert.Contains("no step", lines[header], StringComparison.Ordinal);
    }

    [Fact]
    public void The_features_counts_leave_a_step_with_no_result_out_of_skipped()
    {
        var counts = ReportGenerator.CountStepsByStatusRecursive([
            new ScenarioStep { Text = "ended", Status = ExecutionResult.Passed },
            new ScenarioStep { Text = "still running" },
            new ScenarioStep { Text = "not run", Status = ExecutionResult.Skipped }]);

        Assert.Equal((1, 0, 1), counts);
    }
}
