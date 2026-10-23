using System.Buffers;
using System.Text.Encodings.Web;
using System.Text.Json;
using Kronikol.Compression;
using Kronikol.Query;
using Kronikol.Reports;

namespace Kronikol.Tool;

/// <summary>
/// <c>kronikol decode</c>: writes the JSON a report file holds, for a script or a person who needs the JSON itself
/// (plans/REPORT_FORMAT_PLAN.md 4.4, Q15). It reads every form the query engine reads, a compressed <c>.krkl</c>
/// included, and inflates each payload stored compressed in place (<c>$z</c>), so its output is plain JSON a
/// <c>jq</c> recipe or a redaction check can read in full. Compact, or indented with <c>--indent</c>; to stdout, or to
/// the file <c>-o</c> names. One forward pass: a report is never held whole. Given a report page it writes the plain
/// page instead (plans/HTML_COMPRESSION_PLAN.md Q9).
/// </summary>
public static class DecodeCommand
{
    public static int Run(IReadOnlyList<string> args, TextWriter @out, TextWriter error)
    {
        string? report = null;
        string? output = null;
        var indent = false;
        for (var i = 0; i < args.Count; i++)
        {
            switch (args[i])
            {
                case "-h" or "--help":
                    PrintUsage(@out);
                    return 0;
                case "-o" or "--out":
                    if (++i >= args.Count)
                    {
                        error.WriteLine("-o needs a file to write.");
                        return 2;
                    }
                    output = args[i];
                    break;
                case "--indent":
                    indent = true;
                    break;
                default:
                    if (args[i].StartsWith('-') || report is not null)
                    {
                        error.WriteLine($"Unknown argument: {args[i]}");
                        PrintUsage(error);
                        return 2;
                    }
                    report = args[i];
                    break;
            }
        }

        if (report is null)
        {
            error.WriteLine("No report given. Pass a TestRunReport.json or TestRunReport.krkl, or a directory holding one, or a report page.");
            PrintUsage(error);
            return 2;
        }

        if (QueryCommand.ResolveReport(report, error) is not { } resolved)
            return 2;

        var written = output is null ? null : Path.GetFullPath(output);
        if (written is not null && string.Equals(written, Path.GetFullPath(resolved), StringComparison.OrdinalIgnoreCase))
        {
            error.WriteLine($"-o {output} is the report being decoded; name another file.");
            return 2;
        }

        try
        {
            if (IsPage(resolved))
                return DecodePage(resolved, written, indent, @out, error);

            using var file = ReportScanner.OpenShared(resolved);
            using var content = ReportFileFormat.Open(file);
            using var destination = written is null ? new TextWriterStream(@out) : (Stream)File.Create(written);
            using (var writer = new Utf8JsonWriter(destination, new JsonWriterOptions { Indented = indent, Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping }))
                Rewrite(content.Stream, writer);
            if (written is null)
                @out.WriteLine();
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            error.WriteLine($"Could not read {resolved}: {exception.Message}");
            return 1;
        }
        catch (JsonException exception)
        {
            error.WriteLine($"{resolved} is not valid JSON, the form of every report Kronikol reads (plain, or compressed with zstd or gzip): {exception.Message}");
            return 1;
        }

        return 0;
    }

    /// <summary>
    /// Whether <paramref name="path"/> holds a page rather than a report data file: its first character, past a byte order
    /// mark and white space, is <c>&lt;</c>, which no form of the data file starts with.
    /// </summary>
    private static bool IsPage(string path)
    {
        Span<byte> head = stackalloc byte[64];
        using var file = ReportScanner.OpenShared(path);
        var text = head[..file.ReadAtLeast(head, head.Length, throwOnEndOfStream: false)];
        if (text.StartsWith((ReadOnlySpan<byte>)[0xEF, 0xBB, 0xBF]))
            text = text[3..];
        text = text.TrimStart(" \t\r\n"u8);
        return !text.IsEmpty && text[0] == (byte)'<';
    }

