using System.Globalization;
using System.IO.Compression;
using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Kronikol.Compression.Zstd;
using Kronikol.Compression.Zstd.Unsafe;
using Kronikol.InternalFlow;

namespace Kronikol.Reports;

/// <summary>
/// The compressed report page (plans/HTML_COMPRESSION_PLAN.md section 4). <c>TestRunReport.html</c> and
/// <c>Specifications.html</c> are written as a small loader page holding the report as zstd frames in base64: the page
/// itself, with every embedded item raw and each diagram source replaced by a token, then the diagram sources in pieces
/// of about 16 MB, decoded by the page when a diagram in them is first asked for. In one stream the items share their
/// text with each other, which per-item gzip could not: 29 MB of page became 7 MB.
/// </summary>
internal static partial class CompressedPage
{
    /// <summary>The marker a compressed page carries in its head, for tools and tests (and <c>kronikol decode</c>).</summary>
    internal const string MarkerName = "kronikol-page";
    internal const string MarkerContent = "frames 1";

    /// <summary>A diagram's place in <c>#puml-data</c>: U+0001 and the diagram's number, in the order of the sources.</summary>
    internal const char TokenMark = '\u0001';

    /// <summary>zstd level 9 (the owner's choice, section 3.2) with an 8 MB window: RFC 9659's cap for a native decoder.</summary>
    internal const int Level = 9;
    internal const int WindowLog = 23;

    /// <summary>The diagram sources go in pieces of at most this much UTF-8, cut between sources (section 3.12, Q2).</summary>
    internal const int PieceBytes = 16 << 20;

    private static readonly AsyncLocal<int?> PieceBytesOverride = new();

    /// <summary>A smaller piece for the writer in this flow, so a fact can cut a small report into many pieces.</summary>
    internal static IDisposable WithPieceBytes(int bytes)
    {
        var previous = PieceBytesOverride.Value;
        PieceBytesOverride.Value = bytes;
        return new Restore(() => PieceBytesOverride.Value = previous);
    }

    private sealed class Restore(Action undo) : IDisposable
    {
        public void Dispose() => undo();
    }

    internal const string PageFrameId = "kron-frame-page";
    internal const string PieceIdPrefix = "kron-frame-diagrams-";
    internal const string IndexId = "kron-diagram-index";

    internal static string Token(int number) => TokenMark + number.ToString(CultureInfo.InvariantCulture);

    internal static bool IsToken(string value) => value.Length > 1 && value[0] == TokenMark;

    /// <summary>
    /// True for an item still in gzip and base64 (it starts with gzip's magic bytes): a fragment that merge took from a
    /// data file written before the compressed page, read as it is.
    /// </summary>
    internal static bool IsGzipBase64(string value) => value.StartsWith("H4sI", StringComparison.Ordinal);

    /// <summary>True when <paramref name="html"/> is a compressed page: its marker sits in the first few kilobytes.</summary>
    internal static bool IsCompressed(string html)
    {
        var head = html.Length > 8192 ? html.AsSpan(0, 8192) : html.AsSpan();
        return head.Contains($"<meta name=\"{MarkerName}\" content=\"frames ", StringComparison.Ordinal);
    }

    // ---------------------------------------------------------------- writing

