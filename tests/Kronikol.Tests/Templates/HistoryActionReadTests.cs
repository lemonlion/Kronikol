namespace Kronikol.Tests.Templates;

/// <summary>
/// The history action's <c>read</c> phase (plans/HISTORY_ACTION_PLAN.md §4.3): before the tests, the ledger and its
/// two companions from the data branch into <c>RUNNER_TEMP</c>, named in <c>KRONIKOL_HISTORY</c>, so the run's own
/// report, Failures.md and job summary carry the verdicts. It never fails the job: "a test run never fails over its
/// history".
/// </summary>
public class HistoryActionReadTests
{
    private const string Ledger = "{\"t\":\"header\",\"historyFormatVersion\":1}\n";
    private const string Quarantine = "{\"entries\":[]}\n";
    private const string Aliases = "{\"mappings\":{}}\n";

    [Fact]
    public void Without_a_history_branch_read_finds_nothing_and_the_run_goes_on_without_history()
    {
        HistoryWorld.SkipWithoutBashOrGit();
        using var world = new HistoryWorld();
        var job = world.Job(1);

        var read = HistoryWorld.Read(job);

        Assert.True(read.Succeeded, read.ToString());
        Assert.Equal("false", read.Outputs["found"]);
        Assert.Equal("", read.Outputs["ledger"]);
        Assert.False(job.Env.ContainsKey("KRONIKOL_HISTORY"));
        Assert.Contains(read.Notices, n => n.Contains(HistoryWorld.Branch, StringComparison.Ordinal) && n.Contains("record", StringComparison.Ordinal));
        Assert.Empty(read.Warnings);
    }

