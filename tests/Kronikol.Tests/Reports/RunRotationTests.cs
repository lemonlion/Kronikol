using System.Text.Json;
using Kronikol.History;
using Kronikol.Reports;
using Kronikol.Tracking;

namespace Kronikol.Tests.Reports;

/// <summary>
/// The last N runs are kept (plans/EVIDENCE_SURVIVES_A_RERUN_PLAN.md §6, issue #80): before a run writes,
/// the run before it is moved to <c>runs/&lt;run&gt;/</c>, so a failure followed by the instinctive re-run
/// is still on disk. The numbers in the test names' comments are §6.5's.
///
/// <para><b>Every run here is given its environment.</b> The defaults depend on whether the process is on
/// CI, and this process may be — Kronikol's own pipeline runs these tests, and <c>CiDebugSectionTests</c>
/// sets <c>GITHUB_ACTIONS</c> for the whole process while it runs. So nothing reads the real environment:
/// each run goes through the generator's internal entry point with a lookup that answers
/// <c>KRONIKOL_HISTORY=off</c> and nothing else unless the test says otherwise.</para>
///
/// <para><b>"A new process" is a forgotten directory.</b> A process rotates a directory at most once —
/// a second call is the same run writing again (LightBDD's formatter and <c>kronikol ingest</c> both reach
/// the generator). The tests run in one process, so each run that stands for a new one first makes the
/// rotation forget this test's own directory, and only that one: the suite's other classes generate into
/// their own directories in parallel, and a hook that forgot everything would let them rotate twice.</para>
/// </summary>
[Collection("DiagramsFetcher")]
public class RunRotationTests : IDisposable
{
    private static readonly DateTime Day = new(2026, 9, 18, 10, 0, 0, DateTimeKind.Utc);

    private readonly string _root = Directory.CreateTempSubdirectory("kronikol-rotation").FullName;
    private int _runs;

    public RunRotationTests() => DefaultDiagramsFetcher.Reset();

    public void Dispose()
    {
        DefaultDiagramsFetcher.Reset();
        RunRotation.ForgetForTests(Reports);
        foreach (var file in Directory.EnumerateFiles(_root, "*", SearchOption.AllDirectories))
        {
            try { File.SetAttributes(file, FileAttributes.Normal); } catch { /* best effort */ }
        }
        try { Directory.Delete(_root, recursive: true); } catch (IOException) { }
        GC.SuppressFinalize(this);
    }

    // ─── Harness ───────────────────────────────────────────────

    private string Reports => Path.Combine(_root, "Reports");

    private string Runs => Path.Combine(Reports, ReportFolders.RunsFolderName);

    private string Ledger => Path.Combine(_root, "ledger", "history.jsonl");

    /// <summary>When run <paramref name="ordinal"/> (1-based) ended: a minute apart, so every run has its own second and its own local id.</summary>
    private static DateTime EndOf(int ordinal) => Day.AddMinutes(ordinal);

    /// <summary>The id a run mints when history is off and the process is not on CI.</summary>
    private static string LocalId(int ordinal) => HistoryRunBuilder.RunId(null, new DateTimeOffset(EndOf(ordinal)));

    private static string LocalName(int ordinal) => HistoryRunId.DirectoryName(LocalId(ordinal));

    private static Func<string, string?> Env(params (string Name, string Value)[] variables) =>
        name => variables.FirstOrDefault(v => v.Name == name) is { Name: not null } found ? found.Value
            : name == HistoryFormat.EnvironmentVariable ? HistoryFormat.EnvironmentOff
            : null;

    private static Func<string, string?> GitHub(string runId, string attempt = "1", params (string Name, string Value)[] more) =>
        Env([("GITHUB_ACTIONS", "true"), ("GITHUB_RUN_ID", runId), ("GITHUB_RUN_ATTEMPT", attempt), .. more]);

    private ReportConfigurationOptions Options() => new()
    {
        ReportsFolderPath = Reports,
        InternalFlowTracking = false,
        GenerateComponentDiagram = false,
        GenerateSpecificationsReport = false,
        GenerateSpecificationsData = false,
    };

    private static Feature[] Features(ExecutionResult result, params FileAttachment[] attachments) =>
    [
        new Feature
        {
            DisplayName = "Checkout",
            Scenarios =
            [
                new Scenario
                {
                    Id = "rotation-" + Guid.NewGuid().ToString("N"), DisplayName = "Pay with an expired card", Result = result,
                    ErrorMessage = result == ExecutionResult.Failed ? "Expected 200 but got 500" : null,
                    Attachments = attachments.Length == 0 ? null : attachments,
                    Steps = [new ScenarioStep { Keyword = "Then", Text = "the charge is declined", Status = result }]
                },
                new Scenario { Id = "rotation-" + Guid.NewGuid().ToString("N"), DisplayName = "Pay with a valid card", Result = ExecutionResult.Passed }
            ]
        }
    ];

    /// <summary>One run into <see cref="Reports"/>. Returns what it printed about that directory, and what it recorded.</summary>
    private (string Console, IReadOnlyList<DiagnosticEntry> Diagnostics) Run(
        ExecutionResult result = ExecutionResult.Passed,
        Action<ReportConfigurationOptions>? configure = null,
        Func<string, string?>? env = null,
        bool sameProcess = false,
        Feature[]? features = null)
    {
        var ordinal = ++_runs;
        if (!sameProcess)
            RunRotation.ForgetForTests(Reports);

        var options = Options();
        configure?.Invoke(options);
        features ??= Features(result);
        foreach (var scenario in features.SelectMany(f => f.Scenarios ?? []))
            RequestResponseLogger.LogPair(scenario.DisplayName, scenario.Id, HttpMethod.Post, new Uri("http://payments/charge"), "payments", "Test");

        var collector = new ReportDiagnosticsCollector();
        var original = Console.Out;
        var captured = new StringWriter();
        try
        {
            Console.SetOut(captured);
            DefaultDiagramsFetcher.Reset();
            using (ReportDiagnosticsScope.Begin(collector))
                ReportGenerator.CreateStandardReportsWithDiagramsInEnvironment(features, EndOf(ordinal).AddSeconds(-30), EndOf(ordinal), options, null, env ?? Env());
        }
        finally
        {
            Console.SetOut(original);
        }

        return (ConsoleLines.About(captured.ToString(), Reports), collector.Entries);
    }

    private static string[] TopLevel(string directory) =>
        Directory.GetFiles(directory).Select(f => Path.GetFileName(f)!).Order(StringComparer.Ordinal).ToArray();

