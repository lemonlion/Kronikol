using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Kronikol.Query;
using Kronikol.Reports;
using Kronikol.Tool;

namespace Kronikol.Tests.Compression;

/// <summary>
/// Finding and gating a compressed report (plans/REPORT_FORMAT_PLAN.md 4.4; section 5, R3 items 6 and 7, and F23): every
/// lookup takes a <c>.krkl</c>, the newer of a pair wins and is named, <c>runs/</c> and <c>baseline/</c> are not
/// searched, the baseline lookup never takes a run summary, every reader takes <c>formatVersion</c> 3 and sends 4 to
/// an upgrade, and a version 3 file from another writer is read as attributed.
/// </summary>
public class KrklLookupTests : IDisposable
{
    private readonly string _root = Directory.CreateTempSubdirectory("kronikol-krkl-lookup").FullName;
    private readonly string _json;

    public KrklLookupTests() => _json = KrklReports.WriteJson(Path.Combine(_root, "source"));

    public void Dispose()
    {
        try { Directory.Delete(_root, recursive: true); } catch (IOException) { } catch (UnauthorizedAccessException) { }
        GC.SuppressFinalize(this);
    }

    [Fact]
    public void A_folder_holding_only_a_krkl_is_read()
    {
        var folder = Path.Combine(_root, "only");
        KrklReports.WriteKrkl(_json, folder);

        var (exit, output, error) = Query("summary", folder);

        Assert.True(exit == 0, error);
        Assert.Contains("TestRunReport.krkl", output, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("TestRunReport.krkl")]
    [InlineData("TestRunReport.json")]
    public void Of_a_report_written_both_ways_the_newer_is_read_and_named(string newer)
    {
        var folder = Path.Combine(_root, "both");
        var krkl = KrklReports.WriteKrkl(_json, folder);
        var json = Path.Combine(folder, "TestRunReport.json");
        File.Copy(_json, json);
        File.SetLastWriteTimeUtc(krkl, newer.EndsWith(".krkl", StringComparison.Ordinal) ? DateTime.UtcNow : DateTime.UtcNow.AddHours(-1));
        File.SetLastWriteTimeUtc(json, newer.EndsWith(".json", StringComparison.Ordinal) ? DateTime.UtcNow : DateTime.UtcNow.AddHours(-1));

        // At the top of a folder, and found by searching below one.
        foreach (var target in new[] { folder, _root })
        {
            if (target == _root)
                Directory.Delete(Path.Combine(_root, "source"), recursive: true);
            var (exit, output, error) = Query("summary", target);

            Assert.True(exit == 0, error);
            Assert.Contains(newer, output.Split('\n')[0], StringComparison.Ordinal);
            Assert.Contains($"reading {newer}, the newer", error, StringComparison.Ordinal);
        }
    }

    [Theory]
    [InlineData("TestRunReport.krkl")]
    [InlineData("TestRunReport.json")]
    public void Of_a_report_written_both_ways_the_one_its_Run_json_lists_is_read_whatever_the_times_say(string listed)
    {
        // The folder's Run.json lists what its newest run wrote. A copy or a download of the artifacts gives each file the
        // time it was written out, in no set order, so until 5.2.7 a stale file could be read as the newer.
        var folder = Path.Combine(_root, "listed");
        var krkl = KrklReports.WriteKrkl(_json, folder);
        var json = Path.Combine(folder, "TestRunReport.json");
        File.Copy(_json, json);
        new RunManifest { Run = "gh:1:1", At = DateTimeOffset.UtcNow, Files = [listed, "TestRunReport.html"] }.Write(folder);
        File.SetLastWriteTimeUtc(listed.EndsWith(".krkl", StringComparison.Ordinal) ? json : krkl, DateTime.UtcNow);
        File.SetLastWriteTimeUtc(Path.Combine(folder, listed), DateTime.UtcNow.AddHours(-1));

        // At the top of a folder, and found by searching below one.
        foreach (var target in new[] { folder, _root })
        {
            if (target == _root)
                Directory.Delete(Path.Combine(_root, "source"), recursive: true);
            var (exit, output, error) = Query("summary", target);

            Assert.True(exit == 0, error);
            Assert.Contains(listed, output.Split('\n')[0], StringComparison.Ordinal);
            Assert.Contains($"reading {listed}, the one its Run.json lists", error, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void Kept_runs_and_the_baseline_copy_are_not_searched()
    {
        var folder = Path.Combine(_root, "reports");
        KrklReports.WriteKrkl(_json, Path.Combine(folder, "runs", "gh_1_1"));
        KrklReports.WriteKrkl(_json, Path.Combine(folder, "baseline"));

        var (exit, _, error) = Query("summary", folder);

        Assert.Equal(2, exit);
        Assert.Contains("No TestRunReport.json or TestRunReport.krkl under", error, StringComparison.Ordinal);
    }

    [Fact]
    public void The_baseline_lookup_takes_a_krkl_and_never_a_run_summary()
    {
        var folder = Path.Combine(_root, "current");
        File.Copy(_json, Directory.CreateDirectory(folder).FullName + "/TestRunReport.json");
        var baseline = Path.Combine(folder, "baseline");
        KrklReports.WriteKrkl(_json, baseline, "Renamed.krkl");
        File.WriteAllText(Path.Combine(baseline, "Run.json"), "{\"run\":\"gh_1_1\"}");
        File.WriteAllText(Path.Combine(baseline, "History.run.json"), "{\"runs\":[]}");

        var (exit, output, error) = Query("diff", folder, "--baseline");

        Assert.True(exit == 0, error);
        Assert.Contains("Renamed.krkl", output + error, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void A_baseline_folder_s_one_report_is_taken_whatever_it_is_called(bool besideARunsOwnFiles)
    {
        var folder = Path.Combine(_root, besideARunsOwnFiles ? "beside" : "alone");
        File.Copy(_json, Directory.CreateDirectory(folder).FullName + "/TestRunReport.json");
        var baseline = Directory.CreateDirectory(Path.Combine(folder, "baseline")).FullName;
        File.Copy(_json, Path.Combine(baseline, "last-green.json"));
        if (besideARunsOwnFiles)
        {
            // What a copied run folder holds beside its report, none of which is one, and the report's schema.
            File.WriteAllText(Path.Combine(baseline, "Run.json"), "{\"run\":\"gh_1_1\"}");
            File.WriteAllText(Path.Combine(baseline, "History.run.json"), "{\"runs\":[]}");
            File.WriteAllText(Path.Combine(baseline, "ctrf-report.json"), "{\"results\":{}}");
            File.WriteAllText(Path.Combine(baseline, "last-green.schema.json"), "{}");
        }

        var (exit, output, error) = Query("diff", folder, "--baseline");

        Assert.True(exit == 0, $"exit {exit}: {error}");
        Assert.Contains("last-green.json", output + error, StringComparison.Ordinal);
    }

    [Fact]
    public void The_baseline_lookup_does_not_take_a_lone_run_summary_for_a_report()
    {
        var folder = Path.Combine(_root, "current");
        File.Copy(_json, Directory.CreateDirectory(folder).FullName + "/TestRunReport.json");
        Directory.CreateDirectory(Path.Combine(folder, "baseline"));
        File.WriteAllText(Path.Combine(folder, "baseline", "Run.json"), "{\"run\":\"gh_1_1\"}");

        var (exit, output, error) = Query("diff", folder, "--baseline");

        Assert.NotEqual(0, exit);
        Assert.DoesNotContain("Run.json", output + error, StringComparison.Ordinal);
    }

    public static TheoryData<string> Readers => new() { "query", "ctrf", "history gate", "merge" };

    [Theory]
    [MemberData(nameof(Readers))]
    public void Every_reader_takes_formatVersion_3_with_its_empty_keys_left_out(string reader)
    {
        var krkl = Version3("v3", 3, source: reader == "merge" ? Mergeable : _json);

        var (exit, _, error) = Read(reader, krkl);

        // The gate's 1 is its verdict on the run, which has a failing scenario; only a refusal of the file is 2 or names a version.
        Assert.True(exit == 0 || reader == "history gate" && exit == 1, $"{reader}: exit {exit}: {error}");
        Assert.DoesNotContain("formatVersion", error, StringComparison.Ordinal);
    }

    [Theory]
    [MemberData(nameof(Readers))]
    public void Every_reader_sends_formatVersion_4_to_an_upgrade(string reader)
    {
        var krkl = Version3("v4", 4, source: reader == "merge" ? Mergeable : _json);

        var (exit, _, error) = Read(reader, krkl);

        Assert.NotEqual(0, exit);
        Assert.Contains("Upgrade Kronikol", error, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(3, false)]
    [InlineData(2, true)]
    public void A_version_3_report_from_another_writer_is_read_as_attributed(int formatVersion, bool predates)
    {
        // Kronikol4J's own version, and none of the keys attribution was told by (a step path, a failure message, an
        // annotation), which version 3 leaves out when they are empty (F23). Version 2 written the same way is the
        // control: with those keys gone, only the version can say that the file is attributed.
        var krkl = Version3($"java{formatVersion}", formatVersion, kronikolVersion: "0.1.25-SNAPSHOT",
            withoutKeys: ["stepPath", "failureMessage", "annotations"]);

        var (exit, output, error) = Query("summary", krkl);

        Assert.True(exit == 0, error);
        Assert.Equal(predates, (output + error).Contains("predates step attribution", StringComparison.Ordinal));
    }

    [Fact]
    public void History_import_takes_a_krkl()
    {
        var folder = Path.Combine(_root, "artifacts");
        KrklReports.WriteKrkl(_json, Path.Combine(folder, "run-1"));
        var ledger = Path.Combine(_root, "ledger", "history.jsonl");

        var output = new StringWriter();
        var error = new StringWriter();
        var exit = HistoryCommand.Run(["import", folder, "--history", ledger], output, error, _ => null);

        Assert.True(exit == 0, $"exit {exit}: {error}");
        Assert.True(File.Exists(ledger) && File.ReadAllText(ledger).Length > 0, $"nothing imported: {output}{error}");
    }

    [Fact]
    public void Merge_finds_a_krkl_shard_when_it_sweeps_a_folder()
    {
        var shards = Path.Combine(_root, "shards");
        Directory.CreateDirectory(Path.Combine(shards, "a"));
        File.WriteAllBytes(Path.Combine(shards, "a", "TestRunReport.krkl"), ReportFileFormatTests.Krkl(File.ReadAllBytes(Mergeable)));
        var output = new StringWriter();
        var error = new StringWriter();

        var exit = MergeCommand.Run([shards, "-o", Path.Combine(_root, "merged", "TestRunReport.html")], output, error, _ => null);

        Assert.True(exit == 0, $"exit {exit}: {error}");
        Assert.Contains("TestRunReport.krkl", output.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public void Merge_takes_a_krkl_name_for_its_data_file_and_writes_the_report_beside_it()
    {
        var output = new StringWriter();
        var error = new StringWriter();

        var exit = MergeCommand.Run([Mergeable, "-o", Path.Combine(_root, "merged", "Merged.krkl")], output, error, _ => null);

        Assert.True(exit == 0, $"exit {exit}: {error}");
        Assert.True(File.Exists(Path.Combine(_root, "merged", "Merged.html")));
        Assert.False(File.Exists(Path.Combine(_root, "merged", "Merged.json")));
        var text = Encoding.UTF8.GetString(KrklWriterTests.Decode(File.ReadAllBytes(Path.Combine(_root, "merged", "Merged.krkl"))));
        Assert.Equal(3, JsonNode.Parse(text)!["formatVersion"]!.GetValue<int>());
    }

    [Fact]
    public void A_merge_of_a_compressed_shard_writes_a_compressed_file_with_every_payload_as_text()
    {
        var shard = Path.Combine(_root, "shards", "TestRunReport.krkl");
        Directory.CreateDirectory(Path.GetDirectoryName(shard)!);
        // A body long enough to be wrapped in $z, which a merge writing its data file plain would wrap.
        var document = JsonNode.Parse(File.ReadAllText(Mergeable))!;
        var body = FirstWithContent(document);
        Assert.NotNull(body);
        var longBody = new string('x', ReportPayloads.Threshold * 2);
        body!["content"] = longBody;
        File.WriteAllBytes(shard, ReportFileFormatTests.Krkl(Encoding.UTF8.GetBytes(document.ToJsonString())));
        var output = new StringWriter();
        var error = new StringWriter();

        var exit = MergeCommand.Run([shard, "-o", Path.Combine(_root, "merged", "TestRunReport.html")], output, error, _ => null);

        Assert.True(exit == 0, $"exit {exit}: {error}");
        Assert.False(File.Exists(Path.Combine(_root, "merged", "TestRunReport.json")));
        var text = Encoding.UTF8.GetString(KrklWriterTests.Decode(File.ReadAllBytes(Path.Combine(_root, "merged", "TestRunReport.krkl"))));
        Assert.DoesNotContain("\"$z\"", text, StringComparison.Ordinal);
        Assert.Contains(longBody, text, StringComparison.Ordinal);
        Assert.NotNull(JsonNode.Parse(text)!["mergeableFormatVersion"]);
    }

    [Fact]
    public void A_merge_of_plain_shards_writes_plain_json_as_before()
    {
        var output = new StringWriter();
        var error = new StringWriter();

        var exit = MergeCommand.Run([Mergeable, "-o", Path.Combine(_root, "plain", "TestRunReport.html")], output, error, _ => null);

        Assert.True(exit == 0, $"exit {exit}: {error}");
        Assert.True(File.Exists(Path.Combine(_root, "plain", "TestRunReport.json")));
        Assert.False(File.Exists(Path.Combine(_root, "plain", "TestRunReport.krkl")));
    }

    [Fact]
    public void A_shard_merge_cannot_open_is_reported_as_not_read_rather_than_not_written()
    {
        var shard = Path.Combine(_root, "locked", "TestRunReport.json");
        Directory.CreateDirectory(Path.GetDirectoryName(shard)!);
        File.Copy(Mergeable, shard);
        var output = new StringWriter();
        var error = new StringWriter();

        int exit;
        // Held open without sharing, as an antivirus or a writer still at work holds a file.
        using (new FileStream(shard, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
            exit = MergeCommand.Run([shard, "-o", Path.Combine(_root, "merged", "TestRunReport.html")], output, error, _ => null);

        Assert.Equal(1, exit);
        Assert.Contains("Could not read a report", error.ToString(), StringComparison.Ordinal);
        Assert.DoesNotContain("Could not write", error.ToString(), StringComparison.Ordinal);
    }

    public static TheoryData<string, string> DamageForEveryReader()
    {
        var data = new TheoryData<string, string>();
        foreach (var reader in new[] { "query", "ctrf", "history gate", "history import", "merge" })
            foreach (var damage in new[] { "truncated", "a byte flipped" })
                data.Add(reader, damage);
        return data;
    }

    [Theory]
    [MemberData(nameof(DamageForEveryReader))]
    public void A_damaged_krkl_is_one_error_from_every_reader(string reader, string damage)
    {
        var bytes = ReportFileFormatTests.Krkl(File.ReadAllBytes(reader == "merge" ? Mergeable : _json));
        if (damage == "truncated")
            bytes = bytes[..(bytes.Length / 2)];
        else
            bytes[bytes.Length / 2] ^= 0xFF;
        var krkl = Path.Combine(Directory.CreateDirectory(Path.Combine(_root, "damaged")).FullName, "TestRunReport.krkl");
        File.WriteAllBytes(krkl, bytes);

        var (exit, _, error) = Read(reader, krkl);

        Assert.True(exit != 0, $"{reader} read a damaged file: {error}");
        Assert.Contains("damaged or truncated", error, StringComparison.Ordinal);
        // One error, not a trace: neither the decoder's own exception nor a stack frame reaches the reader's output.
        Assert.DoesNotContain("   at ", error, StringComparison.Ordinal);
        Assert.DoesNotContain("Exception", error, StringComparison.Ordinal);
    }

    /// <summary>An empty ledger, which the gate requires to exist.</summary>
    private string Ledger()
    {
        var ledger = Path.Combine(_root, "gate.jsonl");
        File.WriteAllText(ledger, "");
        return ledger;
    }

    /// <summary>A report written with GenerateMergeableData, which merge needs.</summary>
    private static JsonObject? FirstWithContent(JsonNode? node) => node switch
    {
        JsonObject o when o["content"] is JsonValue v && v.TryGetValue<string>(out _) => o,
        JsonObject o => o.Select(p => FirstWithContent(p.Value)).FirstOrDefault(found => found is not null),
        JsonArray a => a.Select(FirstWithContent).FirstOrDefault(found => found is not null),
        _ => null,
    };

    private static string Mergeable =>Path.Combine(AppContext.BaseDirectory, "TestData", "Reports", "failing-with-steps.mergeable.json");

    private string Version3(string name, int formatVersion, string? kronikolVersion = null, string? source = null, string[]? withoutKeys = null)
    {
        var directory = Directory.CreateDirectory(Path.Combine(_root, name)).FullName;
        var path = Path.Combine(directory, "TestRunReport.krkl");
        File.WriteAllBytes(path, ReportFileFormatTests.Krkl(KrklReports.AsVersion3(source ?? _json, formatVersion, kronikolVersion, withoutKeys ?? [])));
        return path;
    }

    private (int Exit, string Output, string Error) Read(string reader, string krkl)
    {
        var output = new StringWriter();
        var error = new StringWriter();
        var exit = reader switch
        {
            "query" => QueryCommand.Run(["summary", krkl], output, error),
            "ctrf" => CtrfCommand.Run([krkl], output, error),
            "history gate" => HistoryCommand.Run(["gate", krkl, "--history", Ledger()], output, error, _ => null),
            "history import" => HistoryCommand.Run(["import", krkl, "--history", Ledger()], output, error, _ => null),
            _ => MergeCommand.Run([krkl, "-o", Path.Combine(_root, "merged", "TestRunReport.html")], output, error, _ => null),
        };
        return (exit, output.ToString(), error.ToString());
    }

    private static (int Exit, string Output, string Error) Query(string verb, string report, params string[] rest)
    {
        var output = new StringWriter();
        var error = new StringWriter();
        var exit = QueryCommand.Run([verb, report, .. rest], output, error);
        return (exit, output.ToString(), error.ToString());
    }
}
