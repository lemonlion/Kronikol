using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Kronikol.InternalFlow;
using Kronikol.Reports;

namespace Kronikol.Tests.Reports;

/// <summary>
/// The compressed report page (plans/HTML_COMPRESSION_PLAN.md R1, section 5 tests 1 to 7f): <c>TestRunReport.html</c> and
/// <c>Specifications.html</c> are a loader page holding the report as zstd frames, with every item raw inside the page
/// frame and each diagram source a token read from the diagram sources' pieces, and decoding it gives back the plain page.
/// </summary>
public class CompressedPageTests
{
    private const string PayloadNeedle = "zqxfrobwidget-7734";
    private const string MessageNeedle = "vlurpqzt-endpoint";

    private static Feature[] TwoScenarioFeature() =>
    [
        new Feature
        {
            DisplayName = "F1",
            Scenarios =
            [
                new Scenario
                {
                    Id = "s1", DisplayName = "Create order", Result = ExecutionResult.Passed,
                    Steps = [new ScenarioStep { Keyword = "Given", Text = "a valid request", Status = ExecutionResult.Passed }]
                },
                new Scenario
                {
                    Id = "s2", DisplayName = "Cancel order", Result = ExecutionResult.Passed,
                    Steps = [new ScenarioStep { Keyword = "Given", Text = "an existing order", Status = ExecutionResult.Passed }]
                }
            ]
        }
    ];

    private static DefaultDiagramsFetcher.DiagramAsCode[] NeedleDiagrams() =>
    [
        new("s1", "", "@startuml\nA -> B : POST: /api/" + MessageNeedle + "\nnote left\n{\n  \"widget\": \"" + PayloadNeedle + "\"\n}\nend note\n@enduml"),
        new("s2", "", "@startuml\nA -> B : GET: /api/other\n@enduml")
    ];

    private static string Generate(string fileName, DefaultDiagramsFetcher.DiagramAsCode[]? diagrams = null, Feature[]? features = null,
        PlantUmlRendering rendering = PlantUmlRendering.BrowserJs, bool includeTestRunData = true, bool fullSearchIndex = true,
        string? componentDiagram = null, Dictionary<string, Kronikol.Reports.Merge.WholeTestFlowFragment>? precomputed = null,
        WholeTestFlowVisualization visualization = WholeTestFlowVisualization.None) =>
        ReportGenerator.GenerateHtmlReport(
            diagrams ?? NeedleDiagrams(), features ?? TwoScenarioFeature(),
            new DateTime(2026, 1, 2, 3, 0, 0, DateTimeKind.Utc), new DateTime(2026, 1, 2, 3, 10, 0, DateTimeKind.Utc),
            null, fileName, "Compressed Test", includeTestRunData,
            diagramFormat: DiagramFormat.PlantUml, plantUmlRendering: rendering, fullSearchIndex: fullSearchIndex,
            componentDiagramPlantUml: componentDiagram, precomputedWholeTestContent: precomputed,
            internalFlowTracking: precomputed is not null, wholeTestVisualization: visualization);

    // ---------- the file (test 2) ----------

    [Fact]
    public void The_report_page_is_a_loader_page_with_the_report_in_zstd_frames()
    {
        var text = File.ReadAllText(Generate("CompressedPage_Shape.html"));

        Assert.True(HtmlReportFile.IsCompressed(text));
        Assert.StartsWith("<!DOCTYPE html>", text, StringComparison.Ordinal);
        Assert.Contains("<meta name=\"kronikol-page\" content=\"frames 1\" />", text, StringComparison.Ordinal);
        Assert.Contains("<title>Compressed Test</title>", text, StringComparison.Ordinal);
        Assert.Contains("<script id=\"kronikol-theme-boot\">", text, StringComparison.Ordinal);
        Assert.Contains("<div id=\"kron-loading\" role=\"status\" aria-live=\"polite\">", text, StringComparison.Ordinal);
        Assert.Contains("class=\"kron-loading-hint\"", text, StringComparison.Ordinal);
        Assert.Contains("<noscript>", text, StringComparison.Ordinal);
        Assert.Contains("kronikol decode CompressedPage_Shape.html", text, StringComparison.Ordinal);
        Assert.Matches("<script id=\"kron-frame-page\" type=\"application/octet-stream\" data-codec=\"zstd\" data-length=\"[0-9]+\">[A-Za-z0-9+/=]+</script>", text);
        Assert.Single(Regex.Matches(text, "<script id=\"kron-frame-diagrams-1\" data-kron-piece=\"1\" type=\"application/octet-stream\" data-codec=\"zstd\" data-length=\"[0-9]+\">"));
        Assert.Contains("<script id=\"kron-diagram-index\" type=\"application/json\">", text, StringComparison.Ordinal);
        Assert.Contains("<script id=\"kron-zstd\">", text, StringComparison.Ordinal);
        Assert.Contains("<script id=\"kron-loader\">", text, StringComparison.Ordinal);
        // Nothing of the report outside its frames: no scenario, no control, no item.
        Assert.DoesNotContain("Create order", text, StringComparison.Ordinal);
        Assert.DoesNotContain("id=\"searchbar\"", text, StringComparison.Ordinal);
        Assert.DoesNotContain("id=\"puml-data\"", text, StringComparison.Ordinal);
        Assert.DoesNotContain(MessageNeedle, text, StringComparison.Ordinal);
    }

