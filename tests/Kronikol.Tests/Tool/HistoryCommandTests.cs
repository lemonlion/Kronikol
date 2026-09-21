using Kronikol.History;
using Kronikol.Tool;

namespace Kronikol.Tests.Tool;

/// <summary>
/// <c>kronikol history</c>: the bookkeeping over the ledger (plans/CROSS_RUN_HISTORY_PLAN.md §6.5, §7.7).
/// Every verb resolves the ledger the same way and says where it looked when nothing is there, and
/// <c>record</c> is what turns a CI run's fragments into one line without losing a shard.
/// </summary>
public class HistoryCommandTests : IDisposable
{
    private readonly string _dir = Directory.CreateTempSubdirectory("kronikol-history-cmd").FullName;

    public void Dispose()
    {
        try { Directory.Delete(_dir, recursive: true); } catch (IOException) { }
        GC.SuppressFinalize(this);
    }

    private string Ledger => Path.Combine(_dir, "ledger", "history.jsonl");

    private static (string Out, string Err, int Exit) Run(Func<string, string?>? env, params string[] args)
    {
        var output = new StringWriter();
        var error = new StringWriter();
        var exit = HistoryCommand.Run(args, output, error, env ?? (_ => null));
        return (output.ToString(), error.ToString(), exit);
    }

    private static HistoryRoster Roster(string suite, params string[] ids) =>
        HistoryRoster.Create(suite, ids.Select(id => new HistoryRosterEntry(id, "Scenario " + id, "Feature", null)).ToArray());

    private static HistoryRun RunLine(HistoryRoster roster, string id, string results, bool? partial = null) => new()
    {
        Id = id, Suite = roster.Suite, Partial = partial, At = new DateTimeOffset(2026, 9, 12, 10, 0, 0, TimeSpan.Zero),
        Branch = "main", Commit = "abc1234", Provider = "GitHubActions", Url = null, Shards = 1, RosterHash = roster.Hash,
        Results = results, Attempts = new string('-', results.Length), Durations = results.Select(_ => (int?)100).ToArray(),
        Calls = results.Select(_ => 1).ToArray(), ShapeSet = results.Select(_ => "aaaaaaaa").ToArray(), ShapeOrdered = results.Select(_ => "aaaaaaaa").ToArray(),
        Errors = results.Select(r => r == 'F' ? "e1" : null).ToArray(),
        ErrorText = results.Contains('F') ? new Dictionary<string, string> { ["e1"] = "boom" } : new Dictionary<string, string>(),
        Deps = ["Test>orders"]
    };

    private string WriteFragment(string folder, HistoryRoster roster, HistoryRun run)
    {
        var directory = Path.Combine(_dir, folder);
        Directory.CreateDirectory(directory);
        var path = Path.Combine(directory, HistoryFormat.FragmentFileName);
        File.WriteAllText(path, HistoryFragment.Write(roster, run, "3.9.0"));
        return directory;
    }

    // ─── record ────────────────────────────────────────────────

    [Fact]
    public void Record_says_when_the_shards_of_a_run_were_fingerprinted_under_different_rules()
    {
        // The fold rebuilds the run line field by field, and took the rule from the first shard that had
        // one. For a version that is harmless; for a hash of consumer rules, eight shards configured
        // differently would have folded into one line claiming one rule.
        var a = Roster("Suite", "id1");
        var b = Roster("Suite", "id2");
        WriteFragment("shard1", a, RunLine(a, "gh:7:1", "P") with { ShapeVersion = InteractionShape.Version, ShapeRules = "11112222" });
        WriteFragment("shard2", b, RunLine(b, "gh:7:1", "P") with { ShapeVersion = InteractionShape.Version });

        var (output, error, exit) = Run(null, "record", _dir, "--history", Ledger);

        Assert.True(exit == 0, error);
        Assert.Contains("fingerprinted under different templating rules", output);
        var folded = HistoryLedgerReader.Read(Ledger, 50).Ledger!.Runs("Suite").Single();
        Assert.Equal("PP", folded.Results);
        Assert.Null(folded.ShapeSet);
        Assert.Null(folded.ShapeRules);
    }

