using System.Text.RegularExpressions;

namespace Kronikol.Tests.Templates;

/// <summary>
/// The history action's <c>record</c> phase (plans/HISTORY_ACTION_PLAN.md §4.5): in a job after every test job, the
/// run's fragments folded into the ledger on the data branch and pushed. It keeps a repository of its own under
/// <c>RUNNER_TEMP</c>, never rebases and never force-pushes: a lost race is recorded again on the branch as it now is,
/// and a refusal while the branch stands still is reported at once. Only <c>git ls-remote</c>'s exit 2 ("no such
/// branch") ever makes a new branch.
/// </summary>
public class HistoryActionRecordTests
{
    private static readonly Dictionary<string, string> FromWorkspace = new() { ["path"] = "." };

    /// <summary>A job of run <paramref name="run"/> whose workspace holds one fragment per suite, as its test projects wrote them.</summary>
    private static RunnerJob JobWithFragments(HistoryWorld world, long run, params string[] suites)
    {
        var job = world.Job(run);
        foreach (var suite in suites)
            HistoryFixtures.Fragment(Path.Combine(job.Workspace, "tests", suite, "bin", "Reports"), suite, $"gh:{run}:1", "PP");
        return job;
    }

    [Fact]
    public void The_first_record_makes_an_orphan_branch_with_a_readme_the_merge_attribute_and_the_ledger()
    {
        HistoryWorld.SkipWithoutBashOrGit();
        using var world = new HistoryWorld();

        var record = HistoryWorld.Record(JobWithFragments(world, 1, "A", "B"), FromWorkspace);

        Assert.True(record.Succeeded, record.ToString());
        Assert.Equal("true", record.Outputs["pushed"]);
        Assert.Equal("2", record.Outputs["recorded"]);
        Assert.Equal("0", record.Outputs["duplicates"]);
        Assert.Equal(world.Origin.Tip(HistoryWorld.Branch), record.Outputs["commit"]);
        Assert.Equal([".gitattributes", "README.md", "history.jsonl"], world.Origin.Files(HistoryWorld.Branch));
        Assert.Contains("history.jsonl merge=union", world.Origin.Show(HistoryWorld.Branch, ".gitattributes"), StringComparison.Ordinal);
        Assert.Equal([0], world.Origin.Parents(HistoryWorld.Branch));
        var ledger = world.Ledger();
        Assert.Equal(1, ledger.Headers);
        Assert.Equal(["gh:1:1 A", "gh:1:1 B"], ledger.RunKeys.Order());
        Assert.Equal(["Record run 1:1 (0123456) [skip ci]"], world.Origin.Messages(HistoryWorld.Branch));
        Assert.Equal(world.Origin.Show(HistoryWorld.Branch, "history.jsonl")!.Length.ToString(), record.Outputs["ledger-bytes"]);
        Assert.Empty(record.Warnings);
        Assert.Empty(record.Errors);
    }

    [Fact]
    public void A_later_run_adds_one_line_per_suite_in_one_commit_named_for_the_run()
    {
        HistoryWorld.SkipWithoutBashOrGit();
        using var world = new HistoryWorld();
        Assert.True(HistoryWorld.Record(JobWithFragments(world, 1, "A", "B"), FromWorkspace).Succeeded);

        var record = HistoryWorld.Record(JobWithFragments(world, 2, "A", "B"), FromWorkspace);

        Assert.True(record.Succeeded, record.ToString());
        Assert.Equal("2", record.Outputs["recorded"]);
        Assert.Equal(["gh:1:1 A", "gh:1:1 B", "gh:2:1 A", "gh:2:1 B"], world.Ledger().RunKeys.Order());
        Assert.Equal(["Record run 2:1 (0123456) [skip ci]", "Record run 1:1 (0123456) [skip ci]"], world.Origin.Messages(HistoryWorld.Branch));
        Assert.Equal([1, 0], world.Origin.Parents(HistoryWorld.Branch));
    }