    /// <summary>The loader page for <paramref name="page"/>, whose tokens name <paramref name="diagramSources"/> by position.</summary>
    internal static string Build(string page, IReadOnlyList<string> diagramSources, LoaderParts parts)
    {
        var html = new StringBuilder(64 * 1024);
        html.Append("<!DOCTYPE html>\n<html>\n<head>\n<meta charset=\"utf-8\" />\n")
            .Append("<meta name=\"viewport\" content=\"width=device-width, initial-scale=1\" />\n")
            .Append("<meta name=\"generator\" content=\"Kronikol v").Append(parts.Version).Append("\" />\n")
            .Append("<meta name=\"").Append(MarkerName).Append("\" content=\"").Append(MarkerContent).Append("\" />\n")
            .Append("<title>").Append(WebUtility.HtmlEncode(parts.Title)).Append("</title>\n")
            .Append(parts.FaviconLink).Append('\n')
            .Append("<script id=\"kronikol-theme-boot\">").Append(parts.ThemeBoot).Append("</script>\n")
            .Append("<style>").Append(ReportGenerator.LoadResource("report-loader.css")).Append("</style>\n")
            .Append("</head>\n<body>\n");
        // The file's name only: the page is read on other machines than the one that wrote it, and the folder it was
        // written in is that machine's.
        var file = WebUtility.HtmlEncode(Path.GetFileName(parts.FileName));
        html.Append("<div id=\"kron-loading\" role=\"status\" aria-live=\"polite\">")
            .Append("<p class=\"kron-loading-title\">Opening the report\u2026</p><div class=\"kron-loading-bar\"></div>")
            .Append("<p class=\"kron-loading-hint\">Still opening? This report is compressed and opens with JavaScript in a web browser. ")
            .Append("If it never opens, write a plain copy with the kronikol tool: <code>kronikol decode ").Append(file)
            .Append(" -o plain.html</code></p></div>\n")
            .Append("<noscript><p>This report is compressed and needs JavaScript to open. Open it in a web browser with JavaScript on, ")
            .Append("or write a plain copy with the kronikol tool: <code>kronikol decode ").Append(file).Append(" -o plain.html</code></p></noscript>\n");

        AppendFrames(html, page, diagramSources);
        html.Append("<script id=\"kron-zstd\">").Append(DecoderScript.Value).Append("</script>\n")
            .Append("<script id=\"kron-loader\">").Append(ReportGenerator.LoadResource("report-loader.js")).Append("</script>\n")
            .Append("</body>\n</html>\n");
        return html.ToString();
    }

    /// <summary>The page frame, a frame per piece of the diagram sources, and the index of the sources' lengths per piece.</summary>
    private static void AppendFrames(StringBuilder html, string page, IReadOnlyList<string> diagramSources)
    {
        AppendFrame(html, PageFrameId, piece: null, Encoding.UTF8.GetBytes(page));
        var index = new List<List<int>>();
        var n = 0;
        foreach (var (bytes, lengths) in Pieces(diagramSources))
        {
            n++;
            AppendFrame(html, PieceIdPrefix + n.ToString(CultureInfo.InvariantCulture), piece: n, bytes);
            index.Add(lengths);
        }
        html.Append("<script id=\"").Append(IndexId).Append("\" type=\"application/json\">")
            .Append(JsonSerializer.Serialize(index)).Append("</script>\n");
    }

    private static void AppendFrame(StringBuilder html, string id, int? piece, byte[] content)
    {
        html.Append("<script id=\"").Append(id).Append('"').Append(piece is { } n ? $" data-kron-piece=\"{n.ToString(CultureInfo.InvariantCulture)}\"" : "")
            .Append(" type=\"application/octet-stream\" data-codec=\"zstd\" data-length=\"")
            .Append(content.Length.ToString(CultureInfo.InvariantCulture)).Append("\">")
            .Append(Convert.ToBase64String(Compress(content))).Append("</script>\n");
    }

    /// <summary>The sources as UTF-8, cut between sources into pieces of at most <see cref="PieceBytes"/>; a larger source gets a piece of its own.</summary>
    internal static IEnumerable<(byte[] Bytes, List<int> Lengths)> Pieces(IReadOnlyList<string> sources)
    {
        var limit = PieceBytesOverride.Value ?? PieceBytes;
        var buffer = new MemoryStream();
        var lengths = new List<int>();
        foreach (var source in sources)
        {
            var bytes = Encoding.UTF8.GetBytes(source);
            if (lengths.Count > 0 && buffer.Length + bytes.Length > limit)
            {
                yield return (buffer.ToArray(), lengths);
                buffer = new MemoryStream();
                lengths = new List<int>();
            }
            buffer.Write(bytes);
            lengths.Add(bytes.Length);
        }
        if (lengths.Count > 0)
            yield return (buffer.ToArray(), lengths);
    }

