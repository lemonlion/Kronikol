using Kronikol.Tool;

namespace Kronikol.Tests.Templates;

/// <summary>
/// The history action's optional <c>gate</c> phase (plans/HISTORY_ACTION_PLAN.md §4.6): after the tests, each report
/// read against the ledger <c>read</c> fetched, failing on what the ledger says is new. Without a ledger there is
/// nothing to read a run against, so it fails only when the tests did (F11). Every report is gated before the step
/// fails, and an unreadable report is an error, never a pass.
/// </summary>
public class HistoryActionGateTests
{
    /// <summary>A ledger of five green runs of suite A, named in the job's <c>KRONIKOL_HISTORY</c> as <c>read</c> names it.</summary>
    private static string Ledger(RunnerJob job, int scenarios = 2)
    {
        var path = Path.Combine(job.Temp, "kronikol-history", "history.jsonl");
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        HistoryFixtures.Ledger(path, "A", Enumerable.Repeat(new string('P', scenarios), 5).ToArray());
        job.Env["KRONIKOL_HISTORY"] = path;
        return path;
    }

    private static string Report(RunnerJob job, string directory, string results, string suite = "A") =>
        HistoryFixtures.Report(Path.Combine(job.Workspace, directory), suite, 6, results);

    [Fact]
    public void Without_a_ledger_the_gate_passes_a_green_run_and_fails_a_red_one_saying_why()
    {
        HistoryWorld.SkipWithoutBashOrGit();
        using var world = new HistoryWorld();
        var green = world.Job(6);
        Report(green, "Reports", "PP");
        var red = world.Job(6);
        Report(red, "Reports", "PF");

        var passed = HistoryWorld.Gate(green, new Dictionary<string, string> { ["reports"] = "Reports", ["test-outcome"] = "success" });
        var failed = HistoryWorld.Gate(red, new Dictionary<string, string> { ["reports"] = "Reports", ["test-outcome"] = "failure" });

        Assert.True(passed.Succeeded, passed.ToString());
        Assert.Equal("no-ledger", passed.Outputs["result"]);
        Assert.False(failed.Succeeded);
        Assert.Equal("no-ledger", failed.Outputs["result"]);
        Assert.Contains(failed.Errors, e => e.Contains("no ledger", StringComparison.Ordinal) && e.Contains("failed", StringComparison.Ordinal));
    }

    [Fact]
    public void A_new_failure_fails_the_gate_and_its_reading_is_in_the_summary()
    {
        HistoryWorld.SkipWithoutBashOrGit();
        using var world = new HistoryWorld();
        var job = world.Job(6);
        Ledger(job);
        Report(job, "Reports", "PF");

        var gate = HistoryWorld.Gate(job, new Dictionary<string, string> { ["reports"] = "Reports", ["test-outcome"] = "failure" });

        Assert.False(gate.Succeeded);
        Assert.Equal("failed", gate.Outputs["result"]);
        var summary = job.Summary.ToString();
        Assert.Contains("Reports", summary, StringComparison.Ordinal);
        Assert.Contains("new-failures: 1", summary, StringComparison.Ordinal);
    }

    [Fact]
    public void A_green_run_passes_the_gate()
    {
        HistoryWorld.SkipWithoutBashOrGit();
        using var world = new HistoryWorld();
        var job = world.Job(6);
        Ledger(job);
        Report(job, "Reports", "PP");

        var gate = HistoryWorld.Gate(job, new Dictionary<string, string> { ["reports"] = "Reports", ["test-outcome"] = "success" });

        Assert.True(gate.Succeeded, gate.ToString());
        Assert.Equal("passed", gate.Outputs["result"]);
    }

