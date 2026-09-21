using System.Text;
using Kronikol.History;

namespace Kronikol.Tests.History;

/// <summary>
/// The on-disk ledger (plans/CROSS_RUN_HISTORY_PLAN.md §3): append-only JSONL, roster-interned, written
/// under an exclusive lock and read by streaming. Every fact here is one the plan measured a defect
/// behind - a counter-keyed roster corrupts silently under <c>merge=union</c>, a plain append loses a
/// third of its lines, a rewritten document dies to one <c>SIGKILL</c> - so the shape is pinned, not
/// merely exercised.
/// </summary>
public class HistoryLedgerTests : IDisposable
{
    private readonly string _dir = Directory.CreateTempSubdirectory("kronikol-ledger").FullName;

    public void Dispose()
    {
        try { Directory.Delete(_dir, recursive: true); } catch (IOException) { }
        GC.SuppressFinalize(this);
    }

    private string LedgerPath => Path.Combine(_dir, ".kronikol", "history.jsonl");

    private static HistoryRoster Roster(string? suite, params string[] ids) =>
        HistoryRoster.Create(suite, ids.Select((id, i) => new HistoryRosterEntry(id, "Scenario " + i, "Feature", null)).ToArray());

    private static HistoryRun Run(HistoryRoster roster, string id, string results, string? branch = "main",
        DateTimeOffset? at = null, bool? partial = false) => new()
    {
        Id = id,
        Suite = roster.Suite,
        Partial = partial,
        At = at ?? new DateTimeOffset(2026, 9, 12, 10, 4, 11, TimeSpan.Zero),
        Branch = branch,
        Commit = "abc1234",
        Provider = "GitHubActions",
        Url = null,
        Shards = 1,
        RosterHash = roster.Hash,
        Results = results,
        Attempts = new string(HistoryFormat.AttemptUnknown, results.Length),
        Durations = Enumerable.Range(0, results.Length).Select(i => (int?)(1000 + i)).ToArray(),
        Calls = Enumerable.Repeat(3, results.Length).ToArray(),
        ShapeSet = Enumerable.Repeat("aaaaaaaa", results.Length).ToArray(),
        ShapeOrdered = Enumerable.Repeat("bbbbbbbb", results.Length).ToArray(),
        ShapeVersion = InteractionShape.Version,
        Errors = results.Select(r => r == 'F' ? "e1" : null).ToArray(),
        ErrorText = results.Contains('F') ? new Dictionary<string, string> { ["e1"] = "Expected 200 but got 500" } : new Dictionary<string, string>(),
        Deps = ["Caller>Orders"]
    };

    // ─── Format ────────────────────────────────────────────────

    [Fact]
    public void A_roster_key_is_a_content_hash_never_a_counter()
    {
        // §3.1: under merge=union two branches each adding a roster under a sequential key merged into a
        // file with two different rosters both named r2. A content hash makes the same set the same key
        // and a different set a different one, by construction.
        var same = Roster("Suite", "a1b2", "c3d4");
        var again = Roster("Suite", "a1b2", "c3d4");
        var other = Roster("Suite", "a1b2", "ffff");
        var otherSuite = Roster("Other", "a1b2", "c3d4");

        Assert.Equal(same.Hash, again.Hash);
        Assert.NotEqual(same.Hash, other.Hash);
        Assert.NotEqual(same.Hash, otherSuite.Hash);
        Assert.Matches("^[0-9a-f]{16}$", same.Hash);
    }

    [Fact]
    public void A_roster_keeps_every_scenario_sharing_a_stableId_positionally()
    {
        // §5.5: a [Theory] with repeated data, the same Examples row in two blocks, a retry - one slot per
        // id would silently drop the second holder. The roster lists the id twice and numbers the holders.
        var roster = Roster("Suite", "same", "same", "other");

        Assert.Equal(["same", "same", "other"], roster.Ids);
        Assert.Equal([0, 1, 0], roster.Slots);
        Assert.Equal(3, roster.Count);
    }

    [Fact]
    public void Two_platforms_serialise_the_same_run_to_the_same_bytes()
    {
        // §3.1: ordinal key order, invariant culture, LF, no BOM, ASCII-only escaping - so a ledger written
        // on Windows and one written on Linux for the same run are byte-identical and git sees one line.
        var roster = Roster("Suite ünïcode", "a1b2");
        var run = Run(roster, "gh:1:1", "P");

        var line = HistoryJson.RunLine(run);
        var rosterLine = HistoryJson.RosterLine(roster);

        Assert.DoesNotContain("\r", line);
        Assert.DoesNotContain("\n", line);
        Assert.All(line + rosterLine, c => Assert.True(c < 128, $"non-ASCII byte in a ledger line: {c}"));
        Assert.StartsWith("{\"t\":\"run\",\"id\":\"gh:1:1\",\"suite\":\"Suite \\u00FCn\\u00EFcode\",\"partial\":false,\"at\":\"2026-09-12T10:04:11Z\"", line);
        Assert.StartsWith("{\"t\":\"roster\",\"hash\":\"" + roster.Hash + "\",\"suite\":", rosterLine);
        Assert.Equal(line, HistoryJson.RunLine(HistoryJson.Parse(line).Run!));
    }

