using System.Text.Json;
using Kronikol.History;
using Kronikol.Reports;

namespace Kronikol.Tests.Reports;

/// <summary>
/// The three small pieces the retained-runs layout stands on (plans/EVIDENCE_SURVIVES_A_RERUN_PLAN.md §6):
/// the name a run id gets as a directory, the <c>Run.json</c> a run writes last, and the helper every
/// sweep and every reader of <c>runs/</c> asks instead of walking the folder itself. None of them touches
/// the generator, so none of them needs the DiagramsFetcher collection.
/// </summary>
public class RunManifestTests : IDisposable
{
    private readonly string _dir = Directory.CreateTempSubdirectory("kronikol-run-manifest").FullName;

    public void Dispose()
    {
        try { Directory.Delete(_dir, recursive: true); } catch (IOException) { }
        GC.SuppressFinalize(this);
    }

    // ─── F7: a run id is not a directory name ──────────────────

    [Theory]
    [InlineData("local:20260918T101611Z:ab12cd34", "local_20260918T101611Z_ab12cd34")]
    [InlineData("gh:18273645:1", "gh_18273645_1")]
    [InlineData("ado:20260918.4:1", "ado_20260918.4_1")]
    [InlineData("gitlab:$CI_PIPELINE_ID:1", "gitlab__CI_PIPELINE_ID_1")]
    public void A_run_id_becomes_the_same_directory_name_on_every_operating_system(string id, string expected)
    {
        // Path.GetInvalidFileNameChars is 41 characters on Windows and 2 on Linux (plan §11.5 row 6), so
        // the repo's own sanitising idiom names one run two ways. An allow-list cannot.
        Assert.Equal(expected, HistoryRunId.DirectoryName(id));
    }

    [Theory]
    [InlineData(@"..\..\etc/passwd?*<>|""", "___.._etc_passwd______")]
    [InlineData("", "_")]
    [InlineData("   ", "___")]
    [InlineData(".", "_")]
    [InlineData("..", "__")]
    [InlineData("...", "___")]
    [InlineData(".incoming-gh_1_1", "_incoming-gh_1_1")]
    [InlineData("release.", "release_")]
    [InlineData("CON", "_CON")]
    [InlineData("nul.txt", "_nul.txt")]
    [InlineData("com7", "_com7")]
    [InlineData("ünï:cödé", "_n__c_d_")]
    public void A_hostile_id_cannot_name_a_parent_a_staging_directory_or_a_device(string id, string expected)
    {
        var name = HistoryRunId.DirectoryName(id);

        Assert.Equal(expected, name);
        // Whatever the table above says, the properties are what the rotation relies on: one path
        // segment, never the staging prefix, never a name Windows resolves to something else.
        Assert.DoesNotContain(Path.DirectorySeparatorChar, name);
        Assert.DoesNotContain(Path.AltDirectorySeparatorChar, name);
        Assert.False(name.StartsWith('.'), name);
        Assert.False(name.EndsWith('.'), name);
        Assert.Equal(Path.Combine(_dir, name), Path.GetFullPath(Path.Combine(_dir, name)));
    }

    [Fact]
    public void A_very_long_id_is_cut_and_two_long_ids_with_one_beginning_stay_two_names()
    {
        var first = HistoryRunId.DirectoryName("custom:" + new string('a', 400) + ":1");
        var second = HistoryRunId.DirectoryName("custom:" + new string('a', 400) + ":2");

        Assert.True(first.Length <= 96, $"{first.Length} characters");
        Assert.NotEqual(first, second);
    }

    // ─── Run.json ──────────────────────────────────────────────

    private static RunManifest Sample() => new()
    {
        Run = "local:20260918T101611Z:ab12cd34",
        At = new DateTimeOffset(2026, 9, 18, 10, 16, 11, TimeSpan.Zero),
        Suite = "Checkout",
        Scenarios = 396,
        Failed = 1,
        Partial = null,
        KronikolVersion = "3.23.0",
        Files = ["TestRunReport.html", "TestRunReport.json", "Failures.md"],
        Attachments = ["attachments/checkout_failure.png"]
    };

