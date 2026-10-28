using Google.Cloud.Spanner.V1;
using Grpc.Core;

namespace Kronikol.Extensions.Spanner;

/// <summary>
/// Wraps a streamed call's IAsyncStreamReader: logs the response when the stream ends, with the PartialResultSet rows
/// read while LogResponseContent is on, or the failure when a read throws.
/// </summary>
internal sealed class TrackingAsyncStreamReader<TResponse> : IAsyncStreamReader<TResponse>
{
    private readonly IAsyncStreamReader<TResponse> _inner;
    private readonly SpannerTracker _tracker;
    private readonly SpannerTrackingOptions _options;
    private readonly SpannerOperationInfo _opInfo;
    private readonly Guid _reqId;
    private readonly Guid _traceId;
    private readonly List<PartialResultSet> _chunks = [];
    private bool _logged;

    public TrackingAsyncStreamReader(
        IAsyncStreamReader<TResponse> inner,
        SpannerTracker tracker,
        SpannerTrackingOptions options,
        SpannerOperationInfo opInfo,
        Guid reqId, Guid traceId)
    {
        _inner = inner;
        _tracker = tracker;
        _options = options;
        _opInfo = opInfo;
        _reqId = reqId;
        _traceId = traceId;
    }

    public TResponse Current => _inner.Current;

    public async Task<bool> MoveNext(CancellationToken cancellationToken)
    {
        try
        {
            var hasNext = await _inner.MoveNext(cancellationToken);

            if (hasNext)
            {
                if (_inner.Current is PartialResultSet prs && _options.LogResponseContent)
                    _chunks.Add(prs);
            }
            else if (!_logged)
            {
                _logged = true;
                LogStreamResponse();
            }

            return hasNext;
        }
        catch (RpcException ex)
        {
            // A read that fails is drawn as a unary call that fails is: status Error, with the call's message.
            if (!_logged)
            {
                _logged = true;
                _chunks.Clear();
                _tracker.LogFailure(_opInfo, _reqId, _traceId, ex);
            }
            throw;
        }
    }

    /// <summary>
    /// Logs the response with the rows read so far if it is not logged yet. Called when the call is disposed, so a
    /// stream left before its end is still answered.
    /// </summary>
    internal void LogIfNotAlreadyLogged()
    {
        if (_logged) return;
        _logged = true;
        LogStreamResponse();
    }

    private void LogStreamResponse()
    {
        if (!_options.LogResponseContent || _chunks.Count == 0)
        {
            _tracker.LogResponse(_opInfo, _reqId, _traceId, null);
            return;
        }

        // Each note is made at the detail its own verbosity resolves to, from the rows read once.
        _tracker.LogResponseAtDetail(_opInfo, _reqId, _traceId,
            detail => SpannerResponseFormatter.FormatPartialResultSets(_chunks, detail, _options.MaxResponseRows));
        _chunks.Clear();
    }
}