    [Fact]
    public void Each_frame_records_the_length_it_decodes_to()
    {
        var text = File.ReadAllText(Generate("CompressedPage_Lengths.html"));
        var frames = CompressedPage.Read(text);
        var page = Regex.Match(text, "<script id=\"kron-frame-page\"[^>]* data-length=\"([0-9]+)\">");
        Assert.Equal(int.Parse(page.Groups[1].Value), Encoding.UTF8.GetByteCount(frames.Page));
        Assert.Equal(2, frames.DiagramCount);
    }

    [Fact]
    public void The_loading_screen_names_the_file_and_not_the_folder_it_was_written_in()
    {
        // A report is read on another machine than the one that wrote it, so the hint gives what a reader types there.
        var folder = Directory.CreateTempSubdirectory("kronikol-hint-folder").FullName;
        try
        {
            var text = File.ReadAllText(Generate(Path.Combine(folder, "TestRunReport.html")));

            Assert.Contains("<code>kronikol decode TestRunReport.html -o plain.html</code>", text, StringComparison.Ordinal);
            Assert.DoesNotContain(System.Net.WebUtility.HtmlEncode(folder), text, StringComparison.Ordinal);
            Assert.DoesNotContain("kronikol-hint-folder", text, StringComparison.Ordinal);
        }
        finally
        {
            try { Directory.Delete(folder, recursive: true); } catch (IOException) { } catch (UnauthorizedAccessException) { }
        }
    }

    [Fact]
    public void A_page_frame_stored_as_it_is_decodes_as_the_export_writes_it_where_the_browser_cannot_compress()
    {
        // A browser without CompressionStream exports the page frame stored, data-codec="none".
        var text = File.ReadAllText(Generate("CompressedPage_Stored.html"));
        var bytes = Encoding.UTF8.GetBytes(CompressedPage.Read(text).Page);
        var stored = Regex.Replace(text, "<script id=\"kron-frame-page\" type=\"application/octet-stream\" data-codec=\"zstd\" data-length=\"[0-9]+\">[A-Za-z0-9+/=]*</script>",
            _ => $"<script id=\"kron-frame-page\" type=\"application/octet-stream\" data-codec=\"none\" data-length=\"{bytes.Length}\">{Convert.ToBase64String(bytes)}</script>");

        Assert.NotEqual(text, stored);
        Assert.Equal(HtmlReportFile.ToPlain(text), HtmlReportFile.ToPlain(stored));
    }

    // ---------- the frames' headers (test 3) ----------

    private static (bool SingleSegment, long? Window, long? ContentSize, bool Checksum) Header(byte[] frame)
    {
        Assert.Equal(0xFD2FB528u, BinaryPrimitives.ReadUInt32LittleEndian(frame));
        var descriptor = frame[4];
        var single = (descriptor & 0x20) != 0;
        var pos = 5;
        long? window = null;
        if (!single)
        {
            var wd = frame[pos++];
            var windowBase = 1L << (10 + (wd >> 3));
            window = windowBase + windowBase / 8 * (wd & 7);
        }
        pos += (descriptor & 3) switch { 0 => 0, 1 => 1, 2 => 2, _ => 4 };
        var sizeBytes = (descriptor >> 6) switch { 0 => single ? 1 : 0, 1 => 2, 2 => 4, _ => 8 };
        long? size = sizeBytes switch
        {
            0 => null,
            1 => frame[pos],
            2 => BinaryPrimitives.ReadUInt16LittleEndian(frame.AsSpan(pos)) + 256,
            4 => BinaryPrimitives.ReadUInt32LittleEndian(frame.AsSpan(pos)),
            _ => (long)BinaryPrimitives.ReadUInt64LittleEndian(frame.AsSpan(pos))
        };
        return (single, window, size, (descriptor & 0x04) != 0);
    }