    private string[] RetainedNames() =>
        Directory.Exists(Runs) ? Directory.GetDirectories(Runs).Select(d => Path.GetFileName(d)!).Order(StringComparer.Ordinal).ToArray() : [];

    private static Dictionary<string, byte[]> Snapshot(string directory, params string[] names) =>
        names.ToDictionary(n => n, n => File.ReadAllBytes(Path.Combine(directory, n)));

    private RunManifest Manifest(string? directory = null) =>
        RunManifest.TryRead(Path.Combine(directory ?? Reports, RunManifest.FileName)) ?? throw new InvalidOperationException($"no readable Run.json in {directory ?? Reports}");

    // ─── 1. The issue, as a test ───────────────────────────────

    [Fact]
    public void A_failing_run_is_still_on_disk_after_the_green_run_that_followed_it()
    {
        void History(ReportConfigurationOptions o, string id)
        {
            o.HistoryFilePath = Ledger;
            o.HistoryRunId = id;
            o.HistoryBranch = "";
        }

        Run(ExecutionResult.Failed, o => History(o, "local:first:1"));
        var kept = new[] { "TestRunReport.json", "TestRunReport.html", "Failures.md", "Failures.jsonl", HistoryFormat.FragmentFileName };
        var first = Snapshot(Reports, kept);
        Assert.Contains("# Failures — 1 of 2 scenarios", File.ReadAllText(Path.Combine(Reports, "Failures.md")));

        Run(ExecutionResult.Passed, o => History(o, "local:second:1"));

        // The first run, whole, under its own name — byte for byte what it wrote.
        var retained = Path.Combine(Runs, "local_first_1");
        Assert.Equal(["local_first_1"], RetainedNames());
        foreach (var (name, bytes) in first)
            Assert.True(bytes.AsSpan().SequenceEqual(File.ReadAllBytes(Path.Combine(retained, name))), $"{name} under runs/ is not what the first run wrote");
        Assert.Equal("local:first:1", Manifest(retained).Run);
        Assert.Equal(1, Manifest(retained).Failed);
        Assert.Equal(2, Manifest(retained).Scenarios);

        // The top level is the second run's, and only the second run's.
        Assert.Contains("# No failures", File.ReadAllText(Path.Combine(Reports, "Failures.md")));
        Assert.Equal("local:second:1", HistoryFragment.Parse(File.ReadAllText(Path.Combine(Reports, HistoryFormat.FragmentFileName))).Run.Id);
        Assert.Equal("local:second:1", Manifest().Run);
        Assert.Equal(0, Manifest().Failed);
        Assert.Equal(
            ["AGENTS.md", "CLAUDE.md", "Failures.jsonl", "Failures.md", "History.run.json", "Run.json", "TestRunReport.html", "TestRunReport.json", "TestRunReport.schema.json"],
            TopLevel(Reports));
    }

    [Fact]
    public void A_rotation_that_had_nothing_in_its_way_records_nothing()
    {
        // The first run is into a directory that does not exist yet — which is every first run — and
        // there is no previous manifest to remove from a directory that is not there.
        Assert.False(Directory.Exists(Reports));
        var (_, first) = Run(ExecutionResult.Failed);
        var (_, second) = Run(ExecutionResult.Passed);
        var (_, third) = Run(ExecutionResult.Passed, sameProcess: true);

        Assert.DoesNotContain(first.Concat(second).Concat(third), d => d.Kind is DiagnosticKind.ReportRotationFailed or DiagnosticKind.OutputFailure);
        Assert.Equal([LocalName(1)], RetainedNames());
    }

    [Fact]
    public void An_output_only_the_previous_run_wrote_goes_with_it_instead_of_staying_on_top_as_this_runs()
    {
        // Not what the issue asked for, and worth pinning: a file the newest run did not write used to
        // stay on top indefinitely, under the newest run's heading. What moves is what the previous run
        // listed, so an output that was switched off leaves with the last run that produced it.
        Run(ExecutionResult.Failed, o => { o.GenerateCtrfReport = true; o.GenerateSpecificationsMarkdown = true; });
        Run(ExecutionResult.Passed);

        Assert.False(File.Exists(Path.Combine(Reports, "ctrf-report.json")));
        Assert.False(File.Exists(Path.Combine(Reports, "Specifications.md")));
        Assert.True(File.Exists(Path.Combine(Runs, LocalName(1), "ctrf-report.json")));
        Assert.True(File.Exists(Path.Combine(Runs, LocalName(1), "Specifications.md")));
    }

    // ─── 2. The last failure survives any number of green re-runs ──

    [Fact]
    public void The_newest_failing_run_is_never_the_one_pruned()
    {
        Run(ExecutionResult.Failed, o => o.KeepRuns = 2);
        for (var i = 0; i < 4; i++)
            Run(ExecutionResult.Passed, o => o.KeepRuns = 2);

        // Runs 1–4 were rotated; the newest two are kept, and so is run 1, because it is the last one that failed.
        Assert.Equal(new[] { LocalName(1), LocalName(3), LocalName(4) }.Order(StringComparer.Ordinal).ToArray(), RetainedNames());
        Assert.Equal(1, Manifest(Path.Combine(Runs, LocalName(1))).Failed);
    }

    [Fact]
    public void A_newer_failing_run_takes_the_pin_from_an_older_one()
    {
        Run(ExecutionResult.Failed, o => o.KeepRuns = 1);
        Run(ExecutionResult.Failed, o => o.KeepRuns = 1);
        Run(ExecutionResult.Passed, o => o.KeepRuns = 1);
        Run(ExecutionResult.Passed, o => o.KeepRuns = 1);

        // Rotated: 1 (failed), 2 (failed), 3. Newest one kept (3), plus the newest failure (2). Run 1 goes.
        Assert.Equal(new[] { LocalName(2), LocalName(3) }.Order(StringComparer.Ordinal).ToArray(), RetainedNames());
    }

    // ─── 3. A discovery pass, and the same run writing again ───

    [Fact]
    public void A_pass_with_no_scenarios_rotates_nothing()
    {
        Run(ExecutionResult.Failed);
        var before = Snapshot(Reports, TopLevel(Reports));

        // xUnit v3's discovery pass reaches the generator with nothing in it, in a process of its own.
        Run(features: []);

        Assert.Empty(RetainedNames());
        Assert.Equal(before.Keys.Order(StringComparer.Ordinal).ToArray(), TopLevel(Reports));
        foreach (var (name, bytes) in before)
            Assert.True(bytes.AsSpan().SequenceEqual(File.ReadAllBytes(Path.Combine(Reports, name))), $"{name} changed");
    }