    /// <summary>One zstd frame: level 9, window log 23 set (level 9's own is 22), the content size and the checksum on.</summary>
    internal static byte[] Compress(ReadOnlySpan<byte> content)
    {
        using var compressor = new Compressor(Level);
        compressor.SetParameter(ZSTD_cParameter.ZSTD_c_windowLog, WindowLog);
        compressor.SetParameter(ZSTD_cParameter.ZSTD_c_checksumFlag, 1);
        compressor.SetParameter(ZSTD_cParameter.ZSTD_c_contentSizeFlag, 1);
        return compressor.Wrap(content).ToArray();
    }

    /// <summary>What the loader page carries of the report beside its frames.</summary>
    internal sealed record LoaderParts(string Title, string FileName, string Version, string ThemeBoot, string FaviconLink);

    /// <summary>The decoder script: libzstd's decoder in WebAssembly and fzstd, both vendored (section 4.8), around the glue.</summary>
    internal static readonly Lazy<string> DecoderScript = new(() =>
        ReportGenerator.LoadResource("report-zstd-decoder.js")
            .Replace("__KRONIKOL_ZSTD_WASM__", Convert.ToBase64String(ZstdDecoderWasm()), StringComparison.Ordinal)
            .Replace("/*__KRONIKOL_FZSTD__*/", ReportGenerator.LoadResource("fzstd.js"), StringComparison.Ordinal));

    private static byte[] ZstdDecoderWasm()
    {
        var assembly = typeof(CompressedPage).Assembly;
        var name = assembly.GetManifestResourceNames().First(r => r.EndsWith("zstddec.wasm", StringComparison.OrdinalIgnoreCase));
        using var stream = assembly.GetManifestResourceStream(name)!;
        using var copy = new MemoryStream();
        stream.CopyTo(copy);
        return copy.ToArray();
    }

    // ---------------------------------------------------------------- reading

    /// <summary>
    /// A compressed page's frames: the page's text, decoded, and the diagram sources by their number, each piece decoded
    /// the first time a diagram in it is read. A piece may be missing (an export carries only those its diagrams name).
    /// </summary>
    internal sealed class Frames
    {
        private readonly Dictionary<int, (byte[] Bytes, string Codec, int Length)> _pieces;
        private readonly Dictionary<int, byte[]> _decoded = new();
        private readonly (int Piece, int Start, int Length)[] _at;

        internal Frames(string page, Dictionary<int, (byte[] Bytes, string Codec, int Length)> pieces, int[][] lengths)
        {
            Page = page;
            _pieces = pieces;
            var at = new List<(int, int, int)>();
            for (var p = 0; p < lengths.Length; p++)
            {
                var start = 0;
                foreach (var length in lengths[p])
                {
                    at.Add((p + 1, start, length));
                    start += length;
                }
            }
            _at = at.ToArray();
        }

        public string Page { get; }
        public int DiagramCount => _at.Length;

        public string Diagram(int number)
        {
            if (number < 0 || number >= _at.Length)
                throw new InvalidDataException($"The page names diagram {number}, and its index holds {_at.Length}.");
            var (p, start, length) = _at[number];
            if (!_decoded.TryGetValue(p, out var bytes))
            {
                if (!_pieces.TryGetValue(p, out var piece))
                    throw new InvalidDataException($"Diagram {number} is in piece {p} of the diagram sources, which this file does not carry.");
                _decoded[p] = bytes = Decode(piece.Bytes, piece.Codec, piece.Length, PieceIdPrefix + p.ToString(CultureInfo.InvariantCulture));
            }
            if (start + length > bytes.Length)
                throw new InvalidDataException($"Diagram {number} runs past the end of piece {p} of the diagram sources.");
            return Encoding.UTF8.GetString(bytes, start, length);
        }
    }