    [Fact]
    public void A_frame_over_8_MB_has_a_window_of_exactly_8_MB_its_content_size_and_its_checksum()
    {
        // RFC 9659's cap for a native decoder, and well inside fzstd's 2^25 back-reference limit; level 9's own window is
        // 2^22, so a writer that left it to the level fails here, as does one that set 2^27.
        var content = new byte[9 << 20];
        new Random(7).NextBytes(content.AsSpan(0, 1 << 20));
        for (var i = 1 << 20; i < content.Length; i++) content[i] = content[i % (1 << 20)];
        var header = Header(CompressedPage.Compress(content));
        Assert.False(header.SingleSegment);
        Assert.Equal(1L << 23, header.Window);
        Assert.Equal(content.Length, header.ContentSize);
        Assert.True(header.Checksum);
    }

    [Fact]
    public void A_smaller_frame_is_one_segment_with_its_content_size_and_its_checksum()
    {
        var text = File.ReadAllText(Generate("CompressedPage_Header.html"));
        var m = Regex.Match(text, "<script id=\"kron-frame-page\"[^>]* data-length=\"([0-9]+)\">([A-Za-z0-9+/=]+)</script>");
        var header = Header(Convert.FromBase64String(m.Groups[2].Value));
        Assert.True(header.SingleSegment);
        Assert.Equal(long.Parse(m.Groups[1].Value), header.ContentSize);
        Assert.True(header.Checksum);
    }

    // ---------- the round trip (tests 4, 6, 7d) ----------

    private static Dictionary<string, string> JsonMap(string page, string id)
    {
        var m = Regex.Match(page, $"<script id=\"{id}\" type=\"application/json\">([^<]*)</script>");
        return m.Success ? JsonSerializer.Deserialize<Dictionary<string, string>>(m.Groups[1].Value)! : [];
    }

    private static string Gunzip(string base64) => InternalFlowHtmlGenerator.DecompressFromBase64(base64);

    [Fact]
    public void Decoding_gives_every_item_back_and_the_search_index_unchanged()
    {
        var text = File.ReadAllText(Generate("CompressedPage_RoundTrip.html", componentDiagram: "@startuml\n[Orders] --> [Stock]\n@enduml"));
        var frames = CompressedPage.Read(text);
        var plain = HtmlReportFile.ToPlain(text);

        var tokens = JsonMap(frames.Page, "puml-data");
        var gzipped = JsonMap(plain, "puml-data");
        Assert.Equal(tokens.Keys, gzipped.Keys);
        Assert.Equal(3, tokens.Count);
        foreach (var (id, token) in tokens)
        {
            Assert.True(CompressedPage.IsToken(token), $"{id} holds {token}");
            Assert.Equal(frames.Diagram(int.Parse(token[1..])), Gunzip(gzipped[id]));
        }
        Assert.Contains(gzipped.Values, v => Gunzip(v) == NeedleDiagrams()[0].CodeBehind);
        Assert.Contains(gzipped.Values, v => Gunzip(v) == NeedleDiagrams()[1].CodeBehind);
        Assert.Contains(gzipped.Values, v => Gunzip(v).Contains("[Orders] --> [Stock]", StringComparison.Ordinal));
        Assert.Equal(SearchIndexReportTests.ExtractBlobBase64(frames.Page), SearchIndexReportTests.ExtractBlobBase64(plain));
        Assert.NotNull(SearchIndexReportTests.ExtractBlobBase64(plain));
        // The page frame holds no gzip but the search index.
        Assert.Single(Regex.Matches(frames.Page, "H4sI[A-Za-z0-9+/]{20,}"));
    }