    [Fact]
    public void Record_folds_the_shards_of_one_run_into_one_line()
    {
        var shard1 = WriteFragment("shard1", Roster("Suite", "id1", "id2"), RunLine(Roster("Suite", "id1", "id2"), "gh:7:1", "PP"));
        var shard2 = WriteFragment("shard2", Roster("Suite", "id3"), RunLine(Roster("Suite", "id3"), "gh:7:1", "F"));
        WriteFragment("other", Roster("Suite", "id1", "id2", "id3"), RunLine(Roster("Suite", "id1", "id2", "id3"), "gh:6:1", "PPP"));

        var (output, error, exit) = Run(null, "record", _dir, "--history", Ledger);

        Assert.True(exit == 0, error);
        Assert.Contains("recorded   gh:7:1", output);
        Assert.Contains("from 2 shards", output);
        Assert.Contains("2 run(s) recorded", output);
        var ledger = HistoryLedgerReader.Read(Ledger, 50).Ledger!;
        var runs = ledger.Runs("Suite");
        Assert.Equal(2, runs.Count);
        var folded = runs.Single(r => r.Id == "gh:7:1");
        Assert.Equal(2, folded.Shards);
        Assert.Equal("PPF", folded.Results);
        Assert.Equal(3, ledger.Roster(folded.RosterHash)!.Count);
        _ = (shard1, shard2);
    }

    [Fact]
    public void Record_is_idempotent_and_says_which_runs_were_already_there()
    {
        WriteFragment("run", Roster("Suite", "id1"), RunLine(Roster("Suite", "id1"), "gh:1:1", "P"));
        Run(null, "record", _dir, "--history", Ledger);

        var (output, error, exit) = Run(null, "record", _dir, "--history", Ledger);

        Assert.True(exit == 0, error);
        Assert.Contains("duplicate  gh:1:1", output);
        Assert.Single(HistoryLedgerReader.Read(Ledger, 50).Ledger!.Runs("Suite"));
    }

    [Fact]
    public void Record_resolves_partial_against_the_ledger_not_per_shard()
    {
        // Each shard alone lacks most of the suite; folded they are the whole of it. A run that is
        // genuinely a subset of the previous full run is marked partial.
        WriteFragment("full", Roster("Suite", "id1", "id2", "id3"), RunLine(Roster("Suite", "id1", "id2", "id3"), "gh:1:1", "PPP"));
        Run(null, "record", Path.Combine(_dir, "full"), "--history", Ledger);
        WriteFragment("s1", Roster("Suite", "id1", "id2"), RunLine(Roster("Suite", "id1", "id2"), "gh:2:1", "PP"));
        WriteFragment("s2", Roster("Suite", "id3"), RunLine(Roster("Suite", "id3"), "gh:2:1", "P"));
        WriteFragment("one", Roster("Suite", "id1"), RunLine(Roster("Suite", "id1"), "gh:3:1", "P"));

        var (output, error, exit) = Run(null, "record", Path.Combine(_dir, "s1"), Path.Combine(_dir, "s2"), Path.Combine(_dir, "one"), "--history", Ledger);

        Assert.True(exit == 0, error);
        var runs = HistoryLedgerReader.Read(Ledger, 50).Ledger!.Runs("Suite");
        Assert.False(runs.Single(r => r.Id == "gh:2:1").Partial);
        Assert.True(runs.Single(r => r.Id == "gh:3:1").Partial);
        Assert.Contains("gh:3:1", output.Split('\n').Single(l => l.Contains("partial", StringComparison.Ordinal)));
    }

    [Fact]
    public void Record_with_nothing_to_fold_says_so()
    {
        Directory.CreateDirectory(Path.Combine(_dir, "empty"));

        var (_, error, exit) = Run(null, "record", Path.Combine(_dir, "empty"), "--history", Ledger);

        Assert.Equal(2, exit);
        Assert.Contains(HistoryFormat.FragmentFileName, error);
        Assert.False(File.Exists(Ledger));
    }