    [GeneratedRegex("<script id=\"(?<id>kron-frame-[a-z0-9-]+)\"(?: data-kron-piece=\"(?<piece>[0-9]+)\")? type=\"application/octet-stream\" data-codec=\"(?<codec>zstd|gzip|none)\" data-length=\"(?<length>[0-9]+)\">(?<b64>[A-Za-z0-9+/=]*)</script>")]
    private static partial Regex FrameElement();

    [GeneratedRegex("<script id=\"kron-diagram-index\" type=\"application/json\">(?<json>[^<]*)</script>")]
    private static partial Regex IndexElement();

    /// <summary>Reads a compressed page's frames; throws <see cref="InvalidDataException"/> for one that is damaged.</summary>
    internal static Frames Read(string loaderPage)
    {
        string? page = null;
        var pieces = new Dictionary<int, (byte[], string, int)>();
        foreach (Match m in FrameElement().Matches(loaderPage))
        {
            var length = int.Parse(m.Groups["length"].Value, CultureInfo.InvariantCulture);
            var codec = m.Groups["codec"].Value;
            var bytes = Convert.FromBase64String(m.Groups["b64"].Value);
            if (m.Groups["id"].Value == PageFrameId)
                page = Encoding.UTF8.GetString(Decode(bytes, codec, length, PageFrameId));
            else if (m.Groups["piece"].Success)
                pieces[int.Parse(m.Groups["piece"].Value, CultureInfo.InvariantCulture)] = (bytes, codec, length);
        }
        if (page is null)
            throw new InvalidDataException("The page frame is missing.");
        var index = IndexElement().Match(loaderPage);
        var lengths = index.Success ? JsonSerializer.Deserialize<int[][]>(index.Groups["json"].Value) ?? [] : [];
        return new Frames(page, pieces, lengths);
    }

    private static byte[] Decode(byte[] compressed, string codec, int length, string id)
    {
        try
        {
            byte[] bytes;
            if (codec == "none")
                bytes = compressed;
            else if (codec == "gzip")
            {
                using var gzip = new GZipStream(new MemoryStream(compressed), CompressionMode.Decompress);
                using var output = new MemoryStream(length);
                gzip.CopyTo(output);
                bytes = output.ToArray();
            }
            else
            {
                using var decompressor = new Decompressor();
                bytes = decompressor.Unwrap(compressed, length).ToArray();
            }
            if (bytes.Length != length)
                throw new InvalidDataException($"The frame {id} decodes to {bytes.Length} bytes, where the page records {length}.");
            return bytes;
        }
        catch (Exception e) when (e is not InvalidDataException)
        {
            throw new InvalidDataException($"The frame {id} does not decode: {e.Message}", e);
        }
    }

    [GeneratedRegex("<script id=\"kron-frame-page\" type=\"application/octet-stream\" data-codec=\"(?:zstd|gzip|none)\" data-length=\"[0-9]+\">[A-Za-z0-9+/=]*</script>")]
    private static partial Regex PageFrameElement();

    /// <summary>
    /// <paramref name="loaderPage"/> with its page frame replaced by <paramref name="edit"/> of the page's text, written as
    /// zstd again; the diagram sources' pieces, the loader and the decoder are left as they are. For a fact that changes the
    /// page a reader opens (an engine's address, a script of its own) as it would change the text of a plain page.
    /// </summary>
    internal static string EditPage(string loaderPage, Func<string, string> edit)
    {
        var page = edit(Read(loaderPage).Page);
        var element = new StringBuilder();
        AppendFrame(element, PageFrameId, piece: null, Encoding.UTF8.GetBytes(page));
        var replaced = 0;
        var result = PageFrameElement().Replace(loaderPage, _ => { replaced++; return element.ToString().TrimEnd('\n'); });
        if (replaced != 1)
            throw new InvalidDataException($"The page holds {replaced} page frames.");
        return result;
    }