    [Fact]
    public void Read_copies_the_ledger_and_both_companions_side_by_side_and_names_the_ledger()
    {
        HistoryWorld.SkipWithoutBashOrGit();
        using var world = new HistoryWorld();
        world.Origin.Commit(HistoryWorld.Branch, new Dictionary<string, string>
        {
            ["history.jsonl"] = Ledger, ["quarantine.json"] = Quarantine, ["aliases.json"] = Aliases, ["README.md"] = "# ledger\n"
        });
        var job = world.Job(1);

        var read = HistoryWorld.Read(job);

        Assert.True(read.Succeeded, read.ToString());
        Assert.Equal("true", read.Outputs["found"]);
        var ledger = job.Env["KRONIKOL_HISTORY"];
        Assert.Equal(ledger, read.Outputs["ledger"]);
        Assert.Equal(Ledger, File.ReadAllText(ledger));
        Assert.Equal(Ledger.Length.ToString(), read.Outputs["bytes"]);
        // The resolver reads the companions from beside the ledger (HistoryQuarantine.PathBeside, HistoryAliases.PathBeside).
        var directory = Path.GetDirectoryName(ledger)!;
        Assert.Equal(Quarantine, File.ReadAllText(Path.Combine(directory, "quarantine.json")));
        Assert.Equal(Aliases, File.ReadAllText(Path.Combine(directory, "aliases.json")));
        Assert.False(File.Exists(Path.Combine(directory, "README.md")));
        Assert.StartsWith(Path.GetFullPath(job.Temp), Path.GetFullPath(ledger), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void A_companion_the_branch_does_not_hold_is_not_in_the_copy()
    {
        HistoryWorld.SkipWithoutBashOrGit();
        using var world = new HistoryWorld();
        world.Origin.Commit(HistoryWorld.Branch, new Dictionary<string, string> { ["history.jsonl"] = Ledger, ["quarantine.json"] = Quarantine });
        var job = world.Job(1);
        // An aliases.json an earlier read of the same job left behind is not the branch's.
        var stale = Path.Combine(job.Temp, "kronikol-history", "aliases.json");
        Directory.CreateDirectory(Path.GetDirectoryName(stale)!);
        File.WriteAllText(stale, Aliases);

        var read = HistoryWorld.Read(job);

        Assert.True(read.Succeeded, read.ToString());
        var directory = Path.GetDirectoryName(job.Env["KRONIKOL_HISTORY"])!;
        Assert.True(File.Exists(Path.Combine(directory, "quarantine.json")));
        Assert.False(File.Exists(Path.Combine(directory, "aliases.json")));
    }

    [Fact]
    public void A_branch_without_a_ledger_is_no_history_and_a_notice()
    {
        HistoryWorld.SkipWithoutBashOrGit();
        using var world = new HistoryWorld();
        world.Origin.Commit(HistoryWorld.Branch, new Dictionary<string, string> { ["README.md"] = "# not yet\n" });
        var job = world.Job(1);

        var read = HistoryWorld.Read(job);

        Assert.True(read.Succeeded, read.ToString());
        Assert.Equal("false", read.Outputs["found"]);
        Assert.False(job.Env.ContainsKey("KRONIKOL_HISTORY"));
        Assert.Contains(read.Notices, n => n.Contains("history.jsonl", StringComparison.Ordinal));
    }

    [Fact]
    public void An_unreachable_origin_is_a_warning_and_never_fails_the_job()
    {
        HistoryWorld.SkipWithoutBashOrGit();
        using var world = new HistoryWorld();
        var job = world.Job(1);
        job.Github["server_url"] = new Uri(Path.Combine(world.Root, "no-such-server")).AbsoluteUri.TrimEnd('/');

        var read = HistoryWorld.Read(job);

        Assert.True(read.Succeeded, read.ToString());
        Assert.Equal("false", read.Outputs["found"]);
        Assert.False(job.Env.ContainsKey("KRONIKOL_HISTORY"));
        Assert.Contains(read.Warnings, w => w.Contains("exit 128", StringComparison.Ordinal));
    }

    [Fact]
    public void Read_leaves_the_checkout_untouched()
    {
        HistoryWorld.SkipWithoutBashOrGit();
        using var world = new HistoryWorld();
        world.Origin.Commit(HistoryWorld.Branch, new Dictionary<string, string> { ["history.jsonl"] = Ledger });
        var job = world.Job(1);
        Directory.Delete(job.Workspace);
        BareOrigin.Git(world.Root, "clone", "-q", world.Origin.ServerUrl + "/octo/app.git", job.Workspace);
        string Snapshot() => string.Join("\n",
            File.ReadAllText(Path.Combine(job.Workspace, ".git", "config")),
            BareOrigin.Git(job.Workspace, "for-each-ref"),
            BareOrigin.Git(job.Workspace, "status", "--porcelain", "--ignored"),
            File.Exists(Path.Combine(job.Workspace, ".git", "FETCH_HEAD")).ToString(),
            BareOrigin.Git(job.Workspace, "count-objects", "-v"));
        var before = Snapshot();

        var read = HistoryWorld.Read(job);

        Assert.True(read.Succeeded, read.ToString());
        Assert.Equal("true", read.Outputs["found"]);
        Assert.Equal(before, Snapshot());
    }

    [Fact]
    public void Read_downloads_only_the_files_it_reads()
    {
        HistoryWorld.SkipWithoutBashOrGit();
        using var world = new HistoryWorld(http: true);
        // Beside the ledger, a file the size 9.4's views would reach, which compresses no better than random data.
        var random = new Random(42);
        var views = string.Join("\n", Enumerable.Range(0, 40_000).Select(_ => Convert.ToBase64String(random.GetItems<byte>(Enumerable.Range(0, 256).Select(b => (byte)b).ToArray(), 60))));
        world.Origin.Commit(HistoryWorld.Branch, new Dictionary<string, string> { ["history.jsonl"] = Ledger, ["quarantine.json"] = Quarantine, ["views.jsonl"] = views });
        var job = world.Job(1);
        var before = world.Http!.Requests.Count;

        var read = HistoryWorld.Read(job);

        Assert.True(read.Succeeded, read.ToString());
        Assert.Equal(Ledger, File.ReadAllText(job.Env["KRONIKOL_HISTORY"]));
        var served = world.Http.Requests.Skip(before).Sum(r => r.Bytes);
        Assert.True(served < 200_000, $"read was served {served:N0} bytes for a {Ledger.Length}-byte ledger beside {views.Length:N0} bytes it never reads");
    }
}