    // ─── 4 (and the rule the plan does not state). Two reports in one folder ──

    [Fact]
    public void A_differently_named_report_in_the_same_folder_is_never_rotated_by_the_other_one()
    {
        // A host that renders Checkout.* and Payments.* into one folder, from two processes. Run.json has
        // one name, so the last writer's manifest is the directory's — and the NEXT process to arrive
        // is, half the time, the other report's.
        Run(ExecutionResult.Failed, o => o.HtmlTestRunReportFileName = "Checkout");
        var checkout = Snapshot(Reports, "Checkout.html", "Checkout.json");

        // Payments arrives: the manifest on top is Checkout's CURRENT run. Rotating it would take the
        // newest Checkout report out of the top level, where its reader and its CI glob expect it.
        Run(ExecutionResult.Passed, o => o.HtmlTestRunReportFileName = "Payments");
        Assert.Empty(RetainedNames());
        foreach (var (name, bytes) in checkout)
            Assert.True(bytes.AsSpan().SequenceEqual(File.ReadAllBytes(Path.Combine(Reports, name))), $"{name} was touched by the Payments run");

        // Payments again: now the manifest is its own previous run, and that one does rotate.
        Run(ExecutionResult.Passed, o => o.HtmlTestRunReportFileName = "Payments");
        var retained = Path.Combine(Runs, LocalName(2));
        Assert.Equal([LocalName(2)], RetainedNames());
        Assert.True(File.Exists(Path.Combine(retained, "Payments.json")));
        Assert.False(File.Exists(Path.Combine(retained, "Checkout.json")), "only the files the rotated manifest lists move");
        foreach (var (name, bytes) in checkout)
            Assert.True(bytes.AsSpan().SequenceEqual(File.ReadAllBytes(Path.Combine(Reports, name))), $"{name} was touched by the second Payments run");

        // Checkout again: the manifest on top is Payments' current run. Left alone.
        Run(ExecutionResult.Passed, o => o.HtmlTestRunReportFileName = "Checkout");
        Assert.Equal([LocalName(2)], RetainedNames());
        Assert.True(File.Exists(Path.Combine(Reports, "Payments.json")), "Checkout's run rotated Payments' current report away");
        Assert.True(File.Exists(Path.Combine(Reports, "Payments.html")));
    }

    [Fact]
    public void The_agent_files_describe_the_directory_and_never_move_into_a_retained_run()
    {
        Run(ExecutionResult.Failed);
        Run(ExecutionResult.Passed);
        Run(ExecutionResult.Passed);

        Assert.Equal(2, RetainedNames().Length);
        Assert.Empty(Directory.GetFiles(Runs, "CLAUDE.md", SearchOption.AllDirectories));
        Assert.Empty(Directory.GetFiles(Runs, "AGENTS.md", SearchOption.AllDirectories));
        // Spliced in place on top, as ever (the splice rewrites line endings, so not byte-compared).
        Assert.Contains("<!-- kronikol:begin -->", File.ReadAllText(Path.Combine(Reports, "CLAUDE.md")));
        Assert.Contains("<!-- kronikol:begin -->", File.ReadAllText(Path.Combine(Reports, "AGENTS.md")));
        Assert.DoesNotContain("CLAUDE.md", Manifest().Files);
        Assert.DoesNotContain("AGENTS.md", Manifest().Files);
    }

    // ─── 5. A read-only previous report ────────────────────────

    [Fact]
    public void A_read_only_previous_report_stops_the_rotation_and_never_the_run()
    {
        Run(ExecutionResult.Failed);
        var report = Path.Combine(Reports, "TestRunReport.json");
        File.SetAttributes(report, FileAttributes.ReadOnly);

        var (console, diagnostics) = Run(ExecutionResult.Passed);

        var failure = Assert.Single(diagnostics, d => d.Kind == DiagnosticKind.ReportRotationFailed);
        Assert.Contains("TestRunReport.json", failure.Message);
        Assert.Empty(RetainedNames());
        Assert.False(Directory.Exists(Runs) && Directory.GetDirectories(Runs).Length > 0, "a staging directory was left behind");
        // The run carried on exactly as it does today: every other output replaced, the one it could not
        // replace announced (StaleOutputTests pins the pointer's half of that).
        Assert.Contains("# No failures", File.ReadAllText(Path.Combine(Reports, "Failures.md")));
        Assert.Contains("could not write TestRunReport.json", console);
        Assert.DoesNotContain("TestRunReport.json", Manifest().Files);
    }

    // ─── 9. History off ────────────────────────────────────────

    [Fact]
    public void A_run_has_a_name_when_history_is_off_and_the_rotation_uses_it()
    {
        Run(ExecutionResult.Failed);

        Assert.False(File.Exists(Path.Combine(Reports, HistoryFormat.FragmentFileName)), "history is off for this run");
        Assert.Equal(LocalId(1), Manifest().Run);
        Assert.Equal(new DateTimeOffset(EndOf(1)), Manifest().At);
        Assert.Null(Manifest().Partial);

        Run(ExecutionResult.Passed);

        Assert.Equal([LocalName(1)], RetainedNames());
        Assert.Equal(LocalId(1), Manifest(Path.Combine(Runs, LocalName(1))).Run);
        Assert.Equal(LocalId(2), Manifest().Run);
    }

    [Fact]
    public void An_id_the_host_chose_names_the_run_whether_or_not_history_is_on()
    {
        // HistoryRunId exists for the CI providers Kronikol does not detect (gitlab:$CI_PIPELINE_ID:1).
        // Switching history off must not quietly turn that run into a local one.
        Run(ExecutionResult.Passed, o => o.HistoryRunId = " gitlab:4711:1 ");

        Assert.Equal("gitlab:4711:1", Manifest().Run);
    }

    // ─── 10. Switched off ──────────────────────────────────────

    [Fact]
    public void With_keep_runs_at_zero_the_directory_is_what_it_always_was_plus_the_manifest()
    {
        Run(ExecutionResult.Failed, o => o.KeepRuns = 0);
        Run(ExecutionResult.Passed, o => o.KeepRuns = 0);

        Assert.False(Directory.Exists(Runs), "nothing is rotated at KeepRuns = 0");
        Assert.Equal(
            ["AGENTS.md", "CLAUDE.md", "Failures.jsonl", "Failures.md", "Run.json", "TestRunReport.html", "TestRunReport.json", "TestRunReport.schema.json"],
            TopLevel(Reports));
        Assert.Equal(LocalId(2), Manifest().Run);
    }