    [Fact]
    public void Every_field_round_trips_through_a_line()
    {
        var roster = Roster("Suite", "a1b2", "c3d4");
        var run = Run(roster, "gh:18273645:2", "PF", branch: "feature/x", partial: null);

        var parsed = HistoryJson.Parse(HistoryJson.RunLine(run));

        Assert.Equal(HistoryLineKind.Run, parsed.Kind);
        var back = parsed.Run!;
        Assert.Equal(run.Id, back.Id);
        Assert.Equal(run.Suite, back.Suite);
        Assert.Null(back.Partial);
        Assert.Equal(run.At, back.At);
        Assert.Equal("feature/x", back.Branch);
        Assert.Equal("abc1234", back.Commit);
        Assert.Equal("GitHubActions", back.Provider);
        Assert.Equal(1, back.Shards);
        Assert.Equal(roster.Hash, back.RosterHash);
        Assert.Equal("PF", back.Results);
        Assert.Equal("--", back.Attempts);
        Assert.Equal([1000, 1001], back.Durations);
        Assert.Equal([3, 3], back.Calls);
        Assert.Equal(["aaaaaaaa", "aaaaaaaa"], back.ShapeSet);
        Assert.Equal(["bbbbbbbb", "bbbbbbbb"], back.ShapeOrdered);
        Assert.Equal(InteractionShape.Version, back.ShapeVersion);
        Assert.Equal([null, "e1"], back.Errors);
        Assert.Equal("Expected 200 but got 500", back.ErrorText["e1"]);
        Assert.Equal(["Caller>Orders"], back.Deps);

        var rosterBack = HistoryJson.Parse(HistoryJson.RosterLine(roster)).Roster!;
        Assert.Equal(roster.Hash, rosterBack.Hash);
        Assert.Equal(roster.Ids, rosterBack.Ids);
        Assert.Equal(roster.Names, rosterBack.Names);
        Assert.Equal(roster.Features, rosterBack.Features);
    }

    [Fact]
    public void A_line_from_before_the_shape_rule_was_versioned_reads_as_rule_one()
    {
        // 3.9.0 to 3.13.0 wrote fingerprints without saying which rule made them: rule 1. A line without
        // fingerprints has no rule at all.
        var roster = Roster("Suite", "a1b2");
        var line = HistoryJson.RunLine(Run(roster, "gh:1:1", "P")).Replace(",\"shapeVersion\":" + InteractionShape.Version.ToString(System.Globalization.CultureInfo.InvariantCulture), "");
        Assert.DoesNotContain("shapeVersion", line);

        var back = HistoryJson.Parse(line).Run!;
        Assert.Equal(1, back.ShapeVersion);

        var bare = HistoryJson.Parse(HistoryJson.RunLine(Run(roster, "gh:2:1", "P") with { ShapeSet = null, ShapeOrdered = null, ShapeVersion = null })).Run!;
        Assert.Null(bare.ShapeVersion);
    }

    [Fact]
    public void A_null_suite_is_recorded_as_null_not_as_an_empty_name()
    {
        // §3.2: a run whose suite did not resolve must be tellable from a suite literally named nothing,
        // so history doctor can say which runs were affected.
        var roster = Roster(null, "a1b2");
        var line = HistoryJson.RunLine(Run(roster, "local:1:x", "P"));

        Assert.Contains("\"suite\":null", line);
        Assert.Null(HistoryJson.Parse(line).Run!.Suite);
    }

    [Fact]
    public void The_results_alphabet_covers_every_execution_result_and_two_non_results()
    {
        Assert.Equal('P', HistoryFormat.ResultChar(Kronikol.Reports.ExecutionResult.Passed));
        Assert.Equal('F', HistoryFormat.ResultChar(Kronikol.Reports.ExecutionResult.Failed));
        Assert.Equal('S', HistoryFormat.ResultChar(Kronikol.Reports.ExecutionResult.Skipped));
        Assert.Equal('B', HistoryFormat.ResultChar(Kronikol.Reports.ExecutionResult.Bypassed));
        Assert.Equal('A', HistoryFormat.ResultChar(Kronikol.Reports.ExecutionResult.SkippedAfterFailure));
        Assert.Equal('?', HistoryFormat.Unknown);
        Assert.Equal('.', HistoryFormat.Absent);
        // §7.2: only a pass or a fail is a verdict flips are counted over.
        Assert.True(HistoryFormat.IsRealVerdict('P'));
        Assert.True(HistoryFormat.IsRealVerdict('F'));
        Assert.False(HistoryFormat.IsRealVerdict('S'));
        Assert.False(HistoryFormat.IsRealVerdict('?'));
        Assert.False(HistoryFormat.IsRealVerdict('.'));
    }

    // ─── Append and read ───────────────────────────────────────

