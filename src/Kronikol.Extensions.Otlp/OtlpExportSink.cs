using System.Threading.Channels;
using Kronikol.Reports;
using Kronikol.Tracking;

namespace Kronikol.Extensions.Otlp;

/// <summary>
/// Streaming OTLP export as an <see cref="IRequestResponseSink"/> — the live twin of
/// <see cref="OtlpExporter"/>, for tap topologies: assign it (composed via
/// <see cref="CompositeRequestResponseSink"/> with the tap's normal sink) to a <c>ProxyTap</c>,
/// <c>TcpTap</c> or <see cref="OtlpTap"/> <c>Sink</c> property, and every captured call streams to the
/// collector as it happens.
/// </summary>
/// <remarks>
/// <para><strong>D3: capture never blocks.</strong> <see cref="Log"/> is a <c>TryWrite</c> into a
/// bounded channel (<see cref="OtlpExportOptions.QueueCapacity"/>); when the queue is full the entry is
/// dropped and counted, never awaited. A background worker pairs requests with responses (buffering
/// pending requests up to <see cref="OtlpExportOptions.PendingRequestTtl"/>, after which they export as
/// zero-duration orphans), batches (<see cref="OtlpExportOptions.BatchMaxSpans"/> /
/// <see cref="OtlpExportOptions.FlushInterval"/>) and POSTs. Failures and drops are counted and surfaced
/// through <see cref="Diagnostics"/>, never thrown, never blocking.</para>
/// <para><strong>Deterministic test-end export.</strong> <see cref="FlushAsync"/> drains what is queued
/// and pushes the current batch; <see cref="DisposeAsync"/> additionally exports still-pending requests
/// as orphans, waiting at most <see cref="OtlpExportOptions.ShutdownTimeout"/> before cancelling an
/// in-flight POST so a hung collector cannot hold the process open.</para>
/// </remarks>
public sealed class OtlpExportSink : IRequestResponseSink, IAsyncDisposable
{
    private readonly OtlpExportOptions _options;
    private readonly OtlpExporter _exporter;
    private readonly Channel<Item> _queue;
    private readonly CancellationTokenSource _cts = new();
    private readonly Task _worker;
    private int _disposed;
    private long _dropped;
    private long _skipped;
    private long _spansExported;
    private long _spansFailed;
    private long _batchesFailed;
    private long _orphans;

    /// <summary>Creates the sink and starts its background worker (the options are validated here).</summary>
    public OtlpExportSink(OtlpExportOptions options)
    {
        _options = options ?? throw new ArgumentNullException(nameof(options));
        _exporter = new OtlpExporter(options);
        _queue = Channel.CreateBounded<Item>(new BoundedChannelOptions(Math.Max(1, options.QueueCapacity))
        {
            // Wait, not DropWrite: TryWrite then returns false when the queue is full instead of silently
            // dropping, so the sink can count the drop. Log() never awaits a write, so nothing blocks.
            FullMode = BoundedChannelFullMode.Wait,
            SingleReader = true,
            SingleWriter = false,
        });
        _worker = Task.Run(() => RunAsync(_cts.Token), CancellationToken.None);
    }

    /// <summary>The options this sink runs with.</summary>
    public OtlpExportOptions Options => _options;

    /// <summary>Entries dropped because the export queue was full (D3: capture never blocks).</summary>
    public long EntriesDropped => Interlocked.Read(ref _dropped);

    /// <summary>Records not exported by design: diagram markers, <c>TrackingIgnore</c> entries, span-sourced echoes.</summary>
    public long RecordsSkipped => Interlocked.Read(ref _skipped);

    /// <summary>Spans the collector accepted (orphans included).</summary>
    public long SpansExported => Interlocked.Read(ref _spansExported);

    /// <summary>Spans in batches that failed both delivery attempts.</summary>
    public long SpansFailed => Interlocked.Read(ref _spansFailed);

    /// <summary>Batches that failed both delivery attempts.</summary>
    public long BatchesFailed => Interlocked.Read(ref _batchesFailed);

    /// <summary>Spans exported without their other half (zero-duration, <c>kronikol.orphan = true</c>).</summary>
    public long OrphanSpans => Interlocked.Read(ref _orphans);