    /// <summary>
    /// A report page (plans/HTML_COMPRESSION_PLAN.md Q9): a compressed <c>TestRunReport.html</c> or
    /// <c>Specifications.html</c> written as its plain page, which a browser opens as it is and a search or an HTML parser
    /// reads in full; any other page as it is. The page is held whole, as a browser holds it, and nothing is written
    /// unless it decodes.
    /// </summary>
    private static int DecodePage(string resolved, string? written, bool indent, TextWriter @out, TextWriter error)
    {
        if (indent)
        {
            error.WriteLine("--indent indents JSON; a page is written as its plain page, as it is.");
            return 2;
        }

        string plain;
        try
        {
            plain = HtmlReportFile.ToPlain(File.ReadAllText(resolved));
        }
        catch (InvalidDataException exception)
        {
            error.WriteLine($"{resolved} is a compressed report that is damaged, and cannot be decoded: {exception.Message}");
            return 1;
        }

        if (written is null)
            @out.Write(plain);
        else
            File.WriteAllText(written, plain);
        return 0;
    }

    /// <summary>The decoding pass under test: <paramref name="source"/>'s JSON rewritten to <paramref name="destination"/>.</summary>
    internal static void Rewrite(Stream source, Stream destination, bool indent = false)
    {
        using var writer = new Utf8JsonWriter(destination, new JsonWriterOptions { Indented = indent, Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping });
        Rewrite(source, writer);
    }

    private static void Rewrite(Stream source, Utf8JsonWriter writer)
    {
        var buffer = ArrayPool<byte>.Shared.Rent(128 * 1024);
        try
        {
            var state = new JsonReaderState(new JsonReaderOptions { CommentHandling = JsonCommentHandling.Skip });
            var filled = 0;
            var eof = false;
            while (true)
            {
                if (!eof)
                {
                    var read = source.Read(buffer, filled, buffer.Length - filled);
                    filled += read;
                    eof = read == 0;
                }

                var reader = new Utf8JsonReader(buffer.AsSpan(0, filled), eof, state);
                var consumed = Copy(ref reader, writer, out state);

                if (eof && consumed >= filled)
                    break;
                if (consumed == 0 && filled == buffer.Length)
                {
                    // One token, or one wrapper, is larger than the window: double it and read on.
                    var bigger = ArrayPool<byte>.Shared.Rent(buffer.Length * 2);
                    buffer.AsSpan(0, filled).CopyTo(bigger);
                    ArrayPool<byte>.Shared.Return(buffer);
                    buffer = bigger;
                    continue;
                }
                if (eof && consumed == 0)
                    throw new JsonException("The JSON ends in the middle of a value.");

                Buffer.BlockCopy(buffer, consumed, buffer, 0, filled - consumed);
                filled -= consumed;
            }
            writer.Flush();
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(buffer);
        }
    }

    /// <summary>
    /// Copies every whole token the window holds, a payload wrapper as the text it holds, and returns how many bytes it
    /// used: everything up to the first token, or wrapper, that is not whole in it yet.
    /// </summary>
    private static int Copy(ref Utf8JsonReader reader, Utf8JsonWriter writer, out JsonReaderState state)
    {
        while (true)
        {
            var before = reader;
            if (!reader.Read())
            {
                state = before.CurrentState;
                return (int)before.BytesConsumed;
            }

            if (reader.TokenType == JsonTokenType.StartObject && WrapperAhead(reader) is { } whole)
            {
                if (!whole)
                {
                    // The wrapper runs past the window: start it again once more is read.
                    state = before.CurrentState;
                    return (int)before.BytesConsumed;
                }
                using var wrapper = JsonDocument.ParseValue(ref reader);
                writer.WriteStringValue(ReportPayloads.Inflate(wrapper.RootElement.GetProperty("$z").GetString()!));
                continue;
            }

            switch (reader.TokenType)
            {
                case JsonTokenType.StartObject: writer.WriteStartObject(); break;
                case JsonTokenType.EndObject: writer.WriteEndObject(); break;
                case JsonTokenType.StartArray: writer.WriteStartArray(); break;
                case JsonTokenType.EndArray: writer.WriteEndArray(); break;
                case JsonTokenType.PropertyName: writer.WritePropertyName(reader.GetString()!); break;
                case JsonTokenType.String: writer.WriteStringValue(reader.GetString()); break;
                case JsonTokenType.Number: writer.WriteRawValue(reader.HasValueSequence ? reader.ValueSequence.ToArray() : reader.ValueSpan, skipInputValidation: true); break;
                case JsonTokenType.True: writer.WriteBooleanValue(true); break;
                case JsonTokenType.False: writer.WriteBooleanValue(false); break;
                case JsonTokenType.Null: writer.WriteNullValue(); break;
            }
        }
    }