    [Fact]
    public void Recording_the_same_run_again_changes_nothing_and_succeeds()
    {
        HistoryWorld.SkipWithoutBashOrGit();
        using var world = new HistoryWorld();
        Assert.True(HistoryWorld.Record(JobWithFragments(world, 1, "A", "B"), FromWorkspace).Succeeded);
        var tip = world.Origin.Tip(HistoryWorld.Branch);

        var again = HistoryWorld.Record(JobWithFragments(world, 1, "A", "B"), FromWorkspace);

        Assert.True(again.Succeeded, again.ToString());
        Assert.Equal("false", again.Outputs["pushed"]);
        Assert.Equal("0", again.Outputs["recorded"]);
        Assert.Equal("2", again.Outputs["duplicates"]);
        Assert.Equal(tip, world.Origin.Tip(HistoryWorld.Branch));
    }

    // ─── Races ──────────────────────────────────────────────────

    private const int Writers = 4;
    private const int Races = 3;

    /// <summary>Four fold jobs of four workflow runs finishing together, each with its own workspace and two suites' fragments.</summary>
    private static async Task RaceAsync(HistoryWorld world, long firstRun)
    {
        var jobs = Enumerable.Range(0, Writers).Select(i => JobWithFragments(world, firstRun + i, "A", "B")).ToList();
        using var start = new Barrier(Writers);
        var records = await Task.WhenAll(jobs.Select(job => Task.Run(() =>
        {
            start.SignalAndWait();
            return HistoryWorld.Record(job, FromWorkspace);
        })));

        foreach (var record in records)
            Assert.True(record.Succeeded, record.ToString());
    }

    private static void AssertEveryRunOnce(HistoryWorld world, params long[] runs)
    {
        var ledger = world.Ledger();
        Assert.Equal(runs.SelectMany(run => new[] { $"gh:{run}:1 A", $"gh:{run}:1 B" }).Order(), ledger.RunKeys.Order());
        Assert.Equal(0, ledger.DuplicateRosterLines);
        Assert.Equal(1, ledger.Headers);
    }

    [Fact]
    public async Task Writers_racing_on_an_existing_branch_all_land_once()
    {
        HistoryWorld.SkipWithoutBashOrGit();
        for (var race = 0; race < Races; race++)
        {
            using var world = new HistoryWorld();
            Assert.True(HistoryWorld.Record(JobWithFragments(world, 1, "A", "B"), FromWorkspace).Succeeded);

            await RaceAsync(world, 2);

            AssertEveryRunOnce(world, 1, 2, 3, 4, 5);
        }
    }

    [Fact]
    public async Task Writers_racing_on_a_first_run_all_land_once_with_no_duplicate_roster_line()
    {
        HistoryWorld.SkipWithoutBashOrGit();
        for (var race = 0; race < Races; race++)
        {
            using var world = new HistoryWorld();

            await RaceAsync(world, 1);

            AssertEveryRunOnce(world, 1, 2, 3, 4);
            // Every writer made its own orphan root, and exactly one of them survives.
            Assert.Equal(1, world.Origin.Parents(HistoryWorld.Branch).Count(p => p == 0));
        }
    }

    // ─── Refusals ───────────────────────────────────────────────

    [Fact]
    public void A_push_refused_while_the_branch_stands_still_fails_at_once_naming_the_refusal()
    {
        HistoryWorld.SkipWithoutBashOrGit();
        using var world = new HistoryWorld();
        Assert.True(HistoryWorld.Record(JobWithFragments(world, 1, "A"), FromWorkspace).Succeeded);
        world.Origin.RefusePushesTo(HistoryWorld.Branch, "push declined due to repository rule violations");

        var record = HistoryWorld.Record(JobWithFragments(world, 2, "A"), FromWorkspace);

        Assert.False(record.Succeeded);
        Assert.Contains(record.Errors, e => e.Contains("push declined due to repository rule violations", StringComparison.Ordinal));
        Assert.Equal(1, world.Origin.Refusals);
    }