    /// <inheritdoc />
    public void Log(RequestResponseLog log)
    {
        if (log is null || Volatile.Read(ref _disposed) == 1)
            return;

        if (OtlpSpanMapper.ShouldSkip(log, _options))
        {
            Interlocked.Increment(ref _skipped);
            return;
        }

        if (!_queue.Writer.TryWrite(new Item(log, null)))
        {
            Interlocked.Increment(ref _dropped);
            _options.Log?.Invoke($"[{_options.DisplayName}] dropped a captured entry (export queue full)");
        }
    }

    /// <summary>
    /// Export health as report diagnostics: one <see cref="DiagnosticKind.CaptureDegraded"/> entry per
    /// non-zero problem counter, worded for a report reader — mirroring <see cref="OtlpTap.Diagnostics"/>.
    /// Empty when the sink is healthy. <see cref="RecordsSkipped"/> is by design and not reported.
    /// </summary>
    public IReadOnlyList<DiagnosticEntry> Diagnostics()
    {
        var name = _options.DisplayName;
        var entries = new List<DiagnosticEntry>();

        var dropped = EntriesDropped;
        if (dropped > 0)
            entries.Add(new DiagnosticEntry(DiagnosticKind.CaptureDegraded,
                $"{name}: {dropped:N0} captured entr{(dropped == 1 ? "y" : "ies")} dropped because the export queue was full (QueueCapacity {_options.QueueCapacity}) — their spans never reached the collector; capture was never delayed"));

        var failed = SpansFailed;
        if (failed > 0)
            entries.Add(new DiagnosticEntry(DiagnosticKind.CaptureDegraded,
                $"{name}: {failed:N0} span(s) in {BatchesFailed:N0} batch(es) could not be delivered to {_options.Endpoint} — the collector was down or rejecting exports; the observed system was never delayed"));

        return entries;
    }

    /// <summary>
    /// Drains everything queued so far and pushes the current batch, waiting up to
    /// <paramref name="timeout"/> (null = as long as it takes). Returns quietly on timeout or after
    /// disposal — flushing is best-effort by design, like everything else on this path.
    /// </summary>
    public async Task FlushAsync(TimeSpan? timeout = null, CancellationToken cancellationToken = default)
    {
        if (Volatile.Read(ref _disposed) == 1)
            return;

        var signal = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        try
        {
            await _queue.Writer.WriteAsync(new Item(null, signal), cancellationToken).ConfigureAwait(false);
        }
        catch (ChannelClosedException)
        {
            return;
        }

        try
        {
            await (timeout is { } limit
                ? signal.Task.WaitAsync(limit, cancellationToken)
                : signal.Task.WaitAsync(cancellationToken)).ConfigureAwait(false);
        }
        catch (TimeoutException)
        {
            _options.Log?.Invoke($"[{_options.DisplayName}] flush timed out after {timeout}");
        }
    }

    // ------------------------------------------------------------------ worker

    private async Task RunAsync(CancellationToken ct)
    {
        var reader = _queue.Reader;
        var batch = new List<OtlpExportSpan>();
        var pending = new Dictionary<(Guid TraceId, Guid PairId), (RequestResponseLog Log, long QueuedAt)>();
        var flushIntervalMs = Math.Max(1, (long)_options.FlushInterval.TotalMilliseconds);
        var nextFlush = Environment.TickCount64 + flushIntervalMs;

        try
        {
            while (true)
            {
                while (reader.TryRead(out var item))
                {
                    if (item.Flush is { } signal)
                    {
                        SweepExpired(pending, batch);
                        await FlushBatchAsync(batch, ct).ConfigureAwait(false);
                        nextFlush = Environment.TickCount64 + flushIntervalMs;
                        signal.TrySetResult();
                        continue;
                    }

                    Process(item.Log!, pending, batch);
                    if (batch.Count >= _options.BatchMaxSpans)
                        await FlushBatchAsync(batch, ct).ConfigureAwait(false);
                }

                SweepExpired(pending, batch);
                if (Environment.TickCount64 >= nextFlush)
                {
                    await FlushBatchAsync(batch, ct).ConfigureAwait(false);
                    nextFlush = Environment.TickCount64 + flushIntervalMs;
                }

                var wait = reader.WaitToReadAsync(ct).AsTask();
                var delayMs = Math.Clamp(nextFlush - Environment.TickCount64, 1, flushIntervalMs);
                var completed = await Task.WhenAny(wait, Task.Delay(TimeSpan.FromMilliseconds(delayMs), ct)).ConfigureAwait(false);
                if (completed == wait && !await wait.ConfigureAwait(false))
                    break; // the channel completed: shut down
            }
        }
        catch (OperationCanceledException)
        {
            return; // hard shutdown — a hung collector must not hold the process open
        }

        // Graceful shutdown: drain what is queued, orphan what never got its response, final flush.
        try
        {
            while (reader.TryRead(out var item))
            {
                if (item.Flush is { } signal)
                {
                    signal.TrySetResult();
                    continue;
                }

                Process(item.Log!, pending, batch);
            }

            foreach (var (log, _) in pending.Values)
                batch.Add(MapUnpaired(log));
            pending.Clear();

            await FlushBatchAsync(batch, ct).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            // The shutdown timeout lapsed mid-drain.
        }
    }