    /// <summary>
    /// The page frame and the diagram sources the writer would have made of <paramref name="plain"/>, a plain page: each
    /// <c>#puml-data</c> entry's source becomes a token, numbered in the map's order; <c>#step-data</c>'s entries are
    /// written raw; then <see cref="Raw"/>. The inverse of <see cref="Plain"/>.
    /// </summary>
    internal static (string Page, List<string> Sources) FromPlain(string plain)
    {
        var sources = new List<string>();
        var page = MapElement().Replace(plain, m =>
        {
            var diagrams = m.Groups["id"].Value == "puml-data";
            var map = new Dictionary<string, string>(StringComparer.Ordinal);
            using (var doc = JsonDocument.Parse(m.Groups["json"].Value))
            {
                foreach (var entry in doc.RootElement.EnumerateObject())
                {
                    var value = entry.Value.GetString() ?? "";
                    var text = IsGzipBase64(value) ? InternalFlowHtmlGenerator.DecompressFromBase64(value) : value;
                    if (diagrams)
                    {
                        map[entry.Name] = Token(sources.Count);
                        sources.Add(text);
                    }
                    else
                    {
                        map[entry.Name] = text;
                    }
                }
            }
            return $"<script id=\"{m.Groups["id"].Value}\" type=\"application/json\">{JsonSerializer.Serialize(map)}</script>";
        });
        return (Raw(page), sources);
    }

    /// <summary>
    /// <paramref name="loaderPage"/> after <paramref name="edit"/> of its plain page: decoded, edited, and written back as a
    /// compressed page in the same loader page, its frames and index made again (<see cref="FromPlain"/>). For a fact that
    /// changes a report's text as it would change a plain page's, and still opens the page a reader gets.
    /// </summary>
    internal static string EditPlain(string loaderPage, Func<string, string> edit)
    {
        var (page, sources) = FromPlain(edit(ToPlain(loaderPage)));
        var first = loaderPage.IndexOf("<script id=\"" + PageFrameId + "\"", StringComparison.Ordinal);
        var decoder = loaderPage.IndexOf("<script id=\"kron-zstd\">", StringComparison.Ordinal);
        if (first < 0 || decoder < first)
            throw new InvalidDataException("The page is not a compressed report.");
        var frames = new StringBuilder();
        AppendFrames(frames, page, sources);
        return loaderPage[..first] + frames + loaderPage[decoder..];
    }

    /// <summary>The plain page of a compressed one, as a page written with every item in gzip and base64 reads.</summary>
    internal static string ToPlain(string loaderPage)
    {
        var frames = Read(loaderPage);
        return Plain(frames.Page, frames.Diagram);
    }

    [GeneratedRegex("<script id=\"(?<id>puml-data|step-data)\" type=\"application/json\">(?<json>[^<]*)</script>")]
    private static partial Regex MapElement();

    [GeneratedRegex("<script id=\"iflow-segments\" type=\"application/json\">(?<json>[^<]*)</script>")]
    private static partial Regex SegmentsElement();

    [GeneratedRegex("(?<=\\sdata-(?:flame|plantuml)-z=\")[^\"]*(?=\")")]
    private static partial Regex InlineItem();