    [Fact]
    public void A_merged_page_s_stored_fragments_are_raw_in_the_frame_and_gzip_again_when_decoded()
    {
        var flameJson = "{\"s\":[\"Zorblatt.ActivitySource\"],\"f\":[[0,\"qmxspan-frobnicate\",0,100,0,5]],\"m\":[[0,\"GET: /api/qwyjibo-marker\"]]}";
        var activity = "@startuml\nstart\n:qmxactivity \"quoted\" <a&b>;\nstop\n@enduml";
        var flameHtml = $"<div class=\"iflow-flame\" data-diagram-type=\"flamechart\" data-flame-z=\"{InternalFlowHtmlGenerator.CompressToBase64(flameJson)}\"></div>";
        var activityHtml = $"<div class=\"plantuml-browser iflow-diagram\" id=\"iflow-puml-whole-s1\" data-plantuml-z=\"{InternalFlowHtmlGenerator.CompressToBase64(activity)}\" data-diagram-type=\"plantuml\"></div>";
        var precomputed = new Dictionary<string, Kronikol.Reports.Merge.WholeTestFlowFragment> { ["s1"] = new(activityHtml, flameHtml, 1) };

        var text = File.ReadAllText(Generate("CompressedPage_Merged.html", precomputed: precomputed, visualization: WholeTestFlowVisualization.Both));
        var frames = CompressedPage.Read(text);
        var plain = HtmlReportFile.ToPlain(text);

        Assert.Contains("data-flame-z=\"" + CompressedPage.EscapeAttribute(flameJson) + "\"", frames.Page, StringComparison.Ordinal);
        Assert.Contains("data-plantuml-z=\"" + CompressedPage.EscapeAttribute(activity) + "\"", frames.Page, StringComparison.Ordinal);
        Assert.Contains(flameHtml, plain, StringComparison.Ordinal);
        Assert.Contains(activityHtml, plain, StringComparison.Ordinal);
        Assert.Single(Regex.Matches(frames.Page, "H4sI[A-Za-z0-9+/]{20,}"));
        // The search index reads the fragments' text as before.
        var index = SearchIndexReportTests.DecodeBlob(SearchIndexReportTests.ExtractBlobBase64(plain)!);
        Assert.Contains(0, index.Candidates("qmxspan-frobnicate"));
        Assert.Contains(0, index.Candidates("qmxactivity"));
    }

    [Fact]
    public void Every_token_resolves_to_its_own_source_across_pieces_and_a_large_source_has_a_piece_of_its_own()
    {
        var sources = Enumerable.Range(0, 40).Select(i => $"@startuml\nA -> B : call {i} {new string((char)('a' + i % 26), 20 + i * 7)}\n@enduml").ToList();
        sources[17] = "@startuml\n" + new string('x', 900) + "\n@enduml";
        var diagrams = sources.Select((s, i) => new DefaultDiagramsFetcher.DiagramAsCode(i % 2 == 0 ? "s1" : "s2", "", s)).ToArray();
        string text;
        using (CompressedPage.WithPieceBytes(400))
            text = File.ReadAllText(Generate("CompressedPage_Pieces.html", diagrams, fullSearchIndex: false));

        var frames = CompressedPage.Read(text);
        var index = JsonSerializer.Deserialize<int[][]>(Regex.Match(text, "<script id=\"kron-diagram-index\" type=\"application/json\">([^<]*)</script>").Groups[1].Value)!;
        Assert.True(index.Length > 5, $"{index.Length} pieces");
        Assert.All(index, piece => Assert.True(piece.Length == 1 || piece.Sum() <= 400, string.Join(",", piece)));
        Assert.Contains(index, piece => piece is [var only] && only > 400);
        var tokens = JsonMap(frames.Page, "puml-data");
        var bySource = tokens.Values.Select(t => frames.Diagram(int.Parse(t[1..]))).ToList();
        Assert.Equal(sources.OrderBy(s => s, StringComparer.Ordinal), bySource.OrderBy(s => s, StringComparer.Ordinal));
        Assert.Equal(sources.Count, frames.DiagramCount);
    }

    [Fact]
    public void The_specifications_page_is_compressed_too_and_decodes()
    {
        var text = File.ReadAllText(Generate("CompressedPage_Specifications.html", includeTestRunData: false));
        Assert.True(HtmlReportFile.IsCompressed(text));
        var plain = HtmlReportFile.ToPlain(text);
        Assert.Contains("Create order", plain, StringComparison.Ordinal);
        Assert.Contains(JsonMap(plain, "puml-data").Values, v => Gunzip(v) == NeedleDiagrams()[0].CodeBehind);
    }