    [Fact]
    public void Append_creates_the_file_with_a_header_and_reads_it_back()
    {
        var roster = Roster("Suite", "a1b2", "c3d4");
        var result = HistoryLedgerWriter.Append(LedgerPath, roster, Run(roster, "gh:1:1", "PF"), "3.9.0");

        Assert.Equal(HistoryAppendOutcome.Appended, result.Outcome);
        var lines = File.ReadAllText(LedgerPath).Split('\n');
        Assert.Equal("{\"t\":\"header\",\"historyFormatVersion\":1,\"generator\":\"3.9.0\"}", lines[0]);
        Assert.StartsWith("{\"t\":\"roster\"", lines[1]);
        Assert.StartsWith("{\"t\":\"run\"", lines[2]);
        Assert.Equal("", lines[3]);
        Assert.DoesNotContain("\r", File.ReadAllText(LedgerPath));

        var read = HistoryLedgerReader.Read(LedgerPath, window: 50);
        Assert.Equal(HistoryReadOutcome.Read, read.Outcome);
        Assert.Single(read.Ledger!.Runs("Suite"));
        Assert.Equal("PF", read.Ledger.Runs("Suite")[0].Results);
        Assert.Equal(roster.Ids, read.Ledger.Roster(roster.Hash)!.Ids);
    }

    [Fact]
    public void A_roster_already_in_the_file_is_not_written_twice()
    {
        var roster = Roster("Suite", "a1b2");
        HistoryLedgerWriter.Append(LedgerPath, roster, Run(roster, "gh:1:1", "P"), "3.9.0");
        HistoryLedgerWriter.Append(LedgerPath, roster, Run(roster, "gh:2:1", "F"), "3.9.0");

        var lines = File.ReadAllLines(LedgerPath);
        Assert.Equal(1, lines.Count(l => l.StartsWith("{\"t\":\"roster\"", StringComparison.Ordinal)));
        Assert.Equal(2, lines.Count(l => l.StartsWith("{\"t\":\"run\"", StringComparison.Ordinal)));
    }

    [Fact]
    public void The_same_run_appended_twice_is_recorded_once()
    {
        // §6.5: idempotence on (suite, run id) covers the retry-after-ambiguity case - a writer that could
        // not tell whether its append landed. It does nothing for a lost write, which the lock covers.
        var roster = Roster("Suite", "a1b2");
        HistoryLedgerWriter.Append(LedgerPath, roster, Run(roster, "gh:1:1", "P"), "3.9.0");
        var second = HistoryLedgerWriter.Append(LedgerPath, roster, Run(roster, "gh:1:1", "P"), "3.9.0");

        Assert.Equal(HistoryAppendOutcome.Duplicate, second.Outcome);
        Assert.Single(HistoryLedgerReader.Read(LedgerPath, 50).Ledger!.Runs("Suite"));
    }

    [Fact]
    public void Two_projects_sharing_a_stableId_do_not_share_history()
    {
        // §2.3: the same run id (one CI build) and the same id (a shared Gherkin scenario) in two suites
        // are two runs of two scenarios. Scoping is by suite, and nothing is compared across them.
        var nunit = Roster("Suite.NUnit", "a1b2");
        var xunit = Roster("Suite.xUnit", "a1b2");
        HistoryLedgerWriter.Append(LedgerPath, nunit, Run(nunit, "gh:1:1", "P"), "3.9.0");
        HistoryLedgerWriter.Append(LedgerPath, xunit, Run(xunit, "gh:1:1", "F"), "3.9.0");

        var ledger = HistoryLedgerReader.Read(LedgerPath, 50).Ledger!;
        Assert.Equal("P", Assert.Single(ledger.Runs("Suite.NUnit")).Results);
        Assert.Equal("F", Assert.Single(ledger.Runs("Suite.xUnit")).Results);
        Assert.Equal(2, ledger.Suites.Count);
    }

    [Fact]
    public void Runs_are_ordered_by_append_position_not_timestamp()
    {
        // §5.13 and §5.10: clocks skew, and merge=union keeps ours-then-theirs, so a union-merged ledger
        // has non-monotonic `at`. Append position is the order; `at` is a label.
        var roster = Roster("Suite", "a1b2");
        var later = new DateTimeOffset(2026, 9, 13, 0, 0, 0, TimeSpan.Zero);
        var earlier = new DateTimeOffset(2026, 9, 12, 0, 0, 0, TimeSpan.Zero);
        HistoryLedgerWriter.Append(LedgerPath, roster, Run(roster, "gh:2:1", "F", at: later), "3.9.0");
        HistoryLedgerWriter.Append(LedgerPath, roster, Run(roster, "gh:1:1", "P", at: earlier), "3.9.0");

        var runs = HistoryLedgerReader.Read(LedgerPath, 50).Ledger!.Runs("Suite");
        Assert.Equal(["gh:2:1", "gh:1:1"], runs.Select(r => r.Id));
    }

    [Fact]
    public void Ledger_written_by_a_union_merge_is_read_in_append_order_not_timestamp_order()
    {
        // The fixture §13 asks for: a file whose lines were interleaved by git rather than by one writer -
        // rosters after runs that reference them, and `at` going backwards.
        var a = Roster("Suite", "a1b2");
        var b = Roster("Suite", "a1b2", "c3d4");
        var text = HistoryJson.HeaderLine("3.9.0") + "\n"
                   + HistoryJson.RunLine(Run(b, "gh:5:1", "PF", at: new DateTimeOffset(2026, 9, 13, 0, 0, 0, TimeSpan.Zero))) + "\n"
                   + HistoryJson.RosterLine(a) + "\n"
                   + HistoryJson.RunLine(Run(a, "gh:4:1", "P", at: new DateTimeOffset(2026, 9, 12, 0, 0, 0, TimeSpan.Zero))) + "\n"
                   + HistoryJson.RosterLine(b) + "\n";
        Directory.CreateDirectory(Path.GetDirectoryName(LedgerPath)!);
        File.WriteAllText(LedgerPath, text);

        var ledger = HistoryLedgerReader.Read(LedgerPath, 50).Ledger!;

        Assert.Equal(["gh:5:1", "gh:4:1"], ledger.Runs("Suite").Select(r => r.Id));
        Assert.NotNull(ledger.Roster(b.Hash));
        Assert.Equal(0, ledger.Stats.DamagedLines);
    }

