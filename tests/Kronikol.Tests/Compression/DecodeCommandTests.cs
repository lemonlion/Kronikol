using System.IO.Compression;
using System.Text;
using System.Text.Json.Nodes;
using Kronikol.Reports;
using Kronikol.Tool;

namespace Kronikol.Tests.Compression;

/// <summary>
/// <c>kronikol decode</c> (plans/REPORT_FORMAT_PLAN.md 4.4, Q15): the JSON a report file holds, from every form the
/// reader takes, with each payload stored compressed written out as text, so a script or a redaction check reads it in
/// full; compact, or indented on request; to stdout or a file; and one error for a file it cannot read. A report page
/// (plans/HTML_COMPRESSION_PLAN.md Q9) is written as its plain page.
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

    // ---------- a report page (plans/HTML_COMPRESSION_PLAN.md R1, Q9; section 5 tests 7a and 7b) ----------

    private string Page(string name = "TestRunReport.html") =>
        Kronikol.Reports.ReportGenerator.GenerateHtmlReport(
            new[] { new DefaultDiagramsFetcher.DiagramAsCode("s1", "", "@startuml\nA -> B : GET: /api/decoded-page\n@enduml") },
            new[] { new Feature
            {
                DisplayName = "F1",
                Scenarios = [new Scenario { Id = "s1", DisplayName = "Create order", Result = ExecutionResult.Passed,
                    Steps = [new ScenarioStep { Keyword = "Given", Text = "a valid request", Status = ExecutionResult.Passed }] }]
            } },
            new DateTime(2026, 1, 2, 3, 0, 0, DateTimeKind.Utc), new DateTime(2026, 1, 2, 3, 10, 0, DateTimeKind.Utc),
            null, Path.Combine(_root, name), "Decoded Page", true,
            diagramFormat: DiagramFormat.PlantUml, plantUmlRendering: PlantUmlRendering.BrowserJs);

    [Fact]
    public void A_compressed_page_decodes_to_its_plain_page()
    {
        var page = Page();
        Assert.True(Kronikol.Reports.HtmlReportFile.IsCompressed(File.ReadAllText(page)));

        var (exit, output, error) = Decode(page);

        Assert.True(exit == 0, error);
        Assert.Equal(Kronikol.Reports.HtmlReportFile.ReadPlain(page), output);
        Assert.False(Kronikol.Reports.HtmlReportFile.IsCompressed(output));
        var source = System.Text.RegularExpressions.Regex.Match(output, "<script id=\"puml-data\" type=\"application/json\">\\{\"[^\"]+\":\"(H4sI[^\"]+)\"").Groups[1].Value;
        Assert.Contains("/api/decoded-page", Kronikol.Reports.CompressedPage.ItemText(source, attribute: false), StringComparison.Ordinal);
    }

    [Fact]
    public void Out_writes_the_plain_page_s_bytes_and_nothing_to_stdout()
    {
        var page = Page();
        var target = Path.Combine(_root, "plain.html");

        var (exit, output, error) = Decode(page, "-o", target);

        Assert.True(exit == 0, error);
        Assert.Equal("", output);
        Assert.Equal(Encoding.UTF8.GetBytes(Kronikol.Reports.HtmlReportFile.ReadPlain(page)), File.ReadAllBytes(target));
    }

    [Fact]
    public void A_plain_page_is_written_as_it_is()
    {
        var plain = Path.Combine(_root, "plain-input.html");
        File.WriteAllText(plain, Kronikol.Reports.HtmlReportFile.ReadPlain(Page()));

        var (exit, output, error) = Decode(plain);

        Assert.True(exit == 0, error);
        Assert.Equal(File.ReadAllText(plain), output);
    }

    [Fact]
    public void A_damaged_page_is_one_error_and_writes_nothing()
    {
        var page = Page();
        var text = File.ReadAllText(page);
        var frame = System.Text.RegularExpressions.Regex.Match(text, "(?<=<script id=\"kron-frame-page\"[^>]*>)[A-Za-z0-9+/=]+");
        var bytes = Convert.FromBase64String(frame.Value);
        bytes[bytes.Length / 2] ^= 0x5a;
        File.WriteAllText(page, text[..frame.Index] + Convert.ToBase64String(bytes) + text[(frame.Index + frame.Length)..]);
        var target = Path.Combine(_root, "never.html");

        var (exit, output, error) = Decode(page, "-o", target);

        Assert.Equal(1, exit);
        Assert.Equal("", output);
        Assert.Contains("is a compressed report that is damaged", error, StringComparison.Ordinal);
        Assert.False(File.Exists(target));
    }

    [Fact]
    public void Indent_is_for_the_JSON_and_refused_for_a_page()
    {
        var (exit, output, error) = Decode(Page(), "--indent");

        Assert.Equal(2, exit);
        Assert.Equal("", output);
        Assert.Contains("--indent", error, StringComparison.Ordinal);
    }

    [Fact]
    public void The_tests_reading_helper_returns_the_page_decode_writes()
    {
        var page = Page();

        Assert.Equal(Decode(page).Output, ReportHtml.Read(page));
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