    [Theory]
    [InlineData("off")]
    [InlineData("OFF")]
    [InlineData("0")]
    public void The_environment_switches_it_off_too(string value)
    {
        Run(ExecutionResult.Failed, env: Env(("KRONIKOL_KEEP_RUNS", value)));
        Run(ExecutionResult.Passed, env: Env(("KRONIKOL_KEEP_RUNS", value)));

        Assert.False(Directory.Exists(Runs));
    }

    [Fact]
    public void The_option_beats_the_environment_and_the_environment_beats_the_default()
    {
        // Option 1 over environment off: rotates, and keeps one.
        for (var i = 0; i < 3; i++)
            Run(ExecutionResult.Passed, o => o.KeepRuns = 1, Env(("KRONIKOL_KEEP_RUNS", "off")));
        Assert.Equal([LocalName(2)], RetainedNames());

        // Environment 2 over the default of 3, from here on.
        for (var i = 0; i < 4; i++)
            Run(ExecutionResult.Passed, env: Env(("KRONIKOL_KEEP_RUNS", "2")));
        Assert.Equal(new[] { LocalName(5), LocalName(6) }.Order(StringComparer.Ordinal).ToArray(), RetainedNames());
    }

    [Fact]
    public void The_default_is_three_runs_off_ci_and_a_value_that_is_not_a_number_is_the_default()
    {
        for (var i = 0; i < 6; i++)
            Run(ExecutionResult.Passed, env: i % 2 == 0 ? Env() : Env(("KRONIKOL_KEEP_RUNS", "several")));

        Assert.Equal(new[] { LocalName(3), LocalName(4), LocalName(5) }.Order(StringComparer.Ordinal).ToArray(), RetainedNames());
    }

    [Theory]
    [InlineData(null, null, false, 3)]
    [InlineData(null, null, true, 0)]
    [InlineData(5, "off", true, 5)]
    [InlineData(0, "7", false, 0)]
    [InlineData(null, "7", true, 7)]
    [InlineData(null, " 2 ", false, 2)]
    [InlineData(null, "off", false, 0)]
    [InlineData(null, "-1", false, 3)]
    [InlineData(-4, null, false, 3)]
    [InlineData(-4, "1", false, 1)]
    public void Keep_runs_is_resolved_option_then_environment_then_whether_this_is_ci(int? option, string? variable, bool onCi, int expected)
    {
        Assert.Equal(expected, RunRotation.ResolveKeepRuns(option, name => name == "KRONIKOL_KEEP_RUNS" ? variable : null, onCi));
    }

    // ─── 11. F9: on CI a run id does not identify a report ──────

    [Fact]
    public void Two_steps_of_one_ci_run_are_both_retained_under_one_id_and_two_names()
    {
        // Three processes of workflow run 1, attempt 1 — a retry extension, or a second step running a
        // subset. One id. On CI nothing is kept by default EXCEPT the earlier attempts of this same run.
        Run(ExecutionResult.Failed, env: GitHub("1"));
        Run(ExecutionResult.Passed, env: GitHub("1"));
        Run(ExecutionResult.Passed, env: GitHub("1"));

        Assert.Equal(["gh_1_1", "gh_1_1-2"], RetainedNames());
        Assert.Equal("gh:1:1", Manifest(Path.Combine(Runs, "gh_1_1")).Run);
        Assert.Equal(1, Manifest(Path.Combine(Runs, "gh_1_1")).Failed);
        Assert.Equal("gh:1:1", Manifest(Path.Combine(Runs, "gh_1_1-2")).Run);
        Assert.Equal(0, Manifest(Path.Combine(Runs, "gh_1_1-2")).Failed);
        Assert.Equal("gh:1:1", Manifest().Run);
    }

    [Fact]
    public void A_second_call_in_one_process_is_the_same_run_writing_again()
    {
        Run(ExecutionResult.Failed);
        Run(ExecutionResult.Passed);
        Assert.Equal([LocalName(1)], RetainedNames());

        // LightBDD's formatter, then the host's own call: one process, one run. Whatever id the second
        // call mints, it does not rotate the first call's output away from under the run that wrote it.
        Run(ExecutionResult.Passed, sameProcess: true);

        Assert.Equal([LocalName(1)], RetainedNames());
        Assert.Equal(LocalId(3), Manifest().Run);
    }

    // ─── 12, 14. A directory with no manifest ──────────────────

    /// <summary>What 3.22.1 leaves: everything a run writes except the manifest.</summary>
    private void WrittenBeforeManifestsExisted(ExecutionResult result, Action<ReportConfigurationOptions>? configure = null)
    {
        Run(result, o => { o.KeepRuns = 0; configure?.Invoke(o); });
        File.Delete(Path.Combine(Reports, RunManifest.FileName));
    }

    [Fact]
    public void A_directory_written_before_manifests_existed_rotates_once_by_what_this_run_is_about_to_write()
    {
        WrittenBeforeManifestsExisted(ExecutionResult.Failed);
        File.WriteAllText(Path.Combine(Reports, "notes.txt"), "somebody's own file");
        Directory.CreateDirectory(Path.Combine(Reports, "attachments"));
        File.WriteAllText(Path.Combine(Reports, "attachments", "old.png"), "from the old run");
        var old = Snapshot(Reports, "TestRunReport.json", "TestRunReport.html", "Failures.md", "Failures.jsonl", "TestRunReport.schema.json");
        var stamp = File.GetLastWriteTimeUtc(Path.Combine(Reports, "TestRunReport.json"));

        Run(ExecutionResult.Passed);

        // Named from the report's own timestamp, since nothing else in the directory says which run it was.
        var name = HistoryRunId.DirectoryName(HistoryRunBuilder.RunId(null, new DateTimeOffset(stamp)));
        Assert.Equal([name], RetainedNames());
        var retained = Path.Combine(Runs, name);
        foreach (var (file, bytes) in old)
            Assert.True(bytes.AsSpan().SequenceEqual(File.ReadAllBytes(Path.Combine(retained, file))), $"{file} is not the old run's");

        // What the old run did not provably own stays where it was.
        Assert.True(File.Exists(Path.Combine(Reports, "notes.txt")));
        Assert.True(File.Exists(Path.Combine(Reports, "attachments", "old.png")));
        Assert.False(Directory.Exists(Path.Combine(retained, "attachments")));
        Assert.Empty(Directory.GetFiles(Runs, "CLAUDE.md", SearchOption.AllDirectories));

        // The digest it left says how it went, so it is a retained run like any other: counted, prunable,
        // and the failure it holds is pinned.
        var manifest = Manifest(retained);
        Assert.Equal(1, manifest.Failed);
        Assert.Equal(2, manifest.Scenarios);
        Assert.Equal(old.Keys.Order(StringComparer.Ordinal).ToArray(), manifest.Files.Order(StringComparer.Ordinal).ToArray());
        Assert.Equal([retained], ReportFolders.RetainedRuns(Reports).Select(r => r.Directory).ToArray());

        // Never again: the next rotation reads the manifest the run above wrote.
        Run(ExecutionResult.Passed);
        Assert.Equal(new[] { name, LocalName(2) }.Order(StringComparer.Ordinal).ToArray(), RetainedNames());
    }