    [Fact]
    public void The_manifest_is_written_with_exactly_the_members_the_plan_names()
    {
        var path = Sample().Write(_dir);

        Assert.Equal(Path.Combine(_dir, "Run.json"), path);
        Assert.Equal("Run.json", RunManifest.FileName);
        using var json = JsonDocument.Parse(File.ReadAllText(path));
        Assert.Equal(
            ["runManifestVersion", "run", "at", "suite", "scenarios", "failed", "partial", "kronikolVersion", "files", "attachments"],
            json.RootElement.EnumerateObject().Select(p => p.Name).ToArray());
        Assert.Equal(1, json.RootElement.GetProperty("runManifestVersion").GetInt32());
        Assert.Equal("2026-09-18T10:16:11Z", json.RootElement.GetProperty("at").GetString());
        Assert.Equal(JsonValueKind.Null, json.RootElement.GetProperty("partial").ValueKind);
        Assert.Equal("attachments/checkout_failure.png", json.RootElement.GetProperty("attachments")[0].GetString());
    }

    [Fact]
    public void A_manifest_reads_back_as_it_was_written()
    {
        var read = RunManifest.TryRead(Sample().Write(_dir));

        Assert.NotNull(read);
        Assert.Equal("local:20260918T101611Z:ab12cd34", read.Run);
        Assert.Equal(new DateTimeOffset(2026, 9, 18, 10, 16, 11, TimeSpan.Zero), read.At);
        Assert.Equal("Checkout", read.Suite);
        Assert.Equal(396, read.Scenarios);
        Assert.Equal(1, read.Failed);
        Assert.Null(read.Partial);
        Assert.Equal("3.23.0", read.KronikolVersion);
        Assert.Equal(["TestRunReport.html", "TestRunReport.json", "Failures.md"], read.Files);
        Assert.Equal(["attachments/checkout_failure.png"], read.Attachments);
    }

    [Fact]
    public void A_manifest_is_found_from_the_directory_that_holds_it_too()
    {
        Sample().Write(_dir);

        Assert.Equal("local:20260918T101611Z:ab12cd34", RunManifest.TryRead(_dir)?.Run);
    }

    [Theory]
    [InlineData("")]
    [InlineData("not json")]
    [InlineData("[]")]
    [InlineData("{}")]
    [InlineData("{\"runManifestVersion\":1,\"run\":\"\",\"at\":\"2026-09-18T10:16:11Z\"}")]
    [InlineData("{\"runManifestVersion\":1,\"run\":7,\"at\":\"2026-09-18T10:16:11Z\"}")]
    [InlineData("{\"runManifestVersion\":1,\"run\":\"gh:1:1\",\"at\":\"yesterday\"}")]
    [InlineData("{\"runManifestVersion\":1,\"run\":\"gh:1:1\",\"at\":\"2026-09-18T10:16:11Z\",\"files\":\"TestRunReport.json\"}")]
    public void Anything_unreadable_is_no_manifest_rather_than_an_exception(string content)
    {
        var path = Path.Combine(_dir, RunManifest.FileName);
        File.WriteAllText(path, content);

        Assert.Null(RunManifest.TryRead(path));
    }