    [Fact]
    public void Two_rosters_sharing_a_key_with_different_contents_is_a_verify_failure()
    {
        var a = Roster("Suite", "a1b2");
        var forged = HistoryJson.RosterLine(Roster("Suite", "c3d4")).Replace(Roster("Suite", "c3d4").Hash, a.Hash, StringComparison.Ordinal);
        Directory.CreateDirectory(Path.GetDirectoryName(LedgerPath)!);
        File.WriteAllText(LedgerPath, HistoryJson.HeaderLine("3.9.0") + "\n" + HistoryJson.RosterLine(a) + "\n" + forged + "\n");

        var findings = HistoryLedgerReader.Verify(LedgerPath);

        Assert.Contains(findings, f => f.Contains("roster " + a.Hash, StringComparison.Ordinal) && f.Contains("different contents", StringComparison.Ordinal));
    }

    // ─── Damage and versions ───────────────────────────────────

    [Fact]
    public void Truncated_last_line_is_skipped_not_fatal()
    {
        var roster = Roster("Suite", "a1b2");
        HistoryLedgerWriter.Append(LedgerPath, roster, Run(roster, "gh:1:1", "P"), "3.9.0");
        File.AppendAllText(LedgerPath, "{\"t\":\"run\",\"id\":\"gh:2:1\",\"suite\":\"Suite\",\"partial\":fal");

        var read = HistoryLedgerReader.Read(LedgerPath, 50);

        Assert.Equal(HistoryReadOutcome.Read, read.Outcome);
        Assert.Single(read.Ledger!.Runs("Suite"));
        Assert.Equal(1, read.Ledger.Stats.DamagedLines);
    }

    [Fact]
    public void An_append_after_a_torn_tail_starts_on_a_fresh_line()
    {
        // Belt and braces (§3.4): ~380,000 reads never observed a torn tail, but a SIGKILL mid-write is
        // the case the rule exists for, and the next append must not glue its line onto the stub.
        var roster = Roster("Suite", "a1b2");
        HistoryLedgerWriter.Append(LedgerPath, roster, Run(roster, "gh:1:1", "P"), "3.9.0");
        File.AppendAllText(LedgerPath, "{\"t\":\"run\",\"id\":\"gh:2:1\"");
        HistoryLedgerWriter.Append(LedgerPath, roster, Run(roster, "gh:3:1", "F"), "3.9.0");

        var ledger = HistoryLedgerReader.Read(LedgerPath, 50).Ledger!;
        Assert.Equal(["gh:1:1", "gh:3:1"], ledger.Runs("Suite").Select(r => r.Id));
        Assert.Equal(1, ledger.Stats.DamagedLines);
    }

