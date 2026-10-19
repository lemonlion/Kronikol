using System.Buffers.Binary;
using System.Globalization;
using System.IO.Compression;
using Kronikol.Compression.Zstd;

namespace Kronikol.Compression;

/// <summary>The forms a report data file comes in, told apart by its first bytes (plans/REPORT_FORMAT_PLAN.md 4.1).</summary>
internal enum ReportForm
{
    /// <summary>Plain JSON, as every report written before the compressed file existed.</summary>
    Json,

    /// <summary>Kronikol's marker, a zstd skippable frame naming the file and its decoded length, then one zstd frame.</summary>
    Krkl,

    /// <summary>A zstd frame without Kronikol's marker, such as a report recompressed with the zstd tool.</summary>
    Zstd,

    /// <summary>gzip, as Kronikol4J writes the compressed file.</summary>
    Gzip,
}

/// <summary>
/// A report file that cannot be read as one: damaged or truncated, larger decoded than the cap, written by a newer
/// Kronikol, or not a report at all. An <see cref="IOException"/>, so every caller that already reports a file it could
/// not read reports this one too, rather than letting a decoder's own exception escape as a crash (4.4).
/// </summary>
internal sealed class ReportDecodeException(string message, Exception? inner = null) : IOException(message, inner);

/// <summary>What a report file holds, opened: the file itself for JSON, or a stream that decodes it.</summary>
internal sealed class ReportContent(ReportForm form, Stream stream, long start, long? declaredLength) : IDisposable
{
    public ReportForm Form { get; } = form;

    /// <summary>The JSON: the file, positioned past a byte order mark, or the decoding stream, read forward only.</summary>
    public Stream Stream { get; } = stream;

    /// <summary>Where the JSON starts in the file: past a byte order mark (3), or 0. Compressed forms start at 0 decoded.</summary>
    public long Start { get; } = start;

    /// <summary>The decoded length Kronikol's marker declares; null for every other form.</summary>
    public long? DeclaredLength { get; } = declaredLength;

    /// <summary>Disposes the decoder; the file it reads stays open for its owner.</summary>
    public void Dispose()
    {
        if (Form != ReportForm.Json)
            Stream.Dispose();
    }
}

/// <summary>
/// Reads the first bytes of a report file and opens what it holds (plans/REPORT_FORMAT_PLAN.md 4.1 and 4.4). Every reader
/// of a report data file comes through here, so they all accept and refuse the same files.
/// </summary>
internal static class ReportFileFormat
{
    /// <summary>The length of Kronikol's marker: a skippable frame's magic and length, then 16 bytes of content.</summary>
    internal const int MarkerLength = 24;

    /// <summary>The newest container version this reader knows.</summary>
    internal const byte ContainerVersion = 1;

    /// <summary>The most a report may decode to: 4 GiB (Q11). A small file from a fork's CI that decodes to more is refused (F15).</summary>
    internal const long HardCap = 4L << 30;

    private const uint KronikolMarkerMagic = 0x184D2A50;
    private const uint ZstdFrameMagic = 0xFD2FB528;

    /// <summary>
    /// Writes Kronikol's marker into <paramref name="marker"/>, <see cref="MarkerLength"/> bytes: a skippable frame's magic
    /// and the length of its content, <c>KRKL</c>, the container version, three reserved bytes, and the length of the JSON
    /// the zstd frame after it decodes to, as <see cref="Detect"/> reads them.
    /// </summary>
    internal static void WriteMarker(Span<byte> marker, long decodedLength)
    {
        BinaryPrimitives.WriteUInt32LittleEndian(marker, KronikolMarkerMagic);
        BinaryPrimitives.WriteUInt32LittleEndian(marker[4..], MarkerLength - 8);
        "KRKL"u8.CopyTo(marker[8..]);
        marker[12] = ContainerVersion;
        marker[13..16].Clear();
        BinaryPrimitives.WriteInt64LittleEndian(marker[16..], decodedLength);
    }