    private void Process(
        RequestResponseLog log,
        Dictionary<(Guid TraceId, Guid PairId), (RequestResponseLog Log, long QueuedAt)> pending,
        List<OtlpExportSpan> batch)
    {
        var key = (log.TraceId, log.RequestResponseId);
        if (log.Type == RequestResponseType.Request)
        {
            if (log.DurationMs is not null)
            {
                // A capturer that sends one record per call measured the duration itself — complete now.
                batch.Add(OtlpSpanMapper.Map(log, null, _options, DateTimeOffset.UtcNow));
                return;
            }

            pending[key] = (log, Environment.TickCount64);
            return;
        }

        if (pending.Remove(key, out var request))
        {
            batch.Add(OtlpSpanMapper.Map(request.Log, log, _options, DateTimeOffset.UtcNow));
            return;
        }

        // A response with no buffered request exports as an orphan straight away.
        batch.Add(MapUnpaired(log));
    }

    private void SweepExpired(
        Dictionary<(Guid TraceId, Guid PairId), (RequestResponseLog Log, long QueuedAt)> pending,
        List<OtlpExportSpan> batch)
    {
        if (pending.Count == 0)
            return;

        var deadline = Environment.TickCount64 - (long)_options.PendingRequestTtl.TotalMilliseconds;
        List<(Guid, Guid)>? expired = null;
        foreach (var (key, entry) in pending)
        {
            if (entry.QueuedAt <= deadline)
                (expired ??= []).Add(key);
        }

        if (expired is null)
            return;

        foreach (var key in expired)
        {
            if (pending.Remove(key, out var entry))
                batch.Add(MapUnpaired(entry.Log));
        }
    }

    private OtlpExportSpan MapUnpaired(RequestResponseLog log)
    {
        var span = log.Type == RequestResponseType.Request
            ? OtlpSpanMapper.Map(log, null, _options, DateTimeOffset.UtcNow)
            : OtlpSpanMapper.Map(null, log, _options, DateTimeOffset.UtcNow);
        if (span.Attribute("kronikol.orphan") is not null)
            Interlocked.Increment(ref _orphans);
        return span;
    }

    private async Task FlushBatchAsync(List<OtlpExportSpan> batch, CancellationToken ct)
    {
        if (batch.Count == 0)
            return;

        var page = batch.ToArray();
        batch.Clear();
        var result = await _exporter.ExportSpansAsync(page, ct).ConfigureAwait(false);
        Interlocked.Add(ref _spansExported, result.SpansExported);
        Interlocked.Add(ref _spansFailed, result.SpansFailed);
        Interlocked.Add(ref _batchesFailed, result.BatchesFailed);
    }

    /// <summary>
    /// Stops accepting entries, drains and exports what it can within
    /// <see cref="OtlpExportOptions.ShutdownTimeout"/> (pending requests go out as orphans), then cancels
    /// anything still in flight.
    /// </summary>
    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposed, 1) == 1)
            return;

        _queue.Writer.TryComplete();
        try
        {
            await _worker.WaitAsync(_options.ShutdownTimeout).ConfigureAwait(false);
        }
        catch (TimeoutException)
        {
            await _cts.CancelAsync().ConfigureAwait(false);
            try
            {
                await _worker.ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                // Expected.
            }
        }

        // A hard-cancelled worker may have left flush signals queued — release their waiters.
        while (_queue.Reader.TryRead(out var leftover))
            leftover.Flush?.TrySetResult();

        _exporter.Dispose();
        _cts.Dispose();
        _options.Log?.Invoke(
            $"[{_options.DisplayName}] export sink stopped ({SpansExported} span(s) exported, {OrphanSpans} orphan(s), "
            + $"{SpansFailed} span(s) undeliverable, {EntriesDropped} dropped, {RecordsSkipped} skipped)");
    }

    private readonly record struct Item(RequestResponseLog? Log, TaskCompletionSource? Flush);
}