    [Fact]
    public void Unknown_historyFormatVersion_is_refused_by_the_reader()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(LedgerPath)!);
        File.WriteAllText(LedgerPath, "{\"t\":\"header\",\"historyFormatVersion\":99,\"generator\":\"9.0.0\"}\n");

        var read = HistoryLedgerReader.Read(LedgerPath, 50);

        Assert.Equal(HistoryReadOutcome.UnsupportedVersion, read.Outcome);
        Assert.Null(read.Ledger);
        Assert.Contains("99", read.Message);
    }

    [Fact]
    public void Unknown_historyFormatVersion_is_not_appended_to()
    {
        // §3.5: an older writer appending a v1 line into a v2 ledger produces a file neither version reads
        // correctly, and nothing later would catch it. Refusing costs one run; appending corrupts forever.
        Directory.CreateDirectory(Path.GetDirectoryName(LedgerPath)!);
        var original = "{\"t\":\"header\",\"historyFormatVersion\":99,\"generator\":\"9.0.0\"}\n";
        File.WriteAllText(LedgerPath, original);
        var roster = Roster("Suite", "a1b2");

        var result = HistoryLedgerWriter.Append(LedgerPath, roster, Run(roster, "gh:1:1", "P"), "3.9.0");

        Assert.Equal(HistoryAppendOutcome.UnsupportedVersion, result.Outcome);
        Assert.Equal(original, File.ReadAllText(LedgerPath));
    }

    [Fact]
    public void A_missing_ledger_reads_as_no_history_rather_than_an_error()
    {
        var read = HistoryLedgerReader.Read(LedgerPath, 50);

        Assert.Equal(HistoryReadOutcome.Missing, read.Outcome);
        Assert.NotNull(read.Ledger);
        Assert.Empty(read.Ledger!.Suites);
    }

    // ─── Window ────────────────────────────────────────────────

    [Fact]
    public void Lines_parsed_is_bounded_by_the_window_while_lines_scanned_is_not()
    {
        // §2.6: an unpruned file is scanned end to end even though only the last `window` lines are parsed.
        // Parsing is the cost, so the invariant pinned is LinesParsed <= window (per suite).
        var roster = Roster("Suite", "a1b2");
        for (var i = 0; i < 30; i++)
            HistoryLedgerWriter.Append(LedgerPath, roster, Run(roster, $"gh:{i}:1", "P"), "3.9.0");

        var read = HistoryLedgerReader.Read(LedgerPath, window: 10);

        Assert.Equal(10, read.Ledger!.Runs("Suite").Count);
        Assert.Equal("gh:29:1", read.Ledger.Runs("Suite")[^1].Id);
        Assert.Equal(32, read.Ledger.Stats.LinesScanned);
        Assert.True(read.Ledger.Stats.LinesParsed <= 10 + 1, $"parsed {read.Ledger.Stats.LinesParsed} lines for a window of 10");
    }

    [Fact]
    public void The_window_is_per_suite()
    {
        var a = Roster("A", "a1b2");
        var b = Roster("B", "a1b2");
        for (var i = 0; i < 5; i++)
        {
            HistoryLedgerWriter.Append(LedgerPath, a, Run(a, $"gh:{i}:1", "P"), "3.9.0");
            HistoryLedgerWriter.Append(LedgerPath, b, Run(b, $"gh:{i}:1", "F"), "3.9.0");
        }

        var ledger = HistoryLedgerReader.Read(LedgerPath, window: 3).Ledger!;

        Assert.Equal(3, ledger.Runs("A").Count);
        Assert.Equal(3, ledger.Runs("B").Count);
    }

    // ─── Prune and compact ─────────────────────────────────────

    [Fact]
    public void Prune_keeps_the_last_window_per_suite_and_the_rosters_they_reference()
    {
        var old = Roster("Suite", "a1b2");
        var recent = Roster("Suite", "a1b2", "c3d4");
        for (var i = 0; i < 5; i++)
            HistoryLedgerWriter.Append(LedgerPath, old, Run(old, $"gh:{i}:1", "P"), "3.9.0");
        for (var i = 5; i < 8; i++)
            HistoryLedgerWriter.Append(LedgerPath, recent, Run(recent, $"gh:{i}:1", "PF"), "3.9.0");

        var pruned = HistoryLedgerWriter.Prune(LedgerPath, window: 3, "3.9.0");

        Assert.Equal(5, pruned.RunsDropped);
        var ledger = HistoryLedgerReader.Read(LedgerPath, 50).Ledger!;
        Assert.Equal(["gh:5:1", "gh:6:1", "gh:7:1"], ledger.Runs("Suite").Select(r => r.Id));
        Assert.NotNull(ledger.Roster(recent.Hash));
        Assert.Null(ledger.Roster(old.Hash));
        Assert.Equal(1, ledger.Version);
    }

    [Fact]
    public void Compact_rewrites_to_the_current_version_and_drops_error_text_outside_the_window()
    {
        var roster = Roster("Suite", "a1b2");
        for (var i = 0; i < 4; i++)
            HistoryLedgerWriter.Append(LedgerPath, roster, Run(roster, $"gh:{i}:1", "F"), "3.9.0");

        var compacted = HistoryLedgerWriter.Compact(LedgerPath, window: 2, "3.9.0");

        Assert.Equal(1, compacted.Version);
        var lines = File.ReadAllLines(LedgerPath).Where(l => l.StartsWith("{\"t\":\"run\"", StringComparison.Ordinal)).ToArray();
        Assert.Equal(4, lines.Length);
        Assert.DoesNotContain("Expected 200", lines[0]);
        Assert.DoesNotContain("Expected 200", lines[1]);
        Assert.Contains("Expected 200", lines[2]);
        Assert.Contains("Expected 200", lines[3]);
    }

    // ─── Fragments ─────────────────────────────────────────────

    [Fact]
    public void A_fragment_round_trips_and_is_folded_into_the_ledger()
    {
        var roster = Roster("Suite", "a1b2");
        var run = Run(roster, "gh:1:1", "P", partial: null);
        var json = HistoryFragment.Write(roster, run, "3.9.0");

        var fragment = HistoryFragment.Parse(json);

        Assert.Equal(1, fragment.Version);
        Assert.Equal(roster.Hash, fragment.Roster.Hash);
        Assert.Equal("gh:1:1", fragment.Run.Id);
        Assert.Null(fragment.Run.Partial);
        Assert.Contains("\n", json); // indented: a person opens the artifact
    }

    [Fact]
    public void Fold_of_eight_shard_fragments_produces_one_run()
    {
        // §5.4: a sharded build is one run written by eight processes. Fragments sharing (suite, run id)
        // fold into one run line, shards counted, roster and results concatenated in fragment order.
        var fragments = new List<HistoryFragment>();
        for (var shard = 0; shard < 8; shard++)
        {
            var shardRoster = Roster("Suite", $"id{shard}a", $"id{shard}b");
            var shardRun = Run(shardRoster, "gh:777:1", shard % 2 == 0 ? "PP" : "PF", partial: null);
            fragments.Add(new HistoryFragment(1, shardRoster, shardRun));
        }

        var folded = HistoryFold.Fold(fragments);

        var (roster, run) = Assert.Single(folded);
        Assert.Equal(8, run.Shards);
        Assert.Equal(16, roster.Count);
        Assert.Equal(16, run.Results.Length);
        Assert.Equal("PPPFPPPFPPPFPPPF", run.Results);
        Assert.Equal(16, run.Durations!.Count);
        Assert.Equal(16, run.ShapeSet!.Count);
        Assert.Equal(16, run.Errors!.Count);
        Assert.Equal(roster.Hash, run.RosterHash);
        Assert.Equal(HistoryRoster.ComputeHash(roster.Suite, roster.Ids), roster.Hash);
        Assert.Equal(4, run.Errors.Count(e => e is not null));
        Assert.All(run.Errors.Where(e => e is not null), key => Assert.True(run.ErrorText.ContainsKey(key!)));
    }

    [Fact]
    public void Fold_renumbers_slots_when_two_shards_carry_the_same_stableId()
    {
        var a = Roster("Suite", "same");
        var b = Roster("Suite", "same");
        var folded = HistoryFold.Fold([
            new HistoryFragment(1, a, Run(a, "gh:1:1", "P", partial: null)),
            new HistoryFragment(1, b, Run(b, "gh:1:1", "F", partial: null))
        ]);

        var (roster, _) = Assert.Single(folded);
        Assert.Equal([0, 1], roster.Slots);
    }

    // ── Attempts (plans/EVIDENCE_SURVIVES_A_RERUN_PLAN.md F13/F15): a retry is not a shard ──

    [Fact]
    public void Attempts_of_one_run_keep_one_position_per_scenario_and_count_the_retry()
    {
        var full = Roster("Suite", "a", "b", "c");
        var retried = Roster("Suite", "b");
        var first = new HistoryFragment(1, full, Run(full, "gh:777:1", "PFP", partial: null));
        var second = new HistoryFragment(1, retried, Run(retried, "gh:777:1", "P", partial: null, at: new DateTimeOffset(2026, 9, 12, 10, 4, 40, TimeSpan.Zero)));

        var combined = HistoryFold.Attempts([first, second]);

        Assert.Equal(["a", "b", "c"], combined.Roster.Ids);
        Assert.Equal([0, 0, 0], combined.Roster.Slots);
        Assert.Equal("PPP", combined.Run.Results);
        Assert.Equal("-2-", combined.Run.Attempts);
        Assert.Equal("Expected 200 but got 500", combined.Run.ErrorAt(1));
        Assert.Equal(second.Run.At, combined.Run.At);
        Assert.Null(combined.Run.Partial);
        Assert.Equal(1, combined.Run.Shards);
        Assert.Equal(combined.Roster.Hash, combined.Run.RosterHash);
    }

    [Fact]
    public void A_retry_that_fails_again_is_a_failure_on_its_second_attempt()
    {
        var full = Roster("Suite", "a", "b");
        var retried = Roster("Suite", "b");
        var combined = HistoryFold.Attempts([
            new HistoryFragment(1, full, Run(full, "gh:1:1", "PF", partial: null)),
            new HistoryFragment(1, retried, Run(retried, "gh:1:1", "F", partial: null))
        ]);

        Assert.Equal("PF", combined.Run.Results);
        Assert.Equal("-2", combined.Run.Attempts);
    }

    [Fact]
    public void A_second_step_that_ran_other_scenarios_adds_them_without_touching_the_first()
    {
        var stepOne = Roster("Suite", "a", "b");
        var stepTwo = Roster("Suite", "c");
        var combined = HistoryFold.Attempts([
            new HistoryFragment(1, stepOne, Run(stepOne, "gh:1:1", "PF", partial: null)),
            new HistoryFragment(1, stepTwo, Run(stepTwo, "gh:1:1", "P", partial: null))
        ]);

        Assert.Equal(["a", "b", "c"], combined.Roster.Ids);
        Assert.Equal("PFP", combined.Run.Results);
        Assert.Equal("---", combined.Run.Attempts);
    }

    // The direct-append case: a job that appends to the ledger itself meets its own earlier attempt there.

    [Fact]
    public void Amend_overlays_a_later_attempt_on_the_run_s_own_line_and_leaves_every_other_line_alone()
    {
        var full = Roster("Suite", "a", "b", "c");
        var other = Roster("Other", "x");
        HistoryLedgerWriter.Append(LedgerPath, full, Run(full, "gh:1:1", "PPP"), "3.9.0");
        HistoryLedgerWriter.Append(LedgerPath, full, Run(full, "gh:7:1", "PFP"), "3.9.0");
        HistoryLedgerWriter.Append(LedgerPath, other, Run(other, "gh:7:1", "P"), "3.9.0");
        var before = File.ReadAllLines(LedgerPath);
        var retried = Roster("Suite", "b");
        var later = new DateTimeOffset(2026, 9, 12, 10, 4, 40, TimeSpan.Zero);

        var result = HistoryLedgerWriter.Amend(LedgerPath, retried, Run(retried, "gh:7:1", "P", at: later, partial: null), "3.9.0");

        Assert.Equal(HistoryAppendOutcome.Amended, result.Outcome);
        var ledger = HistoryLedgerReader.Read(LedgerPath, 0).Ledger!;
        var line = Assert.Single(ledger.Runs("Suite"), r => r.Id == "gh:7:1");
        Assert.Equal("PPP", line.Results);
        Assert.Equal("-2-", line.Attempts);
        Assert.Equal("Expected 200 but got 500", line.ErrorAt(1));
        Assert.Equal(later, line.At);
        Assert.False(line.Partial);
        Assert.Equal(["a", "b", "c"], ledger.Roster(line.RosterHash)!.Ids);
        Assert.Equal(2, ledger.Runs("Suite").Count);
        // Everybody else's bytes, in everybody else's order.
        var after = File.ReadAllLines(LedgerPath);
        Assert.Equal(before.Length, after.Length);
        for (var i = 0; i < before.Length; i++)
            if (!before[i].Contains("\"id\":\"gh:7:1\"", StringComparison.Ordinal) || before[i].Contains("\"Other\"", StringComparison.Ordinal))
                Assert.Equal(before[i], after[i]);
        Assert.Empty(HistoryLedgerReader.Verify(LedgerPath));
    }

    [Fact]
    public void Amend_appends_a_run_the_ledger_does_not_hold_and_refuses_the_same_attempt_twice()
    {
        var roster = Roster("Suite", "a", "b");
        HistoryLedgerWriter.Append(LedgerPath, roster, Run(roster, "gh:1:1", "PP"), "3.9.0");

        var appended = HistoryLedgerWriter.Amend(LedgerPath, roster, Run(roster, "gh:2:1", "PF"), "3.9.0");
        var again = HistoryLedgerWriter.Amend(LedgerPath, roster, Run(roster, "gh:2:1", "PF"), "3.9.0");

        Assert.Equal(HistoryAppendOutcome.Appended, appended.Outcome);
        // The same `at`: the same attempt written a second time (LightBDD's formatter and the adapter both
        // reach the generator), not a retry. Overlaying it would count every scenario as retried.
        Assert.Equal(HistoryAppendOutcome.Duplicate, again.Outcome);
        Assert.Equal("--", HistoryLedgerReader.Read(LedgerPath, 0).Ledger!.Runs("Suite")[^1].Attempts);
    }

    [Fact]
    public void A_retried_run_reads_as_passed_on_retry_and_leaves_no_phantom_scenario_behind()
    {
        var full = Roster("Suite", "a", "b", "c");
        var retried = Roster("Suite", "b");
        var text = HistoryJson.HeaderLine("test") + "\n" + HistoryJson.RosterLine(full) + "\n";
        for (var n = 1; n <= 6; n++)
            text += HistoryJson.RunLine(Run(full, $"gh:{n}:1", "PPP")) + "\n";
        var combined = HistoryFold.Attempts([
            new HistoryFragment(1, full, Run(full, "gh:7:1", "PFP", partial: null)),
            new HistoryFragment(1, retried, Run(retried, "gh:7:1", "P", partial: null))
        ]);
        var ledger = HistoryLedgerReader.Parse(text, 0).Ledger!;

        var verdicts = HistoryAnalyzer.Analyse(ledger, combined.Roster, combined.Run with { Partial = false }, new HistoryAnalysisOptions());

        var b = verdicts.Find("b")!;
        Assert.Equal(HistoryVerdictKind.Flaky, b.Primary);
        Assert.Contains("passed on retry 2 in this run", b.Evidence);
        Assert.DoesNotContain(verdicts.Scenarios, s => s.Has(HistoryVerdictKind.New));
        Assert.Equal(3, verdicts.Scenarios.Count);
    }

    [Fact]
    public void Fragments_of_different_runs_or_suites_stay_separate()
    {
        var a = Roster("A", "x");
        var b = Roster("B", "x");
        var folded = HistoryFold.Fold([
            new HistoryFragment(1, a, Run(a, "gh:1:1", "P", partial: null)),
            new HistoryFragment(1, b, Run(b, "gh:1:1", "P", partial: null)),
            new HistoryFragment(1, a, Run(a, "gh:2:1", "P", partial: null))
        ]);

        Assert.Equal(3, folded.Count);
    }

    // ─── Contention ────────────────────────────────────────────

    [Fact]
    public void Append_under_contention_loses_no_run()
    {
        // §6.5: without an exclusive lock a third of concurrent appends vanish silently, on NTFS and on
        // ext4. Thirty-two writers, one line each, is the shape a repo with many test projects produces.
        var writers = Enumerable.Range(0, 32).Select(i =>
        {
            var roster = Roster($"Suite{i % 4}", "a1b2");
            return Task.Run(() => HistoryLedgerWriter.Append(LedgerPath, roster, Run(roster, $"gh:{i}:1", "P"), "3.9.0"));
        }).ToArray();
        Task.WaitAll(writers);

        Assert.All(writers, w => Assert.Equal(HistoryAppendOutcome.Appended, w.Result.Outcome));
        var ledger = HistoryLedgerReader.Read(LedgerPath, 50).Ledger!;
        Assert.Equal(32, ledger.Suites.Sum(s => ledger.Runs(s).Count));
        Assert.Equal(0, ledger.Stats.DamagedLines);
        Assert.Equal(4, File.ReadAllLines(LedgerPath).Count(l => l.StartsWith("{\"t\":\"roster\"", StringComparison.Ordinal)));
    }

    [Fact]
    public void Reader_retries_while_the_writer_holds_the_lock()
    {
        // §3.4: a reader is locked out about a tenth of the time under contention, so the retry path is
        // the common path. Hold the lock for longer than one attempt and the read must still land.
        var roster = Roster("Suite", "a1b2");
        HistoryLedgerWriter.Append(LedgerPath, roster, Run(roster, "gh:1:1", "P"), "3.9.0");

        using (var hold = new FileStream(LedgerPath, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
        {
            var reading = Task.Run(() => HistoryLedgerReader.Read(LedgerPath, 50));
            Thread.Sleep(150);
            Assert.False(reading.IsCompleted, "the reader gave up while the lock was held");
            hold.Dispose();
            var read = reading.Result;
            Assert.Equal(HistoryReadOutcome.Read, read.Outcome);
            Assert.Single(read.Ledger!.Runs("Suite"));
        }
    }

    [Fact]
    public void Reader_that_exhausts_its_budget_degrades_to_no_history_without_throwing()
    {
        var roster = Roster("Suite", "a1b2");
        HistoryLedgerWriter.Append(LedgerPath, roster, Run(roster, "gh:1:1", "P"), "3.9.0");

        using var hold = new FileStream(LedgerPath, FileMode.Open, FileAccess.ReadWrite, FileShare.None);
        var read = HistoryLedgerReader.Read(LedgerPath, 50, new HistoryLockBudget(Attempts: 3, MaxDelayMilliseconds: 5));

        Assert.Equal(HistoryReadOutcome.Locked, read.Outcome);
        Assert.Null(read.Ledger);
        Assert.Contains("locked", read.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Writer_that_exhausts_its_budget_says_so_rather_than_losing_the_run()
    {
        var roster = Roster("Suite", "a1b2");
        HistoryLedgerWriter.Append(LedgerPath, roster, Run(roster, "gh:1:1", "P"), "3.9.0");

        using var hold = new FileStream(LedgerPath, FileMode.Open, FileAccess.ReadWrite, FileShare.None);
        var result = HistoryLedgerWriter.Append(LedgerPath, roster, Run(roster, "gh:2:1", "P"), "3.9.0",
            new HistoryLockBudget(Attempts: 3, MaxDelayMilliseconds: 5));

        Assert.Equal(HistoryAppendOutcome.LockTimeout, result.Outcome);
        Assert.Equal(3, result.Attempts);
    }

    [Fact]
    public void Ledger_for_5000_scenarios_over_50_runs_stays_under_the_budget()
    {
        // What this times is the READ, on lines that carry results alone: a regression to "parse the
        // whole ledger" fails a test rather than slowly ruining everyone's test runs. It never calls the
        // analyzer, and the 65 ms and 150 ms of the history plan's §7.7 were a prototype harness's (#91):
        // the analysis is held by a count of roster reads and an allocation bound in HistoryAnalyzerTests,
        // and a real run's lines are four times these bytes (about 200 ms to read at this size).
        var ids = Enumerable.Range(0, 5000).Select(i => i.ToString("x16")).ToArray();
        var roster = Roster("Suite", ids);
        var builder = new StringBuilder();
        builder.Append(HistoryJson.HeaderLine("3.9.0")).Append('\n');
        builder.Append(HistoryJson.RosterLine(roster)).Append('\n');
        var random = new Random(7);
        for (var run = 0; run < 60; run++)
        {
            var results = new string(Enumerable.Range(0, 5000).Select(_ => random.NextDouble() < 0.04 ? 'F' : 'P').ToArray());
            builder.Append(HistoryJson.RunLine(Run(roster, $"gh:{run}:1", results))).Append('\n');
        }
        Directory.CreateDirectory(Path.GetDirectoryName(LedgerPath)!);
        File.WriteAllText(LedgerPath, builder.ToString());

        HistoryLedgerReader.Read(LedgerPath, 50); // warm
        // The fastest of three reads, not one read. The budget is here to catch a regression to "parse the
        // whole ledger", which is a property of the reader and shows in every read; a single wall-clock
        // sample in a parallel suite also measures whatever else this machine was doing, and measured
        // stalls of 3.4 s and 6 s failed a reader that was doing 100 ms of work. Three samples cannot make
        // a slow reader look fast, and one stalled sample can no longer make a fast one look slow.
        var elapsed = long.MaxValue;
        HistoryReadResult read = null!;
        for (var attempt = 0; attempt < 3; attempt++)
        {
            var watch = System.Diagnostics.Stopwatch.StartNew();
            read = HistoryLedgerReader.Read(LedgerPath, 50);
            watch.Stop();
            elapsed = Math.Min(elapsed, watch.ElapsedMilliseconds);
        }

        Assert.Equal(50, read.Ledger!.Runs("Suite").Count);
        Assert.True(read.Ledger.Stats.LinesParsed <= 51, $"parsed {read.Ledger.Stats.LinesParsed}");
        Assert.True(elapsed < 1500, $"the fastest of three reads took {elapsed} ms (budget is generous here; the measured figure is ~100 ms)");
    }
}