    [Fact]
    public void Decoding_and_writing_back_gives_the_writer_s_own_bytes()
    {
        // ToPlain and FromPlain are each other's inverse on what the writer writes: a page decoded, left as it is and
        // written back is the same file, frames, pieces and index alike.
        var flameJson = "{\"s\":[\"A\"],\"f\":[[0,\"span-x\",0,100,0,5]]}";
        var flameHtml = $"<div class=\"iflow-flame\" data-diagram-type=\"flamechart\" data-flame-z=\"{InternalFlowHtmlGenerator.CompressToBase64(flameJson)}\"></div>";
        var precomputed = new Dictionary<string, Kronikol.Reports.Merge.WholeTestFlowFragment> { ["s1"] = new("", flameHtml, 1) };
        string text;
        using (CompressedPage.WithPieceBytes(80))
            text = File.ReadAllText(Generate("CompressedPage_Inverse.html", precomputed: precomputed,
                visualization: WholeTestFlowVisualization.FlameChart, componentDiagram: "@startuml\n[A] --> [B]\n@enduml"));
        string again;
        using (CompressedPage.WithPieceBytes(80))
            again = CompressedPage.EditPlain(text, plain => plain);
        Assert.Equal(text, again);
    }

    [Fact]
    public void An_edit_of_the_plain_page_is_in_the_page_written_back()
    {
        var text = File.ReadAllText(Generate("CompressedPage_Edit.html"));
        var edited = CompressedPage.EditPlain(text, plain => plain.Replace("Cancel order", "Cancel the order", StringComparison.Ordinal));
        Assert.True(HtmlReportFile.IsCompressed(edited));
        var plain = HtmlReportFile.ToPlain(edited);
        Assert.Contains("Cancel the order", plain, StringComparison.Ordinal);
        Assert.Contains(JsonMap(plain, "puml-data").Values, v => Gunzip(v) == NeedleDiagrams()[1].CodeBehind);
    }

    // ---------- the same bytes (tests 7e, 7f) ----------

    [Theory]
    [InlineData(PlantUmlRendering.BrowserJs)]
    [InlineData(PlantUmlRendering.Local)]
    public void The_search_index_is_the_one_the_plain_page_held(PlantUmlRendering rendering)
    {
        // The SHA-256 of the index's base64 for this corpus on 5.5.4, the last release that wrote the page plain: the
        // index is built from the same text when the items are written raw.
        var plain = HtmlReportFile.ReadPlain(Generate($"CompressedPage_Index_{rendering}.html", rendering: rendering));
        var blob = SearchIndexReportTests.ExtractBlobBase64(plain)!;
        Assert.Equal("24eca78098fc11b0bb2504e17a83389098fa61c20461ab96b3161a70bbed52ff", Convert.ToHexStringLower(SHA256.HashData(Encoding.ASCII.GetBytes(blob))));
    }

    [Fact]
    public void Two_runs_over_the_same_input_write_the_same_bytes()
    {
        var a = File.ReadAllBytes(Generate("CompressedPage_SameA.html"));
        var b = File.ReadAllBytes(Generate("CompressedPage_SameB.html"));
        // The file name is in the loading screen's hint; nothing else may differ.
        var textA = Encoding.UTF8.GetString(a).Replace("CompressedPage_SameA.html", "X", StringComparison.Ordinal);
        var textB = Encoding.UTF8.GetString(b).Replace("CompressedPage_SameB.html", "X", StringComparison.Ordinal);
        Assert.Equal(textA, textB);
    }

    // ---------- the empty page (test 1) ----------

    [Fact]
    public void A_page_the_writer_leaves_empty_stays_empty()
    {
        var failing = TwoScenarioFeature();
        failing[0].Scenarios[0].Result = ExecutionResult.Failed;
        var path = ReportGenerator.GenerateHtmlReport(NeedleDiagrams(), failing, DateTime.UtcNow, DateTime.UtcNow, null,
            "CompressedPage_Blank.html", "Specifications", includeTestRunData: false, generateBlankOnFailedTests: true);
        Assert.Equal("", File.ReadAllText(path));
    }

    // ---------- the scripts that outlive document.open (test 19) ----------

    [Fact]
    public void The_loader_and_the_decoder_declare_no_global_name_a_report_script_could_collide_with()
    {
        // The window and its global scope outlive document.open, so a top-level let, const, class or function in either
        // script would stop a report script that declared the same name: each is one expression.
        static string[] Code(string script) => script.Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n')
            .Where(l => !l.TrimStart().StartsWith("//", StringComparison.Ordinal) && l.Trim().Length > 0).ToArray();
        var loader = Code(ReportGenerator.LoadResource("report-loader.js"));
        var decoder = Code(ReportGenerator.LoadResource("report-zstd-decoder.js"));

        Assert.Equal("(function () {", loader[0]);
        Assert.Equal("self.__kronikolZstd = self.__kronikolZstd || (function () {", decoder[0]);
        foreach (var script in new[] { loader, decoder })
        {
            Assert.Equal("})();", script[^1]);
            Assert.All(script[1..^1], line => Assert.True(char.IsWhiteSpace(line[0]), "a line at the top level: " + line));
        }
    }

