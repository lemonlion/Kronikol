using System.Text.Json;
using System.Text.Json.Nodes;
using Kronikol.Query;

namespace Kronikol.Tests.Compression;

/// <summary>
/// The query engine over a compressed report (plans/REPORT_FORMAT_PLAN.md 4.4; section 5, R3 items 1, 3, 4, 5 and 11):
/// every verb answers over a run's <c>.krkl</c> as over its <c>.json</c>, payloads and headers are read from the decoded
/// copy past the file's size on disk, the copy answers after the file is replaced, and a damaged, oversized or foreign
/// file is one error naming what is wrong.
/// </summary>
public class KrklQueryTests : IDisposable
{
    private readonly string _root = Directory.CreateTempSubdirectory("kronikol-krkl").FullName;
    private readonly string _json;
    private readonly string _krkl;

    public KrklQueryTests()
    {
        _json = KrklReports.WriteJson(Path.Combine(_root, "json"), compressPayloads: true);
        _krkl = KrklReports.WriteKrkl(_json, Path.Combine(_root, "krkl"));
    }

    public void Dispose()
    {
        QueryCommand.AfterScan = null;
        try { Directory.Delete(_root, recursive: true); } catch (IOException) { } catch (UnauthorizedAccessException) { }
        GC.SuppressFinalize(this);
    }

    public static TheoryData<string> Invocations => new()
    {
        "summary", "summary --json", "scenarios", "scenarios --json", "scenarios --failed", "services", "services --json",
        "sizes --json", "failures", "failures --json", "repro", "repro s1", "steps s0", "steps s0 --json", "steps s0/1",
        "attachments s0", "attachments s0 --json", "assertions", "assertions s1", "flow s0", "flow s0 --json", "annotations s0", "values --path $.qty", "values s0 --path $.item",
        "interactions", "interactions --json", "interactions s0", "interactions --group-by service", "interactions --group-by service --sort bytes --json",
        "http s0/i0", "http s0/i0 --headers", "http s0/i0 --json", "body s0/i0", "body s0/i1 --json", "body s2/i1",
        "note s0/d0", "note s0/d0/n1", "grep widget", "grep widget --json", "trace s0/i0", "compare s0 s2", "diff s0/i1 s2/i1",
        "history",
    };

    [Theory]
    [MemberData(nameof(Invocations))]
    public void Every_verb_gives_the_same_answer_over_a_krkl_and_its_json(string invocation)
    {
        var overJson = Run(invocation, _json);
        var overKrkl = Run(invocation, _krkl);

        Assert.Equal(overJson.Exit, overKrkl.Exit);
        Assert.Equal(overJson.Output, overKrkl.Output);
        Assert.Equal(overJson.Error, overKrkl.Error);
    }

    [Theory]
    [MemberData(nameof(Invocations))]
    public void Every_json_answer_names_each_envelope_member_once_and_report_is_the_file(string invocation)
    {
        if (!invocation.Contains("--json", StringComparison.Ordinal))
            return;
        foreach (var report in new[] { _json, _krkl })
        {
            var output = new StringWriter();
            QueryCommand.Run([.. invocation.Split(' ')[..1], report, .. invocation.Split(' ')[1..]], output, new StringWriter());

            var root = JsonNode.Parse(output.ToString())!.AsObject();

            Assert.Equal(report, root["report"]!.GetValue<string>());
        }
    }