    [Fact]
    public void A_refused_first_push_fails_at_once_too()
    {
        HistoryWorld.SkipWithoutBashOrGit();
        using var world = new HistoryWorld();
        world.Origin.RefusePushesTo(HistoryWorld.Branch, "creating this branch is restricted");

        var record = HistoryWorld.Record(JobWithFragments(world, 1, "A"), FromWorkspace);

        Assert.False(record.Succeeded);
        Assert.Contains(record.Errors, e => e.Contains("creating this branch is restricted", StringComparison.Ordinal));
        Assert.Equal(1, world.Origin.Refusals);
        Assert.Null(world.Origin.Tip(HistoryWorld.Branch));
    }

    [Fact]
    public void A_read_only_token_fails_at_once_naming_the_refusal()
    {
        HistoryWorld.SkipWithoutBashOrGit();
        using var world = new HistoryWorld(http: true);
        Assert.True(HistoryWorld.Record(JobWithFragments(world, 1, "A"), FromWorkspace).Succeeded);
        var job = JobWithFragments(world, 2, "A");
        job.Github["token"] = HttpOrigin.ReadOnlyToken;
        var before = world.Http!.Requests.Count;

        var record = HistoryWorld.Record(job, FromWorkspace);

        Assert.False(record.Succeeded);
        Assert.Contains(record.Errors, e => e.Contains("403", StringComparison.Ordinal));
        Assert.Equal(1, world.Http.Requests.Skip(before).Count(r => r.Status == 403));
        Assert.Equal(["gh:1:1 A"], world.Ledger().RunKeys);
    }

    [Fact]
    public void An_unreachable_origin_is_an_error_and_never_a_new_branch()
    {
        HistoryWorld.SkipWithoutBashOrGit();
        using var world = new HistoryWorld();
        var job = JobWithFragments(world, 1, "A");
        job.Github["server_url"] = new Uri(Path.Combine(world.Root, "no-such-server")).AbsoluteUri.TrimEnd('/');
        var trace = Path.Combine(world.Root, "trace2.json");
        job.Env["GIT_TRACE2_EVENT"] = trace;

        var record = HistoryWorld.Record(job, FromWorkspace);

        Assert.False(record.Succeeded);
        Assert.Contains(record.Errors, e => e.Contains("exit 128", StringComparison.Ordinal));
        // Nothing was committed or pushed: a remote that cannot be read is never taken for an empty one.
        Assert.DoesNotMatch(new Regex("\"argv\":\\[[^\\]]*\"(push|commit)\""), File.Exists(trace) ? File.ReadAllText(trace) : "");
    }

    // ─── The machine's configuration ────────────────────────────

    [Fact]
    public void A_machine_hook_does_not_stop_the_data_commit()
    {
        HistoryWorld.SkipWithoutBashOrGit();
        using var world = new HistoryWorld();
        var job = JobWithFragments(world, 1, "A");
        job.GitConfigGlobal = new MachineConfig(Path.Combine(world.Root, "machine")).RefusingHooks().Path;

        var record = HistoryWorld.Record(job, FromWorkspace);

        Assert.True(record.Succeeded, record.ToString());
        Assert.Equal(["gh:1:1 A"], world.Ledger().RunKeys);
    }

    [Fact]
    public void A_machine_that_cannot_sign_fails_naming_gits_message()
    {
        HistoryWorld.SkipWithoutBashOrGit();
        using var world = new HistoryWorld();
        var job = JobWithFragments(world, 1, "A");
        job.GitConfigGlobal = new MachineConfig(Path.Combine(world.Root, "machine")).SignsWithoutAKey().Path;

        var record = HistoryWorld.Record(job, FromWorkspace);

        Assert.False(record.Succeeded);
        Assert.Contains(record.Errors, e => e.Contains("gpg failed to sign", StringComparison.Ordinal));
        Assert.Null(world.Origin.Tip(HistoryWorld.Branch));
    }