    [Fact]
    public void A_quarantined_failure_passes_because_the_companion_came_with_the_ledger()
    {
        HistoryWorld.SkipWithoutBashOrGit();
        using var world = new HistoryWorld();
        // The data branch holds five green runs and a quarantine for the second scenario, as a team keeps it (plan Q7).
        var seed = Path.Combine(world.Root, "seed", "history.jsonl");
        HistoryFixtures.Ledger(seed, "A", "PP", "PP", "PP", "PP", "PP");
        var quarantined = HistoryFixtures.Ids(2)[1];
        Assert.Equal(0, HistoryCommand.Run(["quarantine", quarantined, "--reason", "flaky upstream", "--history", seed], TextWriter.Null, TextWriter.Null, _ => null));
        world.Origin.Commit(HistoryWorld.Branch, new Dictionary<string, string>
        {
            ["history.jsonl"] = File.ReadAllText(seed),
            ["quarantine.json"] = File.ReadAllText(Path.Combine(Path.GetDirectoryName(seed)!, "quarantine.json"))
        });
        var job = world.Job(6);
        Report(job, "Reports", "PF");

        Assert.True(HistoryWorld.Read(job).Succeeded);
        var gate = HistoryWorld.Gate(job, new Dictionary<string, string> { ["reports"] = "Reports", ["test-outcome"] = "failure" });

        Assert.True(gate.Succeeded, gate.ToString());
        Assert.Contains("quarantined: 1", job.Summary.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public void Every_report_is_gated_and_one_trip_fails_the_step()
    {
        HistoryWorld.SkipWithoutBashOrGit();
        using var world = new HistoryWorld();
        var job = world.Job(6);
        Ledger(job);
        Report(job, "unit/Reports", "PF");
        Report(job, "component/Reports", "PP");

        var gate = HistoryWorld.Gate(job, new Dictionary<string, string> { ["reports"] = "unit/Reports\ncomponent/Reports\n", ["test-outcome"] = "failure" });

        Assert.False(gate.Succeeded);
        var summary = job.Summary.ToString();
        Assert.Contains("unit/Reports", summary, StringComparison.Ordinal);
        Assert.Contains("component/Reports", summary, StringComparison.Ordinal);
        Assert.Equal(2, summary.Split("new-failures:").Length - 1);
    }

    [Fact]
    public void The_inputs_reach_the_tool_as_flags()
    {
        HistoryWorld.SkipWithoutBashOrGit();
        using var world = new HistoryWorld();
        var job = world.Job(6);
        var ledger = Ledger(job);
        Report(job, "Reports", "PP");
        var arguments = Path.Combine(world.Root, "arguments.txt");
        var tool = Path.Combine(world.Root, "tool.sh");
        File.WriteAllText(tool, $"#!/usr/bin/env bash\nprintf '%s\\n' \"$@\" > \"{ChildProcess.Slashes(arguments)}\"\necho 'history: nothing to read'\n");

        var gate = HistoryWorld.Gate(job, new Dictionary<string, string>
        {
            ["reports"] = "Reports",
            ["fail-on"] = "new-failures,flaky",
            ["min-runs"] = "3",
            ["max-new-failures"] = "2",
            ["min-pass-rate"] = "0.9",
            ["tool-command"] = $"bash \"{ChildProcess.Slashes(tool)}\""
        });

        Assert.True(gate.Succeeded, gate.ToString());
        var given = File.ReadAllLines(arguments);
        Assert.Equal(["history", "gate"], given[..2]);
        Assert.EndsWith("Reports", given[2].Replace('\\', '/'), StringComparison.Ordinal);
        var flags = string.Join(' ', given[3..]);
        Assert.Contains("--history " + ledger, flags.Replace('/', Path.DirectorySeparatorChar).Replace('\\', Path.DirectorySeparatorChar), StringComparison.OrdinalIgnoreCase);
        Assert.Contains("--fail-on new-failures,flaky", flags, StringComparison.Ordinal);
        Assert.Contains("--min-runs 3", flags, StringComparison.Ordinal);
        Assert.Contains("--max-new-failures 2", flags, StringComparison.Ordinal);
        Assert.Contains("--min-pass-rate 0.9", flags, StringComparison.Ordinal);
    }

    [Fact]
    public void An_unreadable_report_is_an_error_not_a_pass()
    {
        HistoryWorld.SkipWithoutBashOrGit();
        using var world = new HistoryWorld();
        var job = world.Job(6);
        Ledger(job);
        Report(job, "Reports", "PP");

        var gate = HistoryWorld.Gate(job, new Dictionary<string, string> { ["reports"] = "Reports\nno-such-reports", ["test-outcome"] = "success" });

        Assert.False(gate.Succeeded);
        Assert.Equal("failed", gate.Outputs["result"]);
        Assert.Contains(gate.Errors, e => e.Contains("no-such-reports", StringComparison.Ordinal));
    }

    [Fact]
    public void The_gate_runs_after_a_failed_test_step()
    {
        HistoryWorld.SkipWithoutBashOrGit();
        using var world = new HistoryWorld();
        var job = world.Job(6);
        Ledger(job);
        Report(job, "Reports", "PF");
        job.Failed = true;

        var gate = HistoryWorld.Gate(job, new Dictionary<string, string> { ["reports"] = "Reports", ["test-outcome"] = "failure" });

        Assert.Equal("failed", gate.Outputs["result"]);
        Assert.Contains("new-failures: 1", job.Summary.ToString(), StringComparison.Ordinal);
    }
}