    [Fact]
    public void A_fragment_from_a_newer_format_is_refused_rather_than_folded()
    {
        var directory = Path.Combine(_dir, "newer");
        Directory.CreateDirectory(directory);
        var text = HistoryFragment.Write(Roster("Suite", "id1"), RunLine(Roster("Suite", "id1"), "gh:1:1", "P"), "9.0.0")
            .Replace("\"historyFormatVersion\": 1", "\"historyFormatVersion\": 99", StringComparison.Ordinal);
        File.WriteAllText(Path.Combine(directory, HistoryFormat.FragmentFileName), text);

        var (_, error, exit) = Run(null, "record", directory, "--history", Ledger);

        Assert.Equal(1, exit);
        Assert.Contains("99", error);
        Assert.False(File.Exists(Ledger));
    }

    // ─── resolution ────────────────────────────────────────────

    [Fact]
    public void The_ledger_is_found_above_the_input_when_nothing_names_it()
    {
        Directory.CreateDirectory(Path.Combine(_dir, ".kronikol"));
        var reports = WriteFragment(Path.Combine("tests", "X", "bin", "Reports"), Roster("Suite", "id1"), RunLine(Roster("Suite", "id1"), "gh:1:1", "P"));

        var (output, error, exit) = Run(null, "record", reports);

        Assert.True(exit == 0, error);
        Assert.Contains(Path.Combine(_dir, ".kronikol", "history.jsonl"), output);
        Assert.True(File.Exists(Path.Combine(_dir, ".kronikol", "history.jsonl")));
    }

    [Fact]
    public void The_environment_variable_names_the_ledger()
    {
        var reports = WriteFragment("reports", Roster("Suite", "id1"), RunLine(Roster("Suite", "id1"), "gh:1:1", "P"));

        var (_, error, exit) = Run(name => name == "KRONIKOL_HISTORY" ? Ledger : null, "record", reports);

        Assert.True(exit == 0, error);
        Assert.True(File.Exists(Ledger));
    }

    [Fact]
    public void History_switched_off_in_the_environment_is_refused_unless_a_ledger_is_named()
    {
        var reports = WriteFragment("reports", Roster("Suite", "id1"), RunLine(Roster("Suite", "id1"), "gh:1:1", "P"));

        var refused = Run(name => name == "KRONIKOL_HISTORY" ? "off" : null, "record", reports);
        var named = Run(name => name == "KRONIKOL_HISTORY" ? "off" : null, "record", reports, "--history", Ledger);

        Assert.Equal(2, refused.Exit);
        Assert.Contains("--history", refused.Err);
        Assert.Equal(0, named.Exit);
    }

    [Fact]
    public void Nothing_found_names_the_variable_the_flag_and_init()
    {
        var (_, error, exit) = Run(null, "show", "--history", Path.Combine(_dir, "nowhere", "history.jsonl"));

        Assert.Equal(2, exit);
        Assert.Contains("kronikol history init", error);
    }

    // ─── init ──────────────────────────────────────────────────

    [Fact]
    public void Init_creates_the_ledger_and_the_merge_attribute_and_is_idempotent()
    {
        var (output, error, exit) = Run(null, "init", "--history", Path.Combine(_dir, ".kronikol", "history.jsonl"));

        Assert.True(exit == 0, error);
        Assert.Contains("created", output);
        Assert.StartsWith("{\"t\":\"header\",\"historyFormatVersion\":1", File.ReadAllText(Path.Combine(_dir, ".kronikol", "history.jsonl")));
        var attributes = File.ReadAllText(Path.Combine(_dir, ".gitattributes"));
        Assert.Contains(HistoryFormat.GitAttributesLine, attributes);

        var again = Run(null, "init", "--history", Path.Combine(_dir, ".kronikol", "history.jsonl"));
        Assert.Equal(0, again.Exit);
        Assert.Contains("exists", again.Out);
        Assert.Contains("present", again.Out);
        Assert.Equal(1, File.ReadAllText(Path.Combine(_dir, ".gitattributes")).Split("merge=union").Length - 1);
    }

    [Fact]
    public void Init_keeps_an_existing_gitattributes_file_intact()
    {
        File.WriteAllText(Path.Combine(_dir, ".gitattributes"), "* text=auto\n*.cs text diff=csharp");

        var (_, error, exit) = Run(null, "init", "--history", Path.Combine(_dir, ".kronikol", "history.jsonl"));

        Assert.True(exit == 0, error);
        var attributes = File.ReadAllText(Path.Combine(_dir, ".gitattributes"));
        Assert.StartsWith("* text=auto\n*.cs text diff=csharp\n", attributes);
        Assert.EndsWith(HistoryFormat.GitAttributesLine + "\n", attributes);
    }

