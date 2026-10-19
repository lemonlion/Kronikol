using System.IO.Compression;
using System.Text;
using System.Text.Json.Nodes;
using Kronikol.Tool;

namespace Kronikol.Tests.Compression;

/// <summary>
/// <c>kronikol decode</c> (plans/REPORT_FORMAT_PLAN.md 4.4, Q15): the JSON a report file holds, from every form the
/// reader takes, with each payload stored compressed written out as text, so a script or a redaction check reads it in
/// full; compact, or indented on request; to stdout or a file; and one error for a file it cannot read.
/// </summary>
public class DecodeCommandTests : IDisposable
{
    private readonly string _root = Directory.CreateTempSubdirectory("kronikol-decode").FullName;
    private readonly string _json;
    private readonly string _krkl;

    public DecodeCommandTests()
    {
        _json = KrklReports.WriteJson(Path.Combine(_root, "json"));
        _krkl = KrklReports.WriteKrkl(_json, Path.Combine(_root, "krkl"));
    }

    public void Dispose()
    {
        try { Directory.Delete(_root, recursive: true); } catch (IOException) { } catch (UnauthorizedAccessException) { }
        GC.SuppressFinalize(this);
    }

    [Fact]
    public void A_krkl_decodes_to_the_JSON_it_holds()
    {
        var (exit, output, error) = Decode(_krkl);

        Assert.True(exit == 0, error);
        Assert.True(JsonNode.DeepEquals(JsonNode.Parse(File.ReadAllText(_json)), JsonNode.Parse(output)));
    }

    [Fact]
    public void A_folder_is_resolved_as_the_query_resolves_it()
    {
        var (exit, output, error) = Decode(Path.GetDirectoryName(_krkl)!);

        Assert.True(exit == 0, error);
        Assert.True(JsonNode.DeepEquals(JsonNode.Parse(File.ReadAllText(_json)), JsonNode.Parse(output)));
    }

    [Fact]
    public void A_payload_stored_compressed_is_written_out_as_text_whatever_the_reads_return()
    {
        var text = string.Concat(Enumerable.Range(0, 2_000).Select(i => $"line {i} of a captured body\n"));
        var json = $$"""{"formatVersion":2,"features":[],"body":{"$h":"b:00000000","$n":{{text.Length}},"$z":"{{Gzip(text)}}"},"after":[1,2.50,true,null,"é"]}""";

        foreach (var trickle in new[] { int.MaxValue, 5, 1 })
        {
            using var decoded = new MemoryStream();
            DecodeCommand.Rewrite(new TrickleStream(Encoding.UTF8.GetBytes(json), trickle), decoded);

            var root = JsonNode.Parse(decoded.ToArray())!;
            Assert.Equal(text, root["body"]!.GetValue<string>());
            Assert.Equal("[1,2.50,true,null,\"é\"]", root["after"]!.ToJsonString(new System.Text.Json.JsonSerializerOptions { Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping }));
        }
    }

    [Fact]
    public void An_object_that_only_looks_like_a_payload_is_kept()
    {
        const string json = """{"features":[],"a":{"$h":"x"},"b":{"$z":"not base64 but not a wrapper either","other":1}}""";
        using var decoded = new MemoryStream();

        DecodeCommand.Rewrite(new MemoryStream(Encoding.UTF8.GetBytes(json)), decoded);

        Assert.True(JsonNode.DeepEquals(JsonNode.Parse(json), JsonNode.Parse(decoded.ToArray())));
    }

    [Fact]
    public void Indent_and_out_write_an_indented_file_and_nothing_to_stdout()
    {
        var target = Path.Combine(_root, "decoded.json");

        var (exit, output, error) = Decode(_krkl, "--indent", "-o", target);

        Assert.True(exit == 0, error);
        Assert.Equal("", output);
        Assert.Contains("\n  \"", File.ReadAllText(target), StringComparison.Ordinal);
        Assert.True(JsonNode.DeepEquals(JsonNode.Parse(File.ReadAllText(_json)), JsonNode.Parse(File.ReadAllText(target))));
    }

    [Fact]
    public void A_damaged_file_is_one_error_and_no_output()
    {
        var bytes = File.ReadAllBytes(_krkl);
        var broken = Path.Combine(_root, "broken.krkl");
        File.WriteAllBytes(broken, bytes[..(bytes.Length / 2)]);

        var (exit, _, error) = Decode(broken);

        Assert.Equal(1, exit);
        Assert.Contains("damaged or truncated", error, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("")]
    [InlineData("--bogus")]
    [InlineData("a.krkl b.krkl")]
    [InlineData("-o")]
    public void A_command_it_cannot_take_exits_2(string args)
    {
        Assert.Equal(2, DecodeCommand.Run(args.Split(' ', StringSplitOptions.RemoveEmptyEntries), new StringWriter(), new StringWriter()));
    }

    [Fact]
    public void It_will_not_write_over_the_report_it_reads()
    {
        var (exit, _, error) = Decode(_krkl, "-o", _krkl);

        Assert.Equal(2, exit);
        Assert.Contains("is the report being decoded", error, StringComparison.Ordinal);
        Assert.Equal(KrklReportsBytes(), File.ReadAllBytes(_krkl));
    }

    private byte[] KrklReportsBytes() => ReportFileFormatTests.Krkl(File.ReadAllBytes(_json));

    private static (int Exit, string Output, string Error) Decode(params string[] args)
    {
        var output = new StringWriter();
        var error = new StringWriter();
        var exit = DecodeCommand.Run(args, output, error);
        return (exit, output.ToString(), error.ToString());
    }

    private static string Gzip(string text)
    {
        using var compressed = new MemoryStream();
        using (var gzip = new GZipStream(compressed, CompressionLevel.Optimal, leaveOpen: true))
            gzip.Write(Encoding.UTF8.GetBytes(text));
        return Convert.ToBase64String(compressed.ToArray());
    }

    /// <summary>A stream that hands over at most <paramref name="most"/> bytes a read, as a slow pipe or decoder can.</summary>
    private sealed class TrickleStream(byte[] bytes, int most) : MemoryStream(bytes)
    {
        public override int Read(byte[] buffer, int offset, int count) => base.Read(buffer, offset, Math.Min(count, most));
        public override int Read(Span<byte> buffer) => base.Read(buffer[..Math.Min(buffer.Length, most)]);
    }
}