    [Fact]
    public void A_fragment_in_a_directory_with_no_manifest_names_the_run()
    {
        WrittenBeforeManifestsExisted(ExecutionResult.Failed, o =>
        {
            o.HistoryFilePath = Ledger;
            o.HistoryRunId = "gh:31:2";
            o.HistoryBranch = "";
        });

        Run(ExecutionResult.Passed);

        Assert.Equal(["gh_31_2"], RetainedNames());
        var manifest = Manifest(Path.Combine(Runs, "gh_31_2"));
        Assert.Equal("gh:31:2", manifest.Run);
        Assert.Equal(new DateTimeOffset(EndOf(1)), manifest.At);
        Assert.Equal(1, manifest.Failed);
        Assert.Contains(HistoryFormat.FragmentFileName, manifest.Files);
    }

    [Fact]
    public void A_run_killed_before_its_manifest_leaves_none_and_is_kept_but_never_counted_or_pruned()
    {
        // Killed while the outputs were being written: the report is there, the digest is not, and
        // nothing says how the run went. It is moved out of the way like any other previous run, but
        // nobody wrote a Run.json for it and the rotation will not invent one.
        WrittenBeforeManifestsExisted(ExecutionResult.Failed);
        File.Delete(Path.Combine(Reports, "Failures.md"));
        File.Delete(Path.Combine(Reports, "Failures.jsonl"));
        var stamp = File.GetLastWriteTimeUtc(Path.Combine(Reports, "TestRunReport.json"));
        var name = HistoryRunId.DirectoryName(HistoryRunBuilder.RunId(null, new DateTimeOffset(stamp)));

        Run(ExecutionResult.Passed, o => o.KeepRuns = 1);

        var killed = Path.Combine(Runs, name);
        Assert.True(File.Exists(Path.Combine(killed, "TestRunReport.json")));
        Assert.False(File.Exists(Path.Combine(killed, RunManifest.FileName)));
        Assert.Equal([killed], ReportFolders.Unfinished(Reports));
        Assert.Empty(ReportFolders.RetainedRuns(Reports));

        // Three more runs at KeepRuns = 1: the killed run is neither counted nor pruned.
        for (var i = 0; i < 3; i++)
            Run(ExecutionResult.Passed, o => o.KeepRuns = 1);
        Assert.True(Directory.Exists(killed));
        Assert.Equal(new[] { name, LocalName(4) }.Order(StringComparer.Ordinal).ToArray(), RetainedNames());
    }

    [Fact]
    public void A_run_that_dies_after_its_outputs_does_not_leave_the_previous_runs_manifest_describing_them()
    {
        // Rotation off, so the previous manifest is not moved away — it has to be removed, or a run
        // that dies here leaves a Run.json saying "every output is here" over another run's outputs.
        Run(ExecutionResult.Failed, o => o.KeepRuns = 0);
        Assert.Equal(LocalId(1), Manifest().Run);

        // CiSummary.md is written after the isolated outputs and is not one of them: a directory in its
        // way throws out of the generator, which is as close to a kill as a test gets.
        Directory.CreateDirectory(Path.Combine(Reports, "CiSummary.md"));
        Assert.ThrowsAny<Exception>(() => Run(ExecutionResult.Passed, o => { o.KeepRuns = 0; o.WriteCiSummary = true; }));

        Assert.Contains("# No failures", File.ReadAllText(Path.Combine(Reports, "Failures.md")));
        Assert.False(File.Exists(Path.Combine(Reports, RunManifest.FileName)), "the first run's manifest still claims the directory");
    }

    // ─── 13. Attachments ───────────────────────────────────────

    [Fact]
    public void An_attachment_moves_with_the_run_that_made_it_and_the_newest_runs_copy_is_its_own()
    {
        var source = Path.Combine(_root, "captures");
        Directory.CreateDirectory(source);
        var shot = Path.Combine(source, "checkout_failure.png");

        File.WriteAllText(shot, "first run's pixels");
        Run(ExecutionResult.Failed, features: Features(ExecutionResult.Failed, new FileAttachment("checkout_failure.png", shot)));
        Assert.Equal(["attachments/checkout_failure.png"], Manifest().Attachments);

        File.WriteAllText(shot, "second run's pixels");
        Run(ExecutionResult.Failed, features: Features(ExecutionResult.Failed, new FileAttachment("checkout_failure.png", shot)));

        var retained = Path.Combine(Runs, LocalName(1));
        Assert.Equal("first run's pixels", File.ReadAllText(Path.Combine(retained, "attachments", "checkout_failure.png")));
        Assert.Equal("second run's pixels", File.ReadAllText(Path.Combine(Reports, "attachments", "checkout_failure.png")));

        // The retained HTML links to it relatively, so the link resolves beside the retained report.
        var html = File.ReadAllText(Path.Combine(retained, "TestRunReport.html"));
        Assert.Contains("attachments/checkout_failure.png", html);
        Assert.True(File.Exists(Path.Combine(retained, "attachments/checkout_failure.png".Replace('/', Path.DirectorySeparatorChar))));
    }

    [Fact]
    public void An_attachment_another_report_in_the_folder_may_link_to_is_copied_not_taken()
    {
        var source = Path.Combine(_root, "captures");
        Directory.CreateDirectory(source);
        var shot = Path.Combine(source, "shared.png");
        File.WriteAllText(shot, "pixels");

        Run(ExecutionResult.Failed, o => o.HtmlTestRunReportFileName = "Checkout", features: Features(ExecutionResult.Failed, new FileAttachment("shared.png", shot)));
        Run(ExecutionResult.Failed, o => o.HtmlTestRunReportFileName = "Payments", features: Features(ExecutionResult.Failed, new FileAttachment("shared.png", shot)));
        // Payments rotates its own previous run, whose manifest lists the attachment — but Checkout.html,
        // still on top and in nobody's manifest, links to attachments/shared.png as well.
        Run(ExecutionResult.Passed, o => o.HtmlTestRunReportFileName = "Payments");

        Assert.Equal([LocalName(2)], RetainedNames());
        Assert.True(File.Exists(Path.Combine(Runs, LocalName(2), "attachments", "shared.png")), "the retained run lost its attachment");
        Assert.True(File.Exists(Path.Combine(Reports, "attachments", "shared.png")), "Checkout.html's link was broken by Payments' rotation");
    }