    // ─── show / verify / prune / compact ───────────────────────

    private void Seed(int runs, string suite = "Suite")
    {
        var roster = Roster(suite, "id1", "id2");
        for (var i = 1; i <= runs; i++)
            HistoryLedgerWriter.Append(Ledger, roster, RunLine(roster, $"gh:{i}:1", i % 2 == 0 ? "PF" : "PP") with { Branch = i % 3 == 0 ? "feature/x" : "main" }, "3.9.0");
    }

    [Fact]
    public void Show_lists_each_suite_with_its_runs_streams_and_last_run()
    {
        Seed(6);
        Seed(2, "Other");

        var (output, error, exit) = Run(null, "show", "--history", Ledger);

        Assert.True(exit == 0, error);
        Assert.Contains("suite    Other", output);
        Assert.Contains("suite    Suite", output);
        Assert.Contains("runs   6 in the window of 50", output);
        Assert.Contains("main (4)", output);
        Assert.Contains("feature/x (2)", output);
        Assert.Contains("last   gh:6:1", output);
        Assert.Contains("2 scenarios: 1 passed, 1 failed", output);

        var filtered = Run(null, "show", "--history", Ledger, "--suite", "Other");
        Assert.DoesNotContain("suite    Suite", filtered.Out);
        Assert.Contains("suite    Other", filtered.Out);
    }

    [Fact]
    public void Verify_reports_findings_and_exits_1_or_says_the_ledger_is_sound()
    {
        Seed(2);
        var clean = Run(null, "verify", "--history", Ledger);
        Assert.Equal(0, clean.Exit);
        Assert.Contains("no findings", clean.Out);

        File.AppendAllText(Ledger, "{\"t\":\"run\",\"id\":\"gh:9:1\",\"suite\":\"Suite\",\"partial\":false,\"at\":\"2026-09-12T10:00:00Z\",\"branch\":\"main\",\"commit\":null,\"provider\":null,\"url\":null,\"shards\":1,\"roster\":\"0000000000000000\",\"results\":\"PP\",\"attempts\":\"--\"}\n");
        var damaged = Run(null, "verify", "--history", Ledger);
        Assert.Equal(1, damaged.Exit);
        Assert.Contains("gh:9:1", damaged.Out);
        Assert.Contains("not in the file", damaged.Out);
    }

    [Fact]
    public void Prune_and_compact_rewrite_the_ledger_and_say_what_changed()
    {
        Seed(6);

        var pruned = Run(null, "prune", "--history", Ledger, "--window", "2");
        Assert.True(pruned.Exit == 0, pruned.Err);
        Assert.Contains("4 run(s) dropped, 2 kept", pruned.Out);
        Assert.Equal(2, HistoryLedgerReader.Read(Ledger, 50).Ledger!.Runs("Suite").Count);

        var compacted = Run(null, "compact", "--history", Ledger, "--window", "1");
        Assert.True(compacted.Exit == 0, compacted.Err);
        Assert.Contains("2 run(s) kept", compacted.Out);
        Assert.Equal(2, HistoryLedgerReader.Read(Ledger, 50).Ledger!.Runs("Suite").Count);
    }

    [Fact]
    public void An_unknown_verb_and_a_bad_window_are_usage_errors()
    {
        Assert.Equal(2, Run(null, "frobnicate").Exit);
        Assert.Contains("kronikol history", Run(null, "frobnicate").Err);
        Assert.Equal(2, Run(null, "prune", "--history", Ledger, "--window", "many").Exit);
        Assert.Equal(2, Run(null, "show", "--history", Ledger, "--bogus").Exit);
    }

    [Fact]
    public void The_command_is_in_the_table_with_its_usage()
    {
        var output = new StringWriter();
        var error = new StringWriter();

        var exit = Commands.Dispatch(["history", "--help"], output, error);

        Assert.Equal(0, exit);
        Assert.StartsWith("kronikol history", output.ToString());
        Assert.Contains("record", output.ToString());
    }
}