    /// <summary>
    /// <paramref name="page"/> with every raw item put back in gzip and base64 and every diagram token replaced by its
    /// source in gzip and base64: <c>#puml-data</c>, <c>#step-data</c>, <c>#iflow-segments</c>' <c>z</c>, and the
    /// <c>data-flame-z</c> and <c>data-plantuml-z</c> attributes. An item already in gzip is kept as it is.
    /// </summary>
    internal static string Plain(string page, Func<int, string> diagram)
    {
        page = MapElement().Replace(page, m =>
        {
            var map = new Dictionary<string, string>(StringComparer.Ordinal);
            using (var doc = JsonDocument.Parse(m.Groups["json"].Value))
            {
                foreach (var entry in doc.RootElement.EnumerateObject())
                {
                    var value = entry.Value.GetString() ?? "";
                    map[entry.Name] = IsToken(value)
                        ? InternalFlowHtmlGenerator.CompressToBase64(diagram(int.Parse(value.AsSpan(1), CultureInfo.InvariantCulture)))
                        : Gzipped(m.Groups["id"].Value == "step-data" ? PlainInline(value) : value);
                }
            }
            return $"<script id=\"{m.Groups["id"].Value}\" type=\"application/json\">{JsonSerializer.Serialize(map)}</script>";
        });
        page = SegmentsElement().Replace(page, m =>
        {
            using var doc = JsonDocument.Parse(m.Groups["json"].Value);
            var list = doc.RootElement.EnumerateObject().First();
            var z = doc.RootElement.GetProperty("z").GetString() ?? "";
            return $"<script id=\"iflow-segments\" type=\"application/json\">{{\"{list.Name}\":{list.Value.GetRawText()},\"z\":\"{Gzipped(z)}\"}}</script>";
        });
        return PlainInline(page);
    }

    private static string PlainInline(string html) =>
        InlineItem().Replace(html, m => Gzipped(UnescapeAttribute(m.Value)));

    /// <summary>
    /// <paramref name="page"/> with every item still in gzip and base64 written raw, the exact inverse of <see cref="Plain"/>:
    /// the <c>data-flame-z</c> and <c>data-plantuml-z</c> attributes (a whole-test flow's flame chart, the fragments
    /// <c>kronikol merge</c> takes from a data file) attribute-escaped, and <c>#iflow-segments</c>' <c>z</c> as a JSON string.
    /// What the writer holds raw from the start (the diagram sources, the held sub-steps) is left as it is, and so is the
    /// search index, which its worker reads in gzip.
    /// </summary>
    internal static string Raw(string page)
    {
        page = SegmentsElement().Replace(page, m =>
        {
            using var doc = JsonDocument.Parse(m.Groups["json"].Value);
            var z = doc.RootElement.GetProperty("z").GetString() ?? "";
            if (!IsGzipBase64(z))
                return m.Value;
            var list = doc.RootElement.EnumerateObject().First();
            return $"<script id=\"iflow-segments\" type=\"application/json\">{{\"{list.Name}\":{list.Value.GetRawText()},\"z\":{JsonSerializer.Serialize(InternalFlowHtmlGenerator.DecompressFromBase64(z))}}}</script>";
        });
        return InlineItem().Replace(page, m => IsGzipBase64(m.Value) ? EscapeAttribute(InternalFlowHtmlGenerator.DecompressFromBase64(m.Value)) : m.Value);
    }

    /// <summary>An item's text: gunzipped where it is still gzip and base64, else as written (attribute-unescaped for an attribute's value).</summary>
    internal static string ItemText(string value, bool attribute) =>
        IsGzipBase64(value) ? InternalFlowHtmlGenerator.DecompressFromBase64(value) : attribute ? UnescapeAttribute(value) : value;

    private static string Gzipped(string value) =>
        IsGzipBase64(value) ? value : InternalFlowHtmlGenerator.CompressToBase64(value);

    /// <summary>Undoes the writer's attribute escape (<c>&amp;</c>, <c>&quot;</c>, <c>&lt;</c>), <c>&amp;</c> last.</summary>
    internal static string UnescapeAttribute(string value) =>
        value.Replace("&lt;", "<", StringComparison.Ordinal).Replace("&quot;", "\"", StringComparison.Ordinal).Replace("&amp;", "&", StringComparison.Ordinal);

    /// <summary>The writer's attribute escape for a raw item: the three characters that could end the value or start markup.</summary>
    internal static string EscapeAttribute(string value) =>
        value.Replace("&", "&amp;", StringComparison.Ordinal).Replace("\"", "&quot;", StringComparison.Ordinal).Replace("<", "&lt;", StringComparison.Ordinal);
}