    [Fact]
    public void Ingest_told_to_clean_the_attachments_folder_cleans_it_after_the_previous_run_has_been_kept()
    {
        // `kronikol ingest --clean-attachments` emptied attachments/ BEFORE it reached the generator —
        // the one place in the run path that deletes anything — so the run about to be retained lost
        // its screenshots a moment before they would have moved with it.
        var shot = Path.Combine(_root, "overview.png");
        var started = new DateTimeOffset(Day);
        Kronikol.Ingestion.IngestResult Ingest()
        {
            const string testId = "55555555555555555555555555555555";
            var (request, response) = Kronikol.Ingestion.InteractionRecord.Pair(testId, "overview", "GET", "http://localhost/overview", "api", "web",
                responseContent: "{}", statusCode: "200", requestTimestamp: started.AddSeconds(1), responseTimestamp: started.AddSeconds(2));
            var options = Kronikol.Ingestion.IngestPipeline.DefaultOptions();
            options.ReportsFolderPath = Reports;
            options.GenerateComponentDiagram = false;
            options.KeepRuns = 3;
            return Kronikol.Ingestion.IngestPipeline.Run(new Kronikol.Ingestion.IngestRequest
            {
                Interactions = [request, response],
                TestRecords =
                [
                    new() { Event = "start", TestId = testId, TestName = "overview renders", Timestamp = started },
                    new() { Event = "attachment", TestId = testId, Name = "overview.png", Path = shot, MediaType = "image/png", Timestamp = started.AddSeconds(3) },
                    new() { Event = "end", TestId = testId, Status = "failed", Timestamp = started.AddSeconds(5) },
                ],
                Options = options,
                CleanAttachments = true,
            });
        }

        File.WriteAllText(shot, "first run's pixels");
        Ingest();
        Directory.CreateDirectory(Path.Combine(Reports, "attachments"));
        File.WriteAllText(Path.Combine(Reports, "attachments", "stale.png"), "nobody's");

        File.WriteAllText(shot, "second run's pixels");
        started = started.AddMinutes(1);
        RunRotation.ForgetForTests(Reports);
        Ingest();

        var retained = Assert.Single(Directory.GetDirectories(Runs));
        Assert.Equal("first run's pixels", File.ReadAllText(Path.Combine(retained, "attachments", "overview.png")));
        // And the folder on top is still cleaned: exactly the second run's artefacts.
        Assert.Equal(["overview.png"], Directory.GetFiles(Path.Combine(Reports, "attachments")).Select(f => Path.GetFileName(f)!).ToArray());
        Assert.Equal("second run's pixels", File.ReadAllText(Path.Combine(Reports, "attachments", "overview.png")));
    }

    // ─── 15. The manifest lists what reached disk ──────────────

    [Fact]
    public void The_manifest_lists_every_file_the_run_wrote_and_nothing_it_did_not()
    {
        var shot = Path.Combine(_root, "shot.png");
        File.WriteAllText(shot, "pixels");

        var (_, diagnostics) = Run(ExecutionResult.Failed, o =>
        {
            o.GenerateComponentDiagram = true;
            // A component diagram IMAGE: rendered locally to a file beside its HTML, while the
            // per-scenario diagrams go inline and write nothing.
            o.PlantUmlRendering = PlantUmlRendering.Local;
            o.LocalDiagramRenderer = (_, _) => "<svg xmlns=\"http://www.w3.org/2000/svg\"></svg>"u8.ToArray();
            o.PlantUmlImageFormat = PlantUmlImageFormat.Svg;
            o.InlineSvgRendering = true;
            o.GenerateSpecificationsReport = true;
            o.GenerateSpecificationsData = true;
            o.GenerateSpecificationsMarkdown = true;
            o.GenerateCtrfReport = true;
            o.WriteCiSummary = true;
            o.DiagnosticMode = true;
        }, features: Features(ExecutionResult.Failed, new FileAttachment("shot.png", shot)));

        Assert.DoesNotContain(diagnostics, d => d.Kind == DiagnosticKind.OutputFailure);
        var manifest = Manifest();
        var onDisk = Directory.GetFiles(Reports, "*", SearchOption.AllDirectories)
            .Select(f => Path.GetRelativePath(Reports, f).Replace('\\', '/'))
            .Where(f => f is not ("CLAUDE.md" or "AGENTS.md" or "Run.json"))
            .Order(StringComparer.Ordinal).ToArray();

        Assert.Equal(onDisk, manifest.Files.Concat(manifest.Attachments).Order(StringComparer.Ordinal).ToArray());
        // Named, so that a manifest that lists nothing and a directory that holds nothing cannot agree.
        foreach (var expected in new[] { "TestRunReport.html", "TestRunReport.json", "TestRunReport.schema.json", "ComponentDiagram.html", "ComponentDiagram.svg", "CiSummary.md", "DiagnosticReport.html", "Failures.md", "Failures.jsonl", "Specifications.html", "Specifications.yml", "Specifications.md", "ctrf-report.json" })
            Assert.Contains(expected, manifest.Files);
        Assert.Equal(["attachments/shot.png"], manifest.Attachments);
        Assert.Equal(manifest.Files.Order(StringComparer.Ordinal).ToArray(), manifest.Files.ToArray());
    }

    [Fact]
    public void An_output_that_failed_is_not_in_the_manifest()
    {
        // The setup of ReportGeneratorAgentOutputsTests: a directory where the digest should go.
        Directory.CreateDirectory(Path.Combine(Reports, "Failures.md"));

        Run(ExecutionResult.Failed);

        Assert.DoesNotContain("Failures.md", Manifest().Files);
        Assert.Contains("Failures.jsonl", Manifest().Files);
        Assert.Contains("TestRunReport.json", Manifest().Files);
    }

    [Fact]
    public void A_write_that_fell_back_to_a_second_name_is_listed_under_that_name()
    {
        Directory.CreateDirectory(Reports);
        var held = Path.Combine(Reports, "Failures.md");
        File.WriteAllText(held, "# Failures — from an earlier run\n");

        using (new FileStream(held, FileMode.Open, FileAccess.Read, FileShare.None))
            Run(ExecutionResult.Failed);

        Assert.Contains("Failures2.md", Manifest().Files);
        Assert.DoesNotContain("Failures.md", Manifest().Files);
        Assert.Contains("# Failures — 1 of 2 scenarios", File.ReadAllText(Path.Combine(Reports, "Failures2.md")));
    }