    [Fact]
    public void A_missing_file_and_a_held_file_are_no_manifest_either()
    {
        Assert.Null(RunManifest.TryRead(Path.Combine(_dir, "nowhere", RunManifest.FileName)));

        var path = Sample().Write(_dir);
        using (new FileStream(path, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
            Assert.Null(RunManifest.TryRead(path));
    }

    [Fact]
    public void Members_a_later_version_adds_are_ignored_and_the_ones_it_leaves_out_take_their_defaults()
    {
        var path = Path.Combine(_dir, RunManifest.FileName);
        File.WriteAllText(path, "{\"runManifestVersion\":2,\"run\":\"gh:1:1\",\"at\":\"2026-09-18T10:16:11Z\",\"shards\":[1,2]}");

        var read = RunManifest.TryRead(path);

        Assert.NotNull(read);
        Assert.Equal(2, read.RunManifestVersion);
        Assert.Equal("gh:1:1", read.Run);
        Assert.Equal(0, read.Failed);
        Assert.Empty(read.Files);
        Assert.Empty(read.Attachments);
    }

    // ─── ReportFolders ─────────────────────────────────────────

    [Theory]
    [InlineData("TestRunReport.json", false)]
    [InlineData("runs/gh_1_1/TestRunReport.json", true)]
    [InlineData("Runs/gh_1_1/TestRunReport.json", true)]
    [InlineData("baseline/TestRunReport.json", true)]
    [InlineData("BASELINE/TestRunReport.json", true)]
    [InlineData("Shard1/Reports/runs/.incoming-gh_1_1/TestRunReport.json", true)]
    [InlineData("reruns/TestRunReport.json", false)]
    [InlineData("runs.json", false)]
    [InlineData("baseline-2/TestRunReport.json", false)]
    public void A_file_under_a_runs_or_a_baseline_folder_is_reserved_wherever_the_sweep_started(string relative, bool reserved)
    {
        var file = Path.Combine(_dir, relative.Replace('/', Path.DirectorySeparatorChar));

        Assert.Equal(reserved, ReportFolders.IsReserved(_dir, file));
    }

    [Fact]
    public void A_retained_run_that_is_named_rather_than_found_is_not_reserved()
    {
        // The exemption is for what a sweep finds, never for what it is given (plan §6.3):
        // `kronikol merge Reports/runs/gh_1_1` must still read that run.
        var given = Path.Combine(_dir, "runs", "gh_1_1");

        Assert.False(ReportFolders.IsReserved(given, Path.Combine(given, "TestRunReport.json")));
        // Nor does a `runs` segment ABOVE the root count: that is somebody's own folder name.
        Assert.False(ReportFolders.IsReserved(Path.Combine(_dir, "runs", "nightly", "Reports"), Path.Combine(_dir, "runs", "nightly", "Reports", "TestRunReport.json")));
    }

    private string Retain(string name, string runId, DateTimeOffset at, int failed = 0)
    {
        var directory = Path.Combine(_dir, ReportFolders.RunsFolderName, name);
        Directory.CreateDirectory(directory);
        new RunManifest { Run = runId, At = at, Scenarios = 3, Failed = failed, Files = ["TestRunReport.json"] }.Write(directory);
        File.WriteAllText(Path.Combine(directory, "TestRunReport.json"), "{}");
        return directory;
    }

    [Fact]
    public void Retained_runs_are_the_directories_with_a_manifest_newest_first()
    {
        var day = new DateTimeOffset(2026, 9, 18, 10, 0, 0, TimeSpan.Zero);
        var oldest = Retain("local_a", "local:a", day);
        var newest = Retain("local_c", "local:c", day.AddHours(2), failed: 2);
        var middle = Retain("local_b", "local:b", day.AddHours(1));

        var retained = ReportFolders.RetainedRuns(_dir);

        Assert.Equal([newest, middle, oldest], retained.Select(r => r.Directory).ToArray());
        Assert.Equal(["local:c", "local:b", "local:a"], retained.Select(r => r.Manifest.Run).ToArray());
        Assert.Equal(2, retained[0].Manifest.Failed);
    }

    [Fact]
    public void Two_runs_of_one_second_are_ordered_by_the_suffix_the_second_one_took()
    {
        var at = new DateTimeOffset(2026, 9, 18, 10, 0, 0, TimeSpan.Zero);
        Retain("gh_1_1", "gh:1:1", at);
        Retain("gh_1_1-2", "gh:1:1", at);

        Assert.Equal(["gh_1_1-2", "gh_1_1"], ReportFolders.RetainedRuns(_dir).Select(r => Path.GetFileName(r.Directory)).ToArray());
    }

    [Fact]
    public void A_staging_directory_and_a_directory_without_a_manifest_are_unfinished_not_retained()
    {
        // §6.5 test 17, the library's part: what --run offers and what pruning counts both come from
        // RetainedRuns, and what doctor names comes from Unfinished.
        var day = new DateTimeOffset(2026, 9, 18, 10, 0, 0, TimeSpan.Zero);
        var whole = Retain("local_a", "local:a", day);
        var staging = Retain(ReportFolders.IncomingPrefix + "local_b", "local:b", day.AddHours(1));
        var killed = Path.Combine(_dir, ReportFolders.RunsFolderName, "local_c");
        Directory.CreateDirectory(killed);
        File.WriteAllText(Path.Combine(killed, "TestRunReport.json"), "{}");
        var damaged = Path.Combine(_dir, ReportFolders.RunsFolderName, "local_d");
        Directory.CreateDirectory(damaged);
        File.WriteAllText(Path.Combine(damaged, RunManifest.FileName), "{ torn");

        Assert.Equal([whole], ReportFolders.RetainedRuns(_dir).Select(r => r.Directory).ToArray());
        Assert.Equal(new[] { staging, killed, damaged }.Order().ToArray(), ReportFolders.Unfinished(_dir).Order().ToArray());
    }

    [Fact]
    public void A_reports_directory_with_no_runs_folder_has_nothing_retained_and_nothing_unfinished()
    {
        Assert.Empty(ReportFolders.RetainedRuns(_dir));
        Assert.Empty(ReportFolders.Unfinished(_dir));
        Assert.Empty(ReportFolders.RetainedRuns(Path.Combine(_dir, "never-written")));
        Assert.Empty(ReportFolders.Unfinished(Path.Combine(_dir, "never-written")));
    }
}