    // ---------- the decoder the page carries, in Node (test 5) ----------

    /// <summary>
    /// Decodes one frame through the call the loader makes, <c>__kronikolZstd.decode(bytes, length, 'zstd')</c>, with no
    /// <c>DecompressionStream</c> (so the Wasm decoder runs) and, for <c>fzstd</c>, no WebAssembly either; prints the path
    /// taken and the SHA-256 of what it decoded, or the error and exit code 3. It sets the exit code rather than calling
    /// <c>process.exit</c>, which aborts Node 25.9 on Windows there (a libuv assertion in <c>src/win/async.c</c>).
    /// </summary>
    private const string DecoderDriver = """
        const fs = require('fs'), vm = require('vm'), crypto = require('crypto');
        const [decoderPath, framePath, length, mode] = process.argv.slice(2);
        const hide = name => Object.defineProperty(globalThis, name, { value: undefined, configurable: true, writable: true });
        globalThis.self = globalThis;
        hide('DecompressionStream');
        if (mode === 'fzstd') hide('WebAssembly');
        vm.runInThisContext(fs.readFileSync(decoderPath, 'utf8'), { filename: 'report-zstd-decoder.js' });
        const z = self.__kronikolZstd;
        z.decode(new Uint8Array(fs.readFileSync(framePath)), Number(length), 'zstd').then(
            out => process.stdout.write(z.path + ' ' + crypto.createHash('sha256').update(out).digest('hex')),
            e => { process.stderr.write(String(e && e.message || e)); process.exitCode = 3; });
        """;

    /// <summary>33 MB of text that compresses as a report does, with no run longer than a line repeated.</summary>
    private static byte[] LargeContent()
    {
        var text = new StringBuilder(34 << 20);
        var state = 0x2545F491u;
        for (var line = 0; text.Length < 33 << 20; line++)
        {
            state ^= state << 13; state ^= state >> 17; state ^= state << 5;
            text.Append("<div class=\"step\" data-line=\"").Append(line).Append("\">GET /api/orders/").Append(state % 100_000)
                .Append(" returned ").Append(200 + state % 7).Append("</div>\n");
        }
        return Encoding.UTF8.GetBytes(text.ToString());
    }

    private static NodeProbe.NodeRun DecodeInNode(byte[] frame, int length, string mode)
    {
        var dir = Directory.CreateTempSubdirectory("kronikol-zstd-node").FullName;
        try
        {
            var framePath = Path.Combine(dir, "frame.zst");
            var decoderPath = Path.Combine(dir, "report-zstd-decoder.js");
            File.WriteAllBytes(framePath, frame);
            File.WriteAllText(decoderPath, CompressedPage.DecoderScript.Value);
            return NodeProbe.RunCaptured(DecoderDriver, null, decoderPath, framePath, length.ToString(System.Globalization.CultureInfo.InvariantCulture), mode);
        }
        finally
        {
            try { Directory.Delete(dir, recursive: true); } catch (IOException) { } catch (UnauthorizedAccessException) { }
        }
    }

    [Theory]
    [InlineData("wasm")]
    [InlineData("fzstd")]
    public void The_decoder_the_page_carries_decodes_a_frame_over_32_MB_byte_for_byte(string mode)
    {
        Assert.SkipWhen(!NodeProbe.IsAvailable, "Node.js not available on PATH");
        var content = LargeContent();
        Assert.True(content.Length > 32 << 20);

        var run = DecodeInNode(CompressedPage.Compress(content), content.Length, mode);

        Assert.True(run.ExitCode == 0, run.Stderr);
        Assert.Equal(mode + " " + Convert.ToHexStringLower(SHA256.HashData(content)), run.Stdout.Trim());
    }

    [Fact]
    public void The_Wasm_decoder_refuses_a_frame_whose_checksum_is_wrong()
    {
        Assert.SkipWhen(!NodeProbe.IsAvailable, "Node.js not available on PATH");
        var content = Encoding.UTF8.GetBytes(string.Concat(Enumerable.Range(0, 5_000).Select(i => $"line {i} of a report\n")));
        var frame = CompressedPage.Compress(content);
        frame[^1] ^= 0x5a;

        var run = DecodeInNode(frame, content.Length, "wasm");

        Assert.Equal(3, run.ExitCode);
        Assert.Contains("zstd (wasm): the frame does not decode", run.Stderr, StringComparison.Ordinal);
    }
}