    [Fact]
    public void A_machine_that_converts_line_endings_still_writes_the_ledger_as_the_tool_wrote_it()
    {
        HistoryWorld.SkipWithoutBashOrGit();
        using var world = new HistoryWorld();
        var job = JobWithFragments(world, 1, "A");
        job.GitConfigGlobal = new MachineConfig(Path.Combine(world.Root, "machine")).ConvertsLineEndings().Path;
        Assert.True(HistoryWorld.Record(job, FromWorkspace).Succeeded);

        var second = JobWithFragments(world, 2, "A");
        second.GitConfigGlobal = job.GitConfigGlobal;
        var record = HistoryWorld.Record(second, FromWorkspace);

        Assert.True(record.Succeeded, record.ToString());
        Assert.DoesNotContain("\r", world.Origin.Show(HistoryWorld.Branch, "history.jsonl"), StringComparison.Ordinal);
        Assert.Equal(["gh:1:1 A", "gh:2:1 A"], world.Ledger().RunKeys);
    }

    [Fact]
    public void A_copy_of_the_folder_checked_out_with_autocrlf_runs()
    {
        HistoryWorld.SkipWithoutBashOrGit();
        using var world = new HistoryWorld();
        // A consumer copies the folder into .github/actions/, and a Windows runner checks it out with core.autocrlf=true.
        var consumer = Path.Combine(world.Root, "consumer");
        BareOrigin.Git(world.Root, "init", "-q", "-b", "main", consumer);
        CopyTree(HistoryWorld.Folder, Path.Combine(consumer, ".github", "actions", "kronikol-history"));
        BareOrigin.Git(consumer, "add", "-A");
        BareOrigin.Git(consumer, "commit", "-q", "-m", "copy the history action");
        var checkout = Path.Combine(world.Root, "checkout");
        BareOrigin.Git(world.Root, "-c", "core.autocrlf=true", "clone", "-q", consumer, checkout);
        var copy = Path.Combine(checkout, ".github", "actions", "kronikol-history");
        Assert.All(Directory.EnumerateFiles(Path.Combine(copy, "scripts")), script => Assert.DoesNotContain("\r", File.ReadAllText(script), StringComparison.Ordinal));

        var record = HistoryWorld.Record(JobWithFragments(world, 1, "A"), FromWorkspace, folder: copy);

        Assert.True(record.Succeeded, record.ToString());
        Assert.Equal(["gh:1:1 A"], world.Ledger().RunKeys);
    }

    private static void CopyTree(string from, string to)
    {
        foreach (var file in Directory.EnumerateFiles(from, "*", SearchOption.AllDirectories))
        {
            var target = Path.Combine(to, Path.GetRelativePath(from, file));
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            File.Copy(file, target);
        }
    }

    // ─── Guards ─────────────────────────────────────────────────

    [Fact]
    public void A_pull_request_run_is_not_recorded_unless_asked()
    {
        HistoryWorld.SkipWithoutBashOrGit();
        using var world = new HistoryWorld();
        var job = JobWithFragments(world, 1, "A");
        job.Github["event_name"] = "pull_request";

        var record = HistoryWorld.Record(job, FromWorkspace);

        Assert.True(record.Succeeded, record.ToString());
        Assert.Equal("0", record.Outputs["recorded"]);
        Assert.Equal("false", record.Outputs["pushed"]);
        Assert.Contains(record.Notices, n => n.Contains("record-pull-requests", StringComparison.Ordinal));
        Assert.Null(world.Origin.Tip(HistoryWorld.Branch));
    }

    [Fact]
    public void A_pull_request_target_run_is_not_recorded_unless_asked_either()
    {
        HistoryWorld.SkipWithoutBashOrGit();
        using var world = new HistoryWorld();
        var job = JobWithFragments(world, 1, "A");
        job.Github["event_name"] = "pull_request_target";

        var record = HistoryWorld.Record(job, FromWorkspace);

        Assert.True(record.Succeeded, record.ToString());
        Assert.Null(world.Origin.Tip(HistoryWorld.Branch));
    }

