using System.Text;
using Kronikol.Tracking;

namespace Kronikol.Ingestion;

/// <summary>
/// An <see cref="IRequestResponseSink"/> that appends one <see cref="InteractionRecord"/> per line to an
/// NDJSON file — the cross-process / cross-language on-ramp. Pair it with the in-process store via
/// <see cref="CompositeRequestResponseSink"/> when the same process also generates the report, or use it
/// alone in an out-of-process capturer and replay the file with <c>kronikol ingest</c>.
/// </summary>
/// <remarks>
/// Thread-safe: writes are serialised on an internal lock and flushed per line, so a crash loses at most
/// the line being written. The file is opened with read sharing so a dashboard can tail it live.
/// </remarks>
public sealed class NdjsonInteractionWriter : IRequestResponseSink, IDisposable
{
    private readonly object _gate = new();
    private readonly StreamWriter _writer;
    private long _lines;
    private bool _disposed;

    /// <summary>Opens (or creates) the file at <paramref name="path"/>. When <paramref name="append"/> is false the file is truncated — one file per run is the intended lifecycle.</summary>
    public NdjsonInteractionWriter(string path, bool append = false)
    {
        Path = System.IO.Path.GetFullPath(path);
        var directory = System.IO.Path.GetDirectoryName(Path);
        if (!string.IsNullOrEmpty(directory))
            Directory.CreateDirectory(directory);

        var stream = new FileStream(Path, append ? FileMode.Append : FileMode.Create, FileAccess.Write, FileShare.Read);
        _writer = new StreamWriter(stream, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false)) { AutoFlush = true, NewLine = "\n" };
    }

    /// <summary>The absolute path of the file being written.</summary>
    public string Path { get; }

    /// <summary>Lines written so far.</summary>
    public long LinesWritten => Interlocked.Read(ref _lines);

    /// <inheritdoc />
    public void Log(RequestResponseLog log) => Write(InteractionRecord.FromLog(log));

    /// <summary>Writes one record as a line.</summary>
    public void Write(InteractionRecord record)
    {
        var line = record.ToJson();
        lock (_gate)
        {
            if (_disposed)
                return;
            _writer.WriteLine(line);
            Interlocked.Increment(ref _lines);
        }
    }

    /// <summary>Writes a request/response pair (two lines).</summary>
    public void Write((InteractionRecord Request, InteractionRecord Response) pair)
    {
        Write(pair.Request);
        Write(pair.Response);
    }

    /// <summary>Flushes buffered output (writes are already flushed per line; this is for symmetry with other sinks).</summary>
    public void Flush()
    {
        lock (_gate)
        {
            if (!_disposed)
                _writer.Flush();
        }
    }

    /// <inheritdoc />
    public void Dispose()
    {
        lock (_gate)
        {
            if (_disposed)
                return;
            _disposed = true;
            _writer.Dispose();
        }
    }
}
