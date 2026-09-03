using Google.Cloud.Spanner.V1;
using Grpc.Core;

namespace Kronikol.Extensions.Spanner;

/// <summary>
/// Wraps an IAsyncStreamReader to accumulate PartialResultSet messages
/// and log response content after stream completion.
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
        catch (RpcException)
        {
            if (!_logged)
            {
                _logged = true;
                LogStreamResponse();
            }
            throw;
        }
    }

    /// <summary>
    /// Logs accumulated response content if not already logged.
    /// Called on stream completion, disposal, or error.
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

        var content = SpannerResponseFormatter.FormatPartialResultSets(
            _chunks, _options.ResolveResponseDetail(), _options.MaxResponseRows);

        _tracker.LogResponse(_opInfo, _reqId, _traceId, content);
        _chunks.Clear();
    }
}