    // ─── 16. A held report (Windows: a reader with read-sharing blocks the move) ──

    [Fact]
    public void A_report_held_open_by_a_reader_abandons_the_rotation_whole()
    {
        Assert.SkipUnless(OperatingSystem.IsWindows(), "only Windows refuses to move a file that a reader holds (plan §11.1b)");

        Run(ExecutionResult.Failed);
        var before = Snapshot(Reports, "TestRunReport.html", "Run.json");

        (string Console, IReadOnlyList<DiagnosticEntry> Diagnostics) second;
        // The way `kronikol query` holds it (ReportScanner.cs:73).
        using (File.OpenRead(Path.Combine(Reports, "TestRunReport.json")))
            second = Run(ExecutionResult.Passed, o => o.GenerateTestRunReport = false);

        var failure = Assert.Single(second.Diagnostics, d => d.Kind == DiagnosticKind.ReportRotationFailed);
        Assert.Contains("TestRunReport.json", failure.Message);
        // The report goes first, so nothing had moved: no retained run, no staging directory, no runs/ at all.
        Assert.False(Directory.Exists(Runs), "runs/ exists: " + string.Join(", ", Directory.Exists(Runs) ? Directory.GetFileSystemEntries(Runs) : []));
        // The second run does not write an HTML report, so the first run's is exactly where and what it was.
        Assert.True(before["TestRunReport.html"].AsSpan().SequenceEqual(File.ReadAllBytes(Path.Combine(Reports, "TestRunReport.html"))));
        // And the run completed, as it does today.
        Assert.Contains("# No failures", File.ReadAllText(Path.Combine(Reports, "Failures.md")));
        Assert.Equal(LocalId(2), Manifest().Run);
    }

    [Fact]
    public void A_later_file_held_open_puts_the_staged_files_back_where_they_were()
    {
        Assert.SkipUnless(OperatingSystem.IsWindows(), "only Windows refuses to move a file that a reader holds (plan §11.1b)");

        Run(ExecutionResult.Failed, o => o.GenerateCtrfReport = true);
        var names = TopLevel(Reports);
        var before = Snapshot(Reports, names);

        // The rotation alone, so that what it left can be read before a run overwrites it.
        RunRotation.ForgetForTests(Reports);
        var collector = new ReportDiagnosticsCollector();
        RunRotation.Outcome outcome;
        using (File.OpenRead(Path.Combine(Reports, "ctrf-report.json")))
        using (ReportDiagnosticsScope.Begin(collector))
            outcome = RunRotation.Prepare(new RunRotation.Request(Reports, "local:next:1", OnCi: false, KeepRuns: 3, "TestRunReport", "json", []));

        Assert.Null(outcome.KeptDirectory);
        var failure = Assert.Single(collector.Entries, d => d.Kind == DiagnosticKind.ReportRotationFailed);
        Assert.Contains("ctrf-report.json", failure.Message);
        Assert.False(Directory.Exists(Runs), "runs/ exists: " + string.Join(", ", Directory.Exists(Runs) ? Directory.GetFileSystemEntries(Runs) : []));
        // Everything the previous run wrote is back, byte for byte — except its Run.json, which goes
        // whenever a run is about to write over the files it describes (rotated or not).
        Assert.Equal(names.Where(n => n != RunManifest.FileName).ToArray(), TopLevel(Reports));
        foreach (var (name, bytes) in before.Where(f => f.Key != RunManifest.FileName))
            Assert.True(bytes.AsSpan().SequenceEqual(File.ReadAllBytes(Path.Combine(Reports, name))), $"{name} is not where or what it was");
    }

    [Fact]
    public void Anything_else_that_goes_wrong_is_a_diagnostic_and_the_run_is_written_as_before()
    {
        Run(ExecutionResult.Failed);
        // A FILE called runs: the staging directory cannot be created.
        File.WriteAllText(Runs, "not a directory");

        var (_, diagnostics) = Run(ExecutionResult.Passed);

        Assert.Contains(diagnostics, d => d.Kind == DiagnosticKind.ReportRotationFailed);
        Assert.Contains("# No failures", File.ReadAllText(Path.Combine(Reports, "Failures.md")));
        Assert.Equal(LocalId(2), Manifest().Run);
        Assert.Equal("not a directory", File.ReadAllText(Runs));
    }

    [Fact]
    public void A_manifest_cannot_send_the_rotation_outside_the_directory_or_at_the_agent_files()
    {
        Run(ExecutionResult.Failed);
        var outside = Path.Combine(_root, "precious.txt");
        File.WriteAllText(outside, "not Kronikol's");
        var manifest = Manifest();
        (manifest with
        {
            Files = [.. manifest.Files, "../precious.txt", outside, "CLAUDE.md", "runs", "attachments/../../precious.txt"],
            Attachments = ["../precious.txt", "attachments/../../precious.txt"]
        }).Write(Reports);

        Run(ExecutionResult.Passed);

        Assert.Equal("not Kronikol's", File.ReadAllText(outside));
        Assert.Equal([LocalName(1)], RetainedNames());
        Assert.Equal(["Failures.jsonl", "Failures.md", "Run.json", "TestRunReport.html", "TestRunReport.json", "TestRunReport.schema.json"], TopLevel(Path.Combine(Runs, LocalName(1))));
        Assert.True(File.Exists(Path.Combine(Reports, "CLAUDE.md")));
    }

    // ─── 17, and the resume rules (§11.4 row 4) ────────────────

    [Fact]
    public void A_staging_directory_nobody_can_explain_is_left_alone_and_never_counted()
    {
        var orphan = Path.Combine(Runs, ReportFolders.IncomingPrefix + "local_somebody_else");
        Directory.CreateDirectory(orphan);
        File.WriteAllText(Path.Combine(orphan, "TestRunReport.json"), "{}");

        for (var i = 0; i < 4; i++)
            Run(ExecutionResult.Passed, o => o.KeepRuns = 1);

        Assert.True(File.Exists(Path.Combine(orphan, "TestRunReport.json")));
        Assert.Equal([orphan], ReportFolders.Unfinished(Reports));
        Assert.Equal([LocalName(3)], ReportFolders.RetainedRuns(Reports).Select(r => Path.GetFileName(r.Directory)).ToArray());
    }