    [Fact]
    public void A_pull_request_run_is_recorded_under_its_own_stream_when_asked()
    {
        HistoryWorld.SkipWithoutBashOrGit();
        using var world = new HistoryWorld();
        var job = world.Job(1, eventName: "pull_request", refName: "42/merge");
        HistoryFixtures.Fragment(Path.Combine(job.Workspace, "Reports"), "A", "gh:1:1", "PP", branch: "42/merge");

        var record = HistoryWorld.Record(job, new Dictionary<string, string> { ["path"] = ".", ["record-pull-requests"] = "true" });

        Assert.True(record.Succeeded, record.ToString());
        Assert.Equal("42/merge", Assert.Single(world.Ledger().Runs).GetProperty("branch").GetString());
    }

    [Fact]
    public void Record_refuses_the_default_branch()
    {
        HistoryWorld.SkipWithoutBashOrGit();
        using var world = new HistoryWorld();
        var main = world.Origin.Tip("main");

        var record = HistoryWorld.Record(JobWithFragments(world, 1, "A"), new Dictionary<string, string> { ["path"] = ".", ["branch"] = "main" });

        Assert.False(record.Succeeded);
        Assert.Contains(record.Errors, e => e.Contains("default branch", StringComparison.Ordinal));
        Assert.Equal(main, world.Origin.Tip("main"));
    }

    // ─── What it folds ──────────────────────────────────────────

    [Fact]
    public void Record_folds_a_workspace_path_without_artifacts()
    {
        HistoryWorld.SkipWithoutBashOrGit();
        using var world = new HistoryWorld();
        var job = JobWithFragments(world, 1, "A");
        // A rotation a killed run left half-done, holding this run's own id: read as a kept attempt, it would fold in.
        HistoryFixtures.Fragment(Path.Combine(job.Workspace, "tests", "A", "bin", "Reports", "runs", ".incoming-gh_1_1"), "A", "gh:1:1", "FF",
            at: new DateTimeOffset(2026, 9, 29, 9, 0, 0, TimeSpan.Zero));

        var record = HistoryWorld.Record(job, FromWorkspace);

        Assert.True(record.Succeeded, record.ToString());
        Assert.Empty(job.Artifacts.Names);
        var line = Assert.Single(world.Ledger().Runs);
        Assert.Equal(("PP", "--"), (line.GetProperty("results").GetString(), line.GetProperty("attempts").GetString()));
    }

    [Fact]
    public void Without_fragments_there_is_nothing_to_record_and_that_is_a_notice()
    {
        HistoryWorld.SkipWithoutBashOrGit();
        using var world = new HistoryWorld();

        var record = HistoryWorld.Record(world.Job(1));

        Assert.True(record.Succeeded, record.ToString());
        Assert.Equal("false", record.Outputs["pushed"]);
        Assert.Contains(record.Notices, n => n.Contains("History.run.json", StringComparison.Ordinal));
        Assert.Null(world.Origin.Tip(HistoryWorld.Branch));
    }

    [Fact]
    public void Shards_of_one_run_downloaded_from_several_artifacts_fold_into_one_line_per_suite()
    {
        HistoryWorld.SkipWithoutBashOrGit();
        using var world = new HistoryWorld();
        var ids = HistoryFixtures.Ids(4);
        var shard1 = world.Job(7, job: "shard1");
        var shard2 = world.Job(7, job: "shard2");
        HistoryFixtures.Fragment(Path.Combine(shard1.Workspace, "Reports"), "A", "gh:7:1", "PP", ids[..2]);
        HistoryFixtures.Fragment(Path.Combine(shard2.Workspace, "Reports"), "A", "gh:7:1", "PF", ids[2..]);
        Assert.True(HistoryWorld.Save(shard1).Succeeded);
        Assert.True(HistoryWorld.Save(shard2).Succeeded);

        var record = HistoryWorld.Record(world.Job(7, job: "history"));

        Assert.True(record.Succeeded, record.ToString());
        var line = Assert.Single(world.Ledger().Runs);
        Assert.Equal(2, line.GetProperty("shards").GetInt32());
        Assert.Equal(4, line.GetProperty("results").GetString()!.Length);
    }

