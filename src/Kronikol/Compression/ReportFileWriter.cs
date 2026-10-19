using Kronikol.Compression.Zstd;
using Kronikol.Compression.Zstd.Unsafe;

namespace Kronikol.Compression;

/// <summary>
/// Writes a compressed report (plans/REPORT_FORMAT_PLAN.md 4.1 and 4.2): Kronikol's marker, then one zstd frame at level 9
/// with its checksum, around the report's JSON with every key that holds nothing left out (<see cref="ReportJsonPruner"/>).
/// The JSON is streamed through, never held whole, and the marker's decoded length is filled in once the frame is written.
/// </summary>
internal static class ReportFileWriter
{
    /// <summary>The level of section 4.1: on BreakfastProvider's report, 4.6% of the JSON in about 40 ms.</summary>
    internal const int Level = 9;

    /// <summary>
    /// Writes the container to <paramref name="destination"/>, which must be seekable, from its position: the JSON is what
    /// <paramref name="writeJson"/> writes to the stream it is handed. Returns the length of the JSON the file holds.
    /// </summary>
    public static long Write(Stream destination, Action<Stream> writeJson, RequiredKeys required)
    {
        var start = destination.Position;
        var marker = new byte[ReportFileFormat.MarkerLength];
        ReportFileFormat.WriteMarker(marker, decodedLength: 0);
        destination.Write(marker);

        long decoded;
        using (var zstd = new CompressionStream(destination, Level, leaveOpen: true))
        {
            zstd.SetParameter(ZSTD_cParameter.ZSTD_c_checksumFlag, 1);
            using var counting = new CountingStream(zstd);
            using (var pruner = new ReportJsonPruner(counting, required))
            {
                writeJson(pruner);
                pruner.Complete();
            }
            decoded = counting.Count;
        }

        // The length goes into the marker the reader checks the content against, and refuses a file by before decoding.
        var end = destination.Position;
        destination.Position = start;
        ReportFileFormat.WriteMarker(marker, decoded);
        destination.Write(marker);
        destination.Position = end;
        return decoded;
    }

    /// <summary>
    /// Passes every byte on, counting them; the stream beneath it is not disposed with it, and not flushed: a flush of the
    /// compressor ends a zstd block where it stands, and the filter above flushes its writer every few kilobytes. The
    /// compressor ends the frame when it is disposed.
    /// </summary>
    private sealed class CountingStream(Stream inner) : Stream
    {
        public long Count { get; private set; }

        public override bool CanRead => false;
        public override bool CanSeek => false;
        public override bool CanWrite => true;
        public override long Length => throw new NotSupportedException();
        public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
        public override void Flush() { }
        public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => Write(buffer.AsSpan(offset, count));

        public override void Write(ReadOnlySpan<byte> buffer)
        {
            inner.Write(buffer);
            Count += buffer.Length;
        }
    }
}