    [Fact]
    public void A_staging_directory_that_holds_its_manifest_was_complete_and_is_published()
    {
        // Killed between the last move and the rename: everything is staged, Run.json included.
        Run(ExecutionResult.Failed);
        var staged = Path.Combine(Runs, ReportFolders.IncomingPrefix + LocalName(1));
        Directory.CreateDirectory(staged);
        foreach (var file in Directory.GetFiles(Reports).Where(f => Path.GetFileName(f) is not ("CLAUDE.md" or "AGENTS.md")))
            File.Move(file, Path.Combine(staged, Path.GetFileName(file)));

        Run(ExecutionResult.Passed);

        Assert.Equal([LocalName(1)], RetainedNames());
        Assert.Equal(LocalId(1), Manifest(Path.Combine(Runs, LocalName(1))).Run);
        Assert.Empty(ReportFolders.Unfinished(Reports));
    }

    [Fact]
    public void A_staging_directory_the_top_level_manifest_explains_is_carried_on_into()
    {
        // Killed mid-staging: the report had moved, the rest had not, and Run.json — which goes last — is
        // still on top saying whose files these are.
        Run(ExecutionResult.Failed);
        var all = Snapshot(Reports, Manifest().Files.ToArray());
        var staged = Path.Combine(Runs, ReportFolders.IncomingPrefix + LocalName(1));
        Directory.CreateDirectory(staged);
        File.Move(Path.Combine(Reports, "TestRunReport.json"), Path.Combine(staged, "TestRunReport.json"));

        Run(ExecutionResult.Passed);

        Assert.Equal([LocalName(1)], RetainedNames());
        foreach (var (name, bytes) in all)
            Assert.True(bytes.AsSpan().SequenceEqual(File.ReadAllBytes(Path.Combine(Runs, LocalName(1), name))), $"{name} did not end up in the retained run");
        Assert.Empty(ReportFolders.Unfinished(Reports));
    }

    // ─── F13: on CI, the earlier attempts of THIS run, and nothing else ──

    [Fact]
    public void On_ci_a_run_with_another_id_is_overwritten_as_before_and_its_attempts_do_not_accumulate()
    {
        // Workflow run 7: a failing attempt, then the retry extension's process. Kept.
        Run(ExecutionResult.Failed, env: GitHub("7"));
        Run(ExecutionResult.Passed, env: GitHub("7"));
        Assert.Equal(["gh_7_1"], RetainedNames());

        // Workflow run 8 on the same persistent runner. The top level holds run 7's retry: another run,
        // not rotated at KeepRuns = 0. And run 7's attempt is nobody's earlier attempt any more.
        Run(ExecutionResult.Passed, env: GitHub("8"));

        Assert.Empty(RetainedNames());
        Assert.Equal("gh:8:1", Manifest().Run);
    }

    [Fact]
    public void On_ci_keep_runs_means_what_it_means_anywhere_else_when_it_is_set()
    {
        Run(ExecutionResult.Failed, env: GitHub("7", "1", ("KRONIKOL_KEEP_RUNS", "2")));
        Run(ExecutionResult.Passed, env: GitHub("8", "1", ("KRONIKOL_KEEP_RUNS", "2")));
        Run(ExecutionResult.Passed, env: GitHub("9", "1", ("KRONIKOL_KEEP_RUNS", "2")));

        Assert.Equal(["gh_7_1", "gh_8_1"], RetainedNames());
    }

    // ─── It says what it did ───────────────────────────────────

    [Fact]
    public void The_pointer_says_where_a_failing_previous_run_went_and_says_nothing_about_a_green_one()
    {
        Run(ExecutionResult.Failed);

        var (afterFailing, _) = Run(ExecutionResult.Passed);
        var kept = Path.Combine(Runs, LocalName(1));
        Assert.Contains($"  previous run (1 failed) kept: {kept} — kronikol query failures {Reports} --run last-failed", afterFailing.Split('\n').Select(l => l.TrimEnd('\r')));

        var (afterGreen, _) = Run(ExecutionResult.Passed);
        Assert.DoesNotContain("previous run", afterGreen);
    }

    [Fact]
    public void The_digest_of_a_retry_that_passed_says_an_earlier_attempt_of_this_run_failed()
    {
        // F13: "All 2 scenarios passed", two seconds after the failure, is the most misleading sentence
        // in the directory an agent has just been told to read first.
        Run(ExecutionResult.Failed, env: GitHub("7"));
        Run(ExecutionResult.Passed, env: GitHub("7"));

        var digest = File.ReadAllText(Path.Combine(Reports, "Failures.md"));
        Assert.StartsWith("# No failures\n\n> **An earlier attempt of this run failed**", digest);
        Assert.Contains("`runs/gh_7_1`", digest);
        Assert.Contains("1 of its 2 scenarios failed", digest);

        // A different run's failure is not this run's earlier attempt.
        Run(ExecutionResult.Failed, env: GitHub("8", "1", ("KRONIKOL_KEEP_RUNS", "3")));
        Run(ExecutionResult.Passed, env: GitHub("9", "1", ("KRONIKOL_KEEP_RUNS", "3")));
        Assert.DoesNotContain("earlier attempt", File.ReadAllText(Path.Combine(Reports, "Failures.md")));
    }

    [Fact]
    public void The_digest_of_a_green_re_run_says_the_run_before_it_failed_and_where_it_went()
    {
        // #80 as it happens locally: a different run, not a retry. `dotnet test` swallows the console
        // line, so "# No failures" with nothing under it was all a reader of this directory was told -
        // found by doing it: a failing run of an example project, then a filtered green one.
        Run(ExecutionResult.Failed);
        Run(ExecutionResult.Passed);

        var digest = File.ReadAllText(Path.Combine(Reports, "Failures.md"));
        Assert.StartsWith("# No failures\n\n> **The run before this one failed**", digest);
        Assert.Contains("1 of its 2 scenarios failed", digest);
        Assert.Contains($"`runs/{LocalName(1)}`", digest);
        Assert.Contains("`kronikol query failures . --run last-failed`", digest);
        Assert.DoesNotContain("earlier attempt", digest);

        // Said once, by the run that replaced it: the next green run has a green run before it.
        Run(ExecutionResult.Passed);
        Assert.DoesNotContain("The run before this one", File.ReadAllText(Path.Combine(Reports, "Failures.md")));
    }

    [Fact]
    public void A_retry_that_fails_again_says_so_above_its_own_failures()
    {
        Run(ExecutionResult.Failed, env: GitHub("7"));
        Run(ExecutionResult.Failed, env: GitHub("7"));

        var digest = File.ReadAllText(Path.Combine(Reports, "Failures.md"));
        Assert.StartsWith("# Failures — 1 of 2 scenarios\n\n> **An earlier attempt of this run failed**", digest);
        Assert.Contains("`runs/gh_7_1`", digest);
    }
}