    /// <summary>
    /// Whether the object <paramref name="reader"/> has just opened is a payload stored compressed, <c>{"$h","$n","$z"}</c>
    /// (<see cref="ReportPayloads"/>): null when it is not, false when it may be but is not whole in the window yet.
    /// </summary>
    private static bool? WrapperAhead(Utf8JsonReader reader)
    {
        var probe = reader;
        if (!probe.Read())
            return false;
        if (probe.TokenType != JsonTokenType.PropertyName || probe.ValueSpan.Length == 0 || probe.ValueSpan[0] != (byte)'$')
            return null;

        var whole = reader;
        if (!whole.TrySkip())
            return false;

        var keys = reader;
        var hasPayload = false;
        while (keys.Read() && keys.TokenType != JsonTokenType.EndObject)
        {
            if (keys.TokenType != JsonTokenType.PropertyName)
                continue;
            if (keys.ValueTextEquals("$z"u8))
            {
                keys.Read();
                hasPayload = keys.TokenType == JsonTokenType.String;
            }
            else if (!keys.ValueTextEquals("$h"u8) && !keys.ValueTextEquals("$n"u8))
                return null;
            else
                keys.TrySkip();
        }
        return hasPayload ? true : null;
    }

    /// <summary>The bytes the JSON writer writes, as text on a <see cref="TextWriter"/>: the output the command was handed.</summary>
    private sealed class TextWriterStream(TextWriter target) : Stream
    {
        private readonly System.Text.Decoder _decoder = System.Text.Encoding.UTF8.GetDecoder();

        public override void Write(byte[] buffer, int offset, int count) => Write(buffer.AsSpan(offset, count));

        public override void Write(ReadOnlySpan<byte> buffer)
        {
            var chars = ArrayPool<char>.Shared.Rent(buffer.Length + 4);
            try
            {
                var decoded = _decoder.GetChars(buffer, chars, flush: false);
                target.Write(chars, 0, decoded);
            }
            finally
            {
                ArrayPool<char>.Shared.Return(chars);
            }
        }

        public override void Flush() => target.Flush();
        public override bool CanRead => false;
        public override bool CanSeek => false;
        public override bool CanWrite => true;
        public override long Length => throw new NotSupportedException();
        public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
        public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
    }

    public static void PrintUsage(TextWriter w)
    {
        w.WriteLine("Usage: kronikol decode <report> [-o <file>] [--indent]");
        w.WriteLine();
        w.WriteLine("  Writes the JSON a report file holds: TestRunReport.krkl decoded, or a TestRunReport.json, with every");
        w.WriteLine("  payload stored compressed (\"$z\") written out as text. For a script or a jq recipe that needs the JSON");
        w.WriteLine("  itself; to answer a question about a run, use kronikol query, which never needs it.");
        w.WriteLine();
        w.WriteLine("  Given a report page (TestRunReport.html or Specifications.html, which are written compressed), writes");
        w.WriteLine("  its plain page: one a browser opens as it is, and a search or an HTML parser reads in full.");
        w.WriteLine();
        w.WriteLine("  <report>      a TestRunReport.json or TestRunReport.krkl, or a directory holding one; or a report page");
        w.WriteLine("  -o <file>     write to <file> instead of stdout");
        w.WriteLine("  --indent      indent the JSON two spaces a level (compact without it); not for a page");
        w.WriteLine();
        w.WriteLine("Examples:");
        w.WriteLine("  kronikol decode ./Reports --indent | jq '.features[].scenarios[].name'");
        w.WriteLine("  kronikol decode TestRunReport.html -o plain.html");
    }
}