    /// <summary>
    /// Opens the report in <paramref name="file"/>, which must be at its start: the file itself for JSON, past a byte
    /// order mark, or a decoding stream for the compressed forms. Throws <see cref="ReportDecodeException"/> for bytes
    /// that are none of the forms, for a marker that is damaged, from a newer version or claims more than
    /// <paramref name="cap"/>; the stream throws it for damage, and for content that decodes past the cap.
    /// </summary>
    public static ReportContent Open(Stream file, long cap = HardCap)
    {
        Span<byte> head = stackalloc byte[MarkerLength];
        var got = file.ReadAtLeast(head, head.Length, throwOnEndOfStream: false);
        head = head[..got];
        var form = Detect(head, out var declared);

        switch (form)
        {
            case ReportForm.Json:
                var start = head.StartsWith(Bom) ? Bom.Length : 0;
                file.Seek(start - got, SeekOrigin.Current);
                return new ReportContent(form, file, start, null);
            case ReportForm.Krkl:
                if (declared > cap)
                    throw new ReportDecodeException(string.Create(CultureInfo.InvariantCulture,
                        $"it says it decodes to {declared:N0} bytes, more than {Cap(cap)}, the most a report may decode to"));
                return new ReportContent(form, new DecodingStream(new DecompressionStream(file), form, cap, declared), 0, declared);
            case ReportForm.Zstd:
                file.Seek(-got, SeekOrigin.Current);
                return new ReportContent(form, new DecodingStream(new DecompressionStream(file), form, cap, null), 0, null);
            default:
                file.Seek(-got, SeekOrigin.Current);
                // .NET's GZipStream checks the CRC of a whole stream, but a truncated one just ends early, so the
                // decoded length is checked against the size in the trailer, the file's last four bytes (RFC 1952).
                var gzipSize = TrailerSize(file);
                return new ReportContent(form, new DecodingStream(new GZipStream(file, CompressionMode.Decompress, leaveOpen: true), form, cap, null, gzipSize), 0, null);
        }
    }

    /// <summary>The form the first bytes give, and the decoded length when they are Kronikol's marker.</summary>
    internal static ReportForm Detect(ReadOnlySpan<byte> head, out long? declaredLength)
    {
        declaredLength = null;
        if (head.Length >= 12 && BinaryPrimitives.ReadUInt32LittleEndian(head) == KronikolMarkerMagic && head.Slice(8, 4).SequenceEqual("KRKL"u8))
        {
            if (head.Length < MarkerLength || BinaryPrimitives.ReadUInt32LittleEndian(head[4..]) != MarkerLength - 8)
                throw new ReportDecodeException("it is damaged or truncated: Kronikol's marker at its start is not the 24 bytes it should be");
            if (head[12] > ContainerVersion)
                throw new ReportDecodeException($"it was written by a newer Kronikol (container version {head[12]}; this tool reads version {ContainerVersion}). Upgrade Kronikol.Tool.");
            declaredLength = BinaryPrimitives.ReadInt64LittleEndian(head[16..]);
            if (declaredLength < 0)
                throw new ReportDecodeException("it is damaged or truncated: Kronikol's marker gives a negative length");
            return ReportForm.Krkl;
        }

        // Any skippable frame (0x184D2A50 to 0x184D2A5F) is one a zstd decoder steps over.
        if (head.Length >= 4 && (BinaryPrimitives.ReadUInt32LittleEndian(head) & 0xFFFFFFF0) == KronikolMarkerMagic)
            return ReportForm.Zstd;
        if (head.Length >= 4 && BinaryPrimitives.ReadUInt32LittleEndian(head) == ZstdFrameMagic)
            return ReportForm.Zstd;
        if (head.Length >= 2 && head[0] == 0x1F && head[1] == 0x8B)
            return ReportForm.Gzip;

        var json = head.StartsWith(Bom) ? head[Bom.Length..] : head;
        json = json.TrimStart(" \t\r\n"u8);
        if (json.IsEmpty || json[0] == (byte)'{')
            return ReportForm.Json;

        throw new ReportDecodeException(
            "it is not a report Kronikol reads: a report is JSON (starting with {, after a byte order mark or none), or that JSON compressed with zstd (Kronikol's TestRunReport.krkl, or a bare zstd frame) or gzip");
    }

