namespace Kronikol.Tracking;

/// <summary>
/// A destination for captured <see cref="RequestResponseLog"/> entries. The default sink is the
/// process-wide <see cref="RequestResponseLogger"/> store that the report generators read; capturers
/// that run in a <em>different</em> process from report generation (an out-of-process proxy tap, a
/// polyglot service) write to an NDJSON file sink instead and the file is replayed later with
/// <c>kronikol ingest</c> or <c>Kronikol.Ingestion.IngestPipeline</c>.
/// </summary>
public interface IRequestResponseSink
{
    /// <summary>Records one request or response entry.</summary>
    void Log(RequestResponseLog log);
}

/// <summary>Sink that writes to the process-wide <see cref="RequestResponseLogger"/> store.</summary>
public sealed class RequestResponseLoggerSink : IRequestResponseSink
{
    /// <summary>The shared instance.</summary>
    public static RequestResponseLoggerSink Instance { get; } = new();

    /// <inheritdoc />
    public void Log(RequestResponseLog log) => RequestResponseLogger.Log(log);
}

/// <summary>Fans one entry out to several sinks (e.g. the in-process store <em>and</em> an NDJSON file).</summary>
public sealed class CompositeRequestResponseSink : IRequestResponseSink
{
    private readonly IRequestResponseSink[] _sinks;

    /// <summary>Creates a composite over the given sinks; <c>null</c> entries are ignored.</summary>
    public CompositeRequestResponseSink(params IRequestResponseSink?[] sinks)
    {
        _sinks = sinks.Where(s => s is not null).Select(s => s!).ToArray();
    }

    /// <summary>The sinks written to, in order.</summary>
    public IReadOnlyList<IRequestResponseSink> Sinks => _sinks;

    /// <inheritdoc />
    public void Log(RequestResponseLog log)
    {
        foreach (var sink in _sinks)
            sink.Log(log);
    }
}