    [Fact]
    public void A_retry_inside_one_job_is_folded_as_an_attempt()
    {
        HistoryWorld.SkipWithoutBashOrGit();
        using var world = new HistoryWorld();
        var job = world.Job(7);
        var reports = Path.Combine(job.Workspace, "Reports");
        HistoryFixtures.Fragment(reports, "A", "gh:7:1", "PPP", at: new DateTimeOffset(2026, 9, 29, 10, 0, 30, TimeSpan.Zero));
        HistoryFixtures.Fragment(Path.Combine(reports, "runs", "gh_7_1"), "A", "gh:7:1", "PFP", at: new DateTimeOffset(2026, 9, 29, 10, 0, 0, TimeSpan.Zero));
        Assert.True(HistoryWorld.Save(job).Succeeded);

        var record = HistoryWorld.Record(world.Job(7, job: "history"));

        Assert.True(record.Succeeded, record.ToString());
        var line = Assert.Single(world.Ledger().Runs);
        Assert.Equal("PPP", line.GetProperty("results").GetString());
        Assert.Contains("2 attempts", record.Log, StringComparison.Ordinal);
    }

    [Fact]
    public void A_rerun_records_its_attempt_and_leaves_the_first_attempts_line_alone()
    {
        HistoryWorld.SkipWithoutBashOrGit();
        using var world = new HistoryWorld();
        var first = world.Job(7, attempt: 1);
        HistoryFixtures.Fragment(Path.Combine(first.Workspace, "Reports"), "A", "gh:7:1", "PF");
        Assert.True(HistoryWorld.Save(first).Succeeded);
        Assert.True(HistoryWorld.Record(world.Job(7, attempt: 1, job: "history")).Succeeded);

        var rerun = world.Job(7, attempt: 2);
        HistoryFixtures.Fragment(Path.Combine(rerun.Workspace, "Reports"), "A", "gh:7:2", "PP");
        Assert.True(HistoryWorld.Save(rerun).Succeeded);
        var record = HistoryWorld.Record(world.Job(7, attempt: 2, job: "history"));

        Assert.True(record.Succeeded, record.ToString());
        Assert.Equal(["gh:7:1 A", "gh:7:2 A"], world.Ledger().RunKeys);
        Assert.Equal("1", record.Outputs["recorded"]);
        Assert.Equal("1", record.Outputs["duplicates"]);
    }

    // ─── What it reports ────────────────────────────────────────

    [Fact]
    public void The_summary_names_the_recorded_runs_and_the_ledgers_size()
    {
        HistoryWorld.SkipWithoutBashOrGit();
        using var world = new HistoryWorld();
        var job = JobWithFragments(world, 1, "A", "B");

        var record = HistoryWorld.Record(job, FromWorkspace);

        Assert.True(record.Succeeded, record.ToString());
        var summary = job.Summary.ToString();
        Assert.Contains("gh:1:1", summary, StringComparison.Ordinal);
        Assert.Contains("suite A", summary, StringComparison.Ordinal);
        Assert.Contains("suite B", summary, StringComparison.Ordinal);
        Assert.Contains(record.Outputs["ledger-bytes"] + " bytes", summary, StringComparison.Ordinal);
        Assert.Contains(record.Outputs["commit"][..7], summary, StringComparison.Ordinal);
    }

    [Fact]
    public void A_ledger_over_the_threshold_is_a_warning()
    {
        HistoryWorld.SkipWithoutBashOrGit();
        using var world = new HistoryWorld();

        var record = HistoryWorld.Record(JobWithFragments(world, 1, "A"), new Dictionary<string, string> { ["path"] = ".", ["warn-ledger-mb"] = "0.0001" });

        Assert.True(record.Succeeded, record.ToString());
        Assert.Contains(record.Warnings, w => w.Contains(record.Outputs["ledger-bytes"], StringComparison.Ordinal) && w.Contains("0.0001 MB", StringComparison.Ordinal));
    }
}