    [Fact]
    public void Diff_between_a_run_s_json_and_its_krkl_finds_nothing()
    {
        var output = new StringWriter();
        var error = new StringWriter();

        var exit = QueryCommand.Run(["diff", _json, _krkl], output, error);

        Assert.True(exit == 0, $"exit {exit}: {error}");
        Assert.DoesNotContain("removed", output.ToString(), StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("added", output.ToString(), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Summary_gives_the_size_on_disk_and_the_json_the_file_holds()
    {
        var krkl = JsonDocument.Parse(Query("summary", _krkl, "--json")).RootElement.GetProperty("run");
        var json = JsonDocument.Parse(Query("summary", _json, "--json")).RootElement.GetProperty("run");

        Assert.Equal(new FileInfo(_krkl).Length, krkl.GetProperty("sizeBytes").GetInt64());
        Assert.Equal(new FileInfo(_json).Length, krkl.GetProperty("jsonBytes").GetInt64());
        Assert.Equal(new FileInfo(_json).Length, json.GetProperty("sizeBytes").GetInt64());
        Assert.Equal(new FileInfo(_json).Length, json.GetProperty("jsonBytes").GetInt64());
        Assert.Contains(" as JSON", Query("summary", _krkl).Split('\n')[0], StringComparison.Ordinal);
        Assert.DoesNotContain(" as JSON", Query("summary", _json).Split('\n')[0], StringComparison.Ordinal);
    }

    [Fact]
    public void Headers_are_read_from_the_decoded_report_past_the_file_s_size_on_disk()
    {
        // The case of F17: offsets in the decoded JSON lie past the end of the compressed file.
        Assert.True(new FileInfo(_krkl).Length * 2 < new FileInfo(_json).Length, "the fixture compresses too little to show it");

        var http = Query("http", _krkl, $"{Placing(_krkl)}/i0", "--headers");

        Assert.Contains("x-correlation-id", http, StringComparison.Ordinal);
    }

    [Fact]
    public void A_krkl_replaced_after_the_scan_answers_from_what_was_scanned()
    {
        var other = KrklReports.WriteJson(Path.Combine(_root, "other"));
        File.WriteAllText(other, File.ReadAllText(other).Replace("x-correlation-id", "x-request-id", StringComparison.Ordinal));
        var placing = Placing(_krkl);
        QueryCommand.AfterScan = _ => KrklReports.WriteKrkl(other, Path.GetDirectoryName(_krkl)!);

        // Headers are read from the report every time, never from the index.
        var http = Query("http", _krkl, $"{placing}/i0", "--headers");

        Assert.Contains("x-correlation-id", http, StringComparison.Ordinal);
    }

    [Fact]
    public void A_json_replaced_after_the_scan_still_says_it_changed()
    {
        var other = KrklReports.WriteJson(Path.Combine(_root, "other"));
        File.WriteAllText(other, File.ReadAllText(other).Replace("x-correlation-id", "x-request-id", StringComparison.Ordinal) + "\n");
        var placing = Placing(_json);
        QueryCommand.AfterScan = _ => File.Copy(other, _json, overwrite: true);

        var (exit, _, error) = Run($"http {placing}/i0 --headers", _json);

        Assert.Equal(1, exit);
        Assert.Contains("changed while it was being read", error, StringComparison.Ordinal);
    }

    public static TheoryData<string> Damaged => new() { "truncated", "a byte flipped", "the marker claims 5 GiB", "not a report" };

    [Theory]
    [MemberData(nameof(Damaged))]
    public void A_file_that_cannot_be_read_is_one_error_from_the_query_and_from_diff(string damage)
    {
        var bytes = File.ReadAllBytes(_krkl);
        bytes = damage switch
        {
            "truncated" => bytes[..(bytes.Length / 2)],
            "a byte flipped" => Flip(bytes, bytes.Length / 2),
            "the marker claims 5 GiB" => ReportFileFormatTests.Krkl(File.ReadAllBytes(_json), claim: 5L << 30),
            _ => "PK\u0003\u0004 not a report"u8.ToArray(),
        };
        var broken = Path.Combine(_root, "broken", "TestRunReport.krkl");
        Directory.CreateDirectory(Path.GetDirectoryName(broken)!);
        File.WriteAllBytes(broken, bytes);
        var expected = damage switch
        {
            "the marker claims 5 GiB" => "4 GiB",
            "not a report" => "not a report Kronikol reads",
            _ => "damaged or truncated",
        };

        var query = Run("summary", broken);
        var diff = new StringWriter();
        var diffExit = QueryCommand.Run(["diff", _json, broken], new StringWriter(), diff);

        Assert.Equal(1, query.Exit);
        Assert.Contains(expected, query.Error, StringComparison.Ordinal);
        Assert.NotEqual(0, diffExit);
        Assert.Contains(expected, diff.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public void A_json_report_with_a_byte_order_mark_is_read()
    {
        var bom = Path.Combine(_root, "bom", "TestRunReport.json");
        Directory.CreateDirectory(Path.GetDirectoryName(bom)!);
        File.WriteAllBytes(bom, [0xEF, 0xBB, 0xBF, .. File.ReadAllBytes(_json)]);

        var placing = Placing(_json);
        Assert.Contains("leave it at the door", Query("body", bom, $"{placing}/i0"), StringComparison.Ordinal);
        Assert.Contains("x-correlation-id", Query("http", bom, $"{placing}/i0", "--headers"), StringComparison.Ordinal);
        Assert.Equal(Normalise(Query("body", _json, $"{placing}/i0"), _json), Normalise(Query("body", bom, $"{placing}/i0"), bom));
        Assert.Equal(Normalise(Query("http", _json, $"{placing}/i0", "--headers"), _json), Normalise(Query("http", bom, $"{placing}/i0", "--headers"), bom));
        Assert.Equal(Normalise(Run("sizes --json", _json).Output, _json), Normalise(Run("sizes --json", bom).Output, bom));
    }

    /// <summary>The address of the scenario that places an order: its request carries the headers and the body.</summary>
    private static string Placing(string report) =>
        JsonDocument.Parse(Query("scenarios", report, "--json")).RootElement.GetProperty("items").EnumerateArray()
            .Single(item => item.GetProperty("scenario").GetString() == "Place an order").GetProperty("address").GetString()!;

    private static string Query(string verb, string report, params string[] rest)
    {
        var output = new StringWriter();
        var error = new StringWriter();
        var exit = QueryCommand.Run([verb, report, .. rest], output, error);
        Assert.True(exit == 0, $"{verb} exited {exit}: {error}");
        return output.ToString();
    }

    private static (int Exit, string Output, string Error) Run(string invocation, string report)
    {
        var words = invocation.Split(' ');
        var output = new StringWriter();
        var error = new StringWriter();
        var exit = QueryCommand.Run([words[0], report, .. words[1..]], output, error);
        var text = Normalise(output.ToString(), report);
        if (words[0] == "summary")
            text = WithoutSizes(text, words.Contains("--json"));
        if (words[0] == "sizes")
            text = WithoutKeys(text, "bytesOnDisk", "jsonBytes");
        return (exit, text, Normalise(error.ToString(), report));
    }

    /// <summary>The report's own path and name read the same in both forms.</summary>
    private static string Normalise(string text, string report) =>
        text.Replace(Path.GetDirectoryName(report)!.Replace(@"\", @"\\"), "<dir>", StringComparison.Ordinal)
            .Replace(Path.GetDirectoryName(report)!, "<dir>", StringComparison.Ordinal)
            .Replace(Path.GetDirectoryName(report)!.Replace('\\', '/'), "<dir>", StringComparison.Ordinal)
            .Replace("TestRunReport.krkl", "TestRunReport.json", StringComparison.Ordinal);

    /// <summary>Summary's size lines, which differ between the forms by design (section 5, R3 item 1).</summary>
    private static string WithoutSizes(string text, bool json) =>
        json ? WithoutKeys(text, "sizeBytes", "jsonBytes") : string.Join('\n', text.Split('\n').Skip(1));

    private static string WithoutKeys(string text, params string[] keys)
    {
        var root = JsonNode.Parse(text)!;
        Remove(root, keys);
        return root.ToJsonString();
    }

    private static void Remove(JsonNode? node, string[] keys)
    {
        if (node is JsonObject obj)
        {
            foreach (var key in keys)
                obj.Remove(key);
            foreach (var (_, value) in obj)
                Remove(value, keys);
        }
        else if (node is JsonArray array)
            foreach (var item in array)
                Remove(item, keys);
    }

    private static byte[] Flip(byte[] bytes, int at)
    {
        var copy = (byte[])bytes.Clone();
        copy[at] ^= 0xFF;
        return copy;
    }
}
