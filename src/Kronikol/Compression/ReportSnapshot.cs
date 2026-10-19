namespace Kronikol.Compression;

/// <summary>
/// The decoded JSON of a compressed report, kept for the command that read it (plans/REPORT_FORMAT_PLAN.md 4.4, Q11): the
/// scanner reads the file forward once, decoding as it goes, and this holds what it decoded so payload reads can seek in
/// it. Held in memory up to a limit, 256 MiB, and above it in a temporary file opened to be deleted when it is closed.
/// One consistent copy, so a payload read never sees a report replaced after the scan.
/// </summary>
internal sealed class ReportSnapshot(long memoryLimit) : IDisposable
{
    /// <summary>Decoded bytes held in memory before the snapshot moves to a temporary file (Q11).</summary>
    internal const long DefaultMemoryLimit = 256L << 20;

    /// <summary>The size of each piece the bytes are held in, so the whole is never copied to grow it.</summary>
    internal const int ChunkSize = 1 << 20;

    /// <summary>Lowers the memory limit for the scans of one test (an internal hook, section 5); null leaves the default.</summary>
    internal static readonly AsyncLocal<long?> MemoryLimitForTests = new();

    private readonly List<byte[]> _chunks = [];
    private FileStream? _spill;
    private bool _complete;

    public ReportSnapshot() : this(MemoryLimitForTests.Value ?? DefaultMemoryLimit)
    {
    }

    /// <summary>The decoded bytes held so far, and all of them once <see cref="Complete"/> is called.</summary>
    public long Length { get; private set; }

    /// <summary>The temporary file, once the bytes outgrew the memory limit; null while they are in memory.</summary>
    public string? SpillPath { get; private set; }

    public void Write(ReadOnlySpan<byte> bytes)
    {
        if (_complete)
            throw new InvalidOperationException("The snapshot is complete.");
        if (_spill is null && Length + bytes.Length > memoryLimit)
            Spill();

        if (_spill is not null)
        {
            _spill.Write(bytes);
            Length += bytes.Length;
            return;
        }

        while (!bytes.IsEmpty)
        {
            var within = (int)(Length % ChunkSize);
            if (within == 0)
                _chunks.Add(new byte[ChunkSize]);
            var piece = Math.Min(bytes.Length, ChunkSize - within);
            bytes[..piece].CopyTo(_chunks[^1].AsSpan(within));
            bytes = bytes[piece..];
            Length += piece;
        }
    }

    /// <summary>Ends writing: from here on the snapshot is read only.</summary>
    public void Complete()
    {
        _complete = true;
        _spill?.Flush();
    }

    /// <summary>A seekable stream over the snapshot, with its own position; any number may be open at once.</summary>
    public Stream OpenRead()
    {
        if (!_complete)
            throw new InvalidOperationException("The snapshot is still being written.");
        return new Reader(this);
    }

    public void Dispose()
    {
        _spill?.Dispose();
        _chunks.Clear();
    }

    private void Spill()
    {
        SpillPath = Path.Combine(Path.GetTempPath(), $"kronikol-{Guid.NewGuid():N}.json");
        _spill = new FileStream(SpillPath, FileMode.CreateNew, FileAccess.ReadWrite, FileShare.None, 1 << 16, FileOptions.DeleteOnClose);
        var left = Length;
        foreach (var chunk in _chunks)
        {
            var piece = (int)Math.Min(left, ChunkSize);
            _spill.Write(chunk, 0, piece);
            left -= piece;
        }
        _chunks.Clear();
    }

    private int ReadAt(long position, Span<byte> buffer)
    {
        if (position >= Length)
            return 0;
        if (buffer.Length > Length - position)
            buffer = buffer[..(int)(Length - position)];
        if (_spill is not null)
            return RandomAccess.Read(_spill.SafeFileHandle, buffer, position);

        var copied = 0;
        while (copied < buffer.Length)
        {
            var at = position + copied;
            var chunk = _chunks[(int)(at / ChunkSize)];
            var within = (int)(at % ChunkSize);
            var piece = Math.Min(buffer.Length - copied, ChunkSize - within);
            chunk.AsSpan(within, piece).CopyTo(buffer[copied..]);
            copied += piece;
        }
        return copied;
    }

    private sealed class Reader(ReportSnapshot snapshot) : Stream
    {
        private long _position;

        public override int Read(byte[] buffer, int offset, int count) => Read(buffer.AsSpan(offset, count));

        public override int Read(Span<byte> buffer)
        {
            var read = snapshot.ReadAt(_position, buffer);
            _position += read;
            return read;
        }

        public override long Seek(long offset, SeekOrigin origin)
        {
            _position = origin switch
            {
                SeekOrigin.Begin => offset,
                SeekOrigin.Current => _position + offset,
                _ => snapshot.Length + offset,
            };
            if (_position < 0)
                throw new IOException("A seek before the start of the report.");
            return _position;
        }

        public override bool CanRead => true;
        public override bool CanSeek => true;
        public override bool CanWrite => false;
        public override long Length => snapshot.Length;
        public override long Position { get => _position; set => Seek(value, SeekOrigin.Begin); }
        public override void Flush() { }
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }
}

/// <summary>Copies what it reads from a decoding stream into a <see cref="ReportSnapshot"/>, as the scanner reads.</summary>
internal sealed class SnapshotTee(Stream source, ReportSnapshot snapshot) : Stream
{
    public override int Read(byte[] buffer, int offset, int count) => Read(buffer.AsSpan(offset, count));

    public override int Read(Span<byte> buffer)
    {
        var read = source.Read(buffer);
        snapshot.Write(buffer[..read]);
        return read;
    }

    public override bool CanRead => true;
    public override bool CanSeek => false;
    public override bool CanWrite => false;
    public override long Length => throw new NotSupportedException();
    public override long Position { get => snapshot.Length; set => throw new NotSupportedException(); }
    public override void Flush() { }
    public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
    public override void SetLength(long value) => throw new NotSupportedException();
    public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
}