    private static ReadOnlySpan<byte> Bom => [0xEF, 0xBB, 0xBF];

    /// <summary>The ISIZE field of a gzip stream's trailer: its decoded length modulo 2^32.</summary>
    private static uint TrailerSize(Stream file)
    {
        // The shortest gzip stream is a 10-byte header and an 8-byte trailer.
        if (file.Length - file.Position < 18)
            throw new ReportDecodeException("it is damaged or truncated: it is too short to be a gzip stream");
        var start = file.Position;
        Span<byte> size = stackalloc byte[4];
        file.Seek(-4, SeekOrigin.End);
        file.ReadExactly(size);
        file.Position = start;
        return BinaryPrimitives.ReadUInt32LittleEndian(size);
    }

    /// <summary>The cap in words: its bytes, and its gibibytes when it is a whole number of them.</summary>
    internal static string Cap(long cap) =>
        cap >= 1L << 30 && cap % (1L << 30) == 0
            ? string.Create(CultureInfo.InvariantCulture, $"{cap:N0} bytes ({cap >> 30} GiB)")
            : string.Create(CultureInfo.InvariantCulture, $"{cap:N0} bytes");

    /// <summary>
    /// Decodes forward only, counting: it stops at the cap however the content describes itself, checks the length
    /// Kronikol's marker declared once the content ends, and turns every decoder failure into one
    /// <see cref="ReportDecodeException"/> that says the file is damaged or truncated.
    /// </summary>
    private sealed class DecodingStream(Stream decoder, ReportForm form, long cap, long? declared, uint? gzipSize = null) : Stream
    {
        private long _decoded;
        private bool _ended;

        public override int Read(byte[] buffer, int offset, int count) => Read(buffer.AsSpan(offset, count));

        public override int Read(Span<byte> buffer)
        {
            if (_ended || buffer.IsEmpty)
                return 0;

            // One byte past the cap is enough to know the content runs over it, and no more is ever decoded.
            var room = cap - _decoded + 1;
            if (buffer.Length > room)
                buffer = buffer[..(int)room];

            int read;
            try
            {
                read = decoder.Read(buffer);
            }
            catch (Exception exception) when (exception is ZstdException or InvalidDataException or EndOfStreamException)
            {
                throw new ReportDecodeException($"it is damaged or truncated: its {Describe(form)} does not decode ({exception.Message})", exception);
            }

            _decoded += read;
            if (_decoded > cap)
                throw new ReportDecodeException($"it decodes to more than {Cap(cap)}, the most a report may decode to");
            if (read == 0)
            {
                _ended = true;
                if (declared is { } length && length != _decoded)
                    throw new ReportDecodeException(string.Create(CultureInfo.InvariantCulture,
                        $"it is damaged or truncated: it decodes to {_decoded:N0} bytes, where Kronikol's marker says {length:N0}"));
                if (gzipSize is { } size && size != (uint)_decoded)
                    throw new ReportDecodeException("it is damaged or truncated: its gzip stream ends before its trailer, or the trailer gives another length");
            }
            return read;
        }

        private static string Describe(ReportForm form) => form == ReportForm.Gzip ? "gzip stream" : "zstd frame";

        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();
        public override long Position { get => _decoded; set => throw new NotSupportedException(); }
        public override void Flush() { }
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();

        protected override void Dispose(bool disposing)
        {
            if (disposing)
                decoder.Dispose();
            base.Dispose(disposing);
        }
    }
}
