using System.IO.Compression;
using System.Net;
using System.Text;
using Kronikol.Tracking;

namespace Kronikol.Extensions.Otlp;

/// <summary>What one export run did: spans sent, spans lost, and the bookkeeping the caller reports.</summary>
public sealed record OtlpExportResult
{
    /// <summary>Spans POSTed in batches the collector accepted.</summary>
    public int SpansExported { get; init; }

    /// <summary>Spans in batches that failed both attempts — they never reached the collector.</summary>
    public int SpansFailed { get; init; }

    /// <summary>Records not exported: diagram markers, <c>TrackingIgnore</c> entries, span-sourced echoes.</summary>
    public int SkippedRecords { get; init; }

    /// <summary>Spans exported without their other half (zero-duration, <c>kronikol.orphan = true</c>).</summary>
    public int OrphanSpans { get; init; }

    /// <summary>Distinct trace ids across the exported spans.</summary>
    public int TraceCount { get; init; }

    /// <summary>Batches the collector accepted.</summary>
    public int BatchesSent { get; init; }

    /// <summary>Batches that failed both attempts (see <see cref="OtlpExportOptions.Log"/> for the reasons).</summary>
    public int BatchesFailed { get; init; }

    /// <summary>Whether every batch landed.</summary>
    public bool Success => BatchesFailed == 0;
}

/// <summary>
/// Batch push of captured interactions to an OTLP/HTTP collector: pair → encode (OTLP/JSON) → gzip? →
/// POST, paged by <see cref="OtlpExportOptions.BatchMaxSpans"/>. The primary export mode — test suites
/// are batch-shaped, and post-hoc export puts nothing on any hot path (D3 trivially holds). For live tap
/// topologies use <see cref="OtlpExportSink"/>, which streams through this same pipeline.
/// </summary>
/// <remarks>
/// <para><strong>Non-interference.</strong> This is a standalone <c>HttpClient</c> POSTing to a URL. It
/// never touches the observed system's <c>TracerProviderBuilder</c>, never registers processors, never
/// flips <c>Activity.Recorded</c> — exporting can never change what the system under test emits.</para>
/// <para><strong>Failure discipline.</strong> A failed batch gets one immediate re-attempt, then is
/// counted in <see cref="OtlpExportResult.BatchesFailed"/> and logged — never thrown, and no aggressive
/// retry loop that would hold a test process open. The result (or the sink's diagnostics) tells the
/// operator the collector was down.</para>
/// </remarks>
public sealed class OtlpExporter : IDisposable
{
    private readonly OtlpExportOptions _options;
    private readonly HttpClient _http;

    /// <summary>Creates an exporter for the given options (validated here).</summary>
    public OtlpExporter(OtlpExportOptions options)
    {
        _options = options ?? throw new ArgumentNullException(nameof(options));
        options.Validate();
        _http = new HttpClient(new SocketsHttpHandler
        {
            UseProxy = false,
            AllowAutoRedirect = false,
            AutomaticDecompression = DecompressionMethods.None,
            UseCookies = false,
        })
        {
            Timeout = TimeSpan.FromSeconds(30),
        };
    }

    /// <summary>The options this exporter runs with.</summary>
    public OtlpExportOptions Options => _options;

    /// <summary>
    /// Maps the captured records (pairing, skips, orphans — see <see cref="OtlpSpanMapper.MapAll"/>) and
    /// POSTs the resulting spans. Never throws on delivery failure; read the result.
    /// </summary>
    public async Task<OtlpExportResult> ExportAsync(IEnumerable<RequestResponseLog> logs, CancellationToken cancellationToken = default)
    {
        var batch = OtlpSpanMapper.MapAll(logs, _options, DateTimeOffset.UtcNow);
        var result = await ExportSpansAsync(batch.Spans, cancellationToken).ConfigureAwait(false);
        return result with { SkippedRecords = batch.SkippedRecords, OrphanSpans = batch.OrphanSpans };
    }

    /// <summary>POSTs already-mapped spans, paged by <see cref="OtlpExportOptions.BatchMaxSpans"/>.</summary>
    public async Task<OtlpExportResult> ExportSpansAsync(IReadOnlyList<OtlpExportSpan> spans, CancellationToken cancellationToken = default)
    {
        var exported = 0;
        var failed = 0;
        var batchesSent = 0;
        var batchesFailed = 0;

        for (var offset = 0; offset < spans.Count; offset += _options.BatchMaxSpans)
        {
            var page = spans.Skip(offset).Take(_options.BatchMaxSpans).ToArray();
            if (await PostAsync(page, cancellationToken).ConfigureAwait(false))
            {
                batchesSent++;
                exported += page.Length;
            }
            else
            {
                batchesFailed++;
                failed += page.Length;
            }
        }

        return new OtlpExportResult
        {
            SpansExported = exported,
            SpansFailed = failed,
            TraceCount = spans.Select(s => s.TraceId).Distinct(StringComparer.Ordinal).Count(),
            BatchesSent = batchesSent,
            BatchesFailed = batchesFailed,
        };
    }

    /// <summary>POSTs one page: one immediate re-attempt on failure, then count and log — never throw.</summary>
    private async Task<bool> PostAsync(IReadOnlyList<OtlpExportSpan> page, CancellationToken cancellationToken)
    {
        var payload = Encoding.UTF8.GetBytes(OtlpJsonEncoder.Encode(page));
        if (_options.Gzip)
        {
            using var compressed = new MemoryStream();
            await using (var gzip = new GZipStream(compressed, CompressionLevel.Fastest, leaveOpen: true))
                gzip.Write(payload);
            payload = compressed.ToArray();
        }

        for (var attempt = 1; attempt <= 2; attempt++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                using var message = new HttpRequestMessage(HttpMethod.Post, _options.Endpoint);
                var content = new ByteArrayContent(payload);
                content.Headers.TryAddWithoutValidation("Content-Type", OtlpTraceReader.JsonContentType);
                if (_options.Gzip)
                    content.Headers.TryAddWithoutValidation("Content-Encoding", "gzip");
                message.Content = content;
                foreach (var (name, value) in _options.Headers)
                {
                    if (!message.Headers.TryAddWithoutValidation(name, value))
                        content.Headers.TryAddWithoutValidation(name, value);
                }

                using var response = await _http.SendAsync(message, HttpCompletionOption.ResponseContentRead, cancellationToken).ConfigureAwait(false);
                if (response.IsSuccessStatusCode)
                    return true;
                _options.Log?.Invoke($"[{_options.DisplayName}] export batch of {page.Count} span(s) answered {(int)response.StatusCode} (attempt {attempt}/2)");
            }
            catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or IOException)
            {
                if (cancellationToken.IsCancellationRequested)
                    throw new OperationCanceledException(cancellationToken);
                _options.Log?.Invoke($"[{_options.DisplayName}] export batch of {page.Count} span(s) failed: {ex.Message} (attempt {attempt}/2)");
            }
        }

        return false;
    }

    /// <summary>Releases the HTTP client.</summary>
    public void Dispose() => _http.Dispose();
}
