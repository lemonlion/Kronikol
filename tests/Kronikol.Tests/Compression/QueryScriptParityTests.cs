using Kronikol.Query;

namespace Kronikol.Tests.Compression;

/// <summary>
/// The skill's fallback script, <c>query.py</c>, reads and refuses exactly the files the tool does (plans/REPORT_FORMAT_PLAN.md
/// 4.4; section 5, R3 item 9): gzip on any Python, zstd from 3.14 on, and on an older one a message naming the tool and
/// <c>query.cs</c> in place of an answer.
/// </summary>
public class QueryScriptParityTests : IDisposable
{
    private static readonly string RepoRoot = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", ".."));
    private static readonly string Script = Path.Combine(RepoRoot, "templates", "skills", "kronikol-test-debugging", "scripts", "query.py");
    private static readonly string Fixtures = Path.Combine(AppContext.BaseDirectory, "TestData", "Compression");
    private static readonly byte[] Json = File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "TestData", "Reports", "failing-with-steps.mergeable.json"));

    private readonly string _root = Directory.CreateTempSubdirectory("kronikol-parity").FullName;

    public void Dispose()
    {
        try { Directory.Delete(_root, recursive: true); } catch (IOException) { } catch (UnauthorizedAccessException) { }
        GC.SuppressFinalize(this);
    }

    public static TheoryData<string> Inputs => new()
    {
        "json", "json with a byte order mark", "json after whitespace", "krkl", "libzstd frame", "Java gzip",
        "a skippable frame, then zstd", "krkl truncated", "krkl with a byte flipped", "gzip truncated", "gzip with its CRC flipped",
        "a zip", "an empty file", "krkl of container version 2", "krkl claiming 5 GiB", "krkl understating its length",
    };

    [Theory]
    [MemberData(nameof(Inputs))]
    public void The_script_reads_and_refuses_what_the_tool_does(string input)
    {
        Assert.SkipWhen(!PythonProbe.IsAvailable, "no python on PATH");
        var path = Path.Combine(_root, "TestRunReport" + (input.StartsWith("json", StringComparison.Ordinal) ? ".json" : ".krkl"));
        File.WriteAllBytes(path, Bytes(input));

        var tool = QueryCommand.Run(["summary", path], new StringWriter(), new StringWriter());
        var (_, error, script) = PythonProbe.RunFull(Script, "summary", path);

        if (error.Contains("Python reads from 3.14 on", StringComparison.Ordinal))
        {
            // An older Python cannot read zstd: it says so and where to turn, and never answers wrongly.
            Assert.NotEqual(0, script);
            Assert.Contains("kronikol query", error, StringComparison.Ordinal);
            Assert.Contains("query.cs", error, StringComparison.Ordinal);
            return;
        }
        Assert.True((tool == 0) == (script == 0), $"{input}: the tool exited {tool}, the script {script}: {error}");
    }

    [Fact]
    public void A_folder_holding_a_krkl_is_found_by_the_script_as_by_the_tool()
    {
        Assert.SkipWhen(!PythonProbe.IsAvailable, "no python on PATH");
        var folder = Path.Combine(_root, "Reports", "bin");
        Directory.CreateDirectory(folder);
        File.Copy(Path.Combine(Fixtures, "failing-with-steps.json.gz"), Path.Combine(folder, "TestRunReport.krkl"));
        File.WriteAllBytes(Path.Combine(Directory.CreateDirectory(Path.Combine(_root, "Reports", "runs", "gh_1_1")).FullName, "TestRunReport.krkl"), Bytes("krkl"));

        var (output, error, exit) = PythonProbe.RunFull(Script, "summary", Path.Combine(_root, "Reports"));

        Assert.True(exit == 0, error);
        Assert.Contains("TestRunReport.krkl", output, StringComparison.Ordinal);
    }

    private static byte[] Bytes(string input) => input switch
    {
        "json" => Json,
        "json with a byte order mark" => [0xEF, 0xBB, 0xBF, .. Json],
        "json after whitespace" => [.. "\n  "u8, .. Json],
        "krkl" => ReportFileFormatTests.Krkl(Json),
        "libzstd frame" => File.ReadAllBytes(Path.Combine(Fixtures, "failing-with-steps.json.zst")),
        "Java gzip" => File.ReadAllBytes(Path.Combine(Fixtures, "failing-with-steps.json.gz")),
        "a skippable frame, then zstd" => [0x53, 0x2A, 0x4D, 0x18, 2, 0, 0, 0, (byte)'h', (byte)'i', .. ReportFileFormatTests.Zstd(Json)],
        "krkl truncated" => ReportFileFormatTests.Krkl(Json)[..2_000],
        "krkl with a byte flipped" => Flip(ReportFileFormatTests.Krkl(Json), 3_000),
        "gzip truncated" => File.ReadAllBytes(Path.Combine(Fixtures, "failing-with-steps.json.gz"))[..5_000],
        "gzip with its CRC flipped" => Flip(File.ReadAllBytes(Path.Combine(Fixtures, "failing-with-steps.json.gz")), -8),
        "a zip" => "PK\u0003\u0004 not a report"u8.ToArray(),
        "an empty file" => [],
        "krkl of container version 2" => ReportFileFormatTests.Krkl(Json, version: 2),
        "krkl claiming 5 GiB" => ReportFileFormatTests.Krkl(Json, claim: 5L << 30),
        "krkl understating its length" => ReportFileFormatTests.Krkl(Json, claim: Json.Length - 10),
        _ => throw new ArgumentOutOfRangeException(nameof(input)),
    };

    private static byte[] Flip(byte[] bytes, int at)
    {
        var copy = (byte[])bytes.Clone();
        copy[at < 0 ? copy.Length + at : at] ^= 0xFF;
        return copy;
    }
}
