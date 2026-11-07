using System.Collections.Concurrent;
using System.Net;

namespace Kronikol.Tracking;

/// <summary>
/// A log entry queued for deferred flushing. Used with <see cref="DeferredLogFlushHandler"/>
/// when request/response logging must be delayed until test identity is available.
/// </summary>
public record PendingLogEntry(
    string ServiceName,
    string CallerName,
    OneOf<HttpMethod, string> Method,
    string? RequestContent,
    string? ResponseContent,
    Uri Uri,
    HttpStatusCode StatusCode = HttpStatusCode.OK,
    string? ActivityTraceId = null,
    string? ActivitySpanId = null,
    string? DependencyCategory = null)
{
    internal DateTimeOffset Timestamp { get; } = DateTimeOffset.UtcNow;

    /// <summary>The answer's status when it is not <see cref="StatusCode"/>'s: a call that threw, <c>!InvalidOperationException</c>.</summary>
    internal OneOf<HttpStatusCode, string>? Status { get; init; }

    /// <summary>Why the call failed (<see cref="RequestResponseLog.Error"/>).</summary>
    internal string? Error { get; init; }

    /// <summary>The answer's history mark (<see cref="RequestResponseLog.RecordedBefore12"/>).</summary>
    internal RecordedBefore? ResponseBefore12 { get; init; }
}

/// <summary>
/// Thread-safe queue for deferring request/response log entries until test identity is available.
/// Used with <see cref="DeferredLogFlushHandler"/>.
/// </summary>
public static class PendingRequestResponseLogs
{
    private static readonly ConcurrentQueue<PendingLogEntry> Pending = new();

    public static void Enqueue(PendingLogEntry entry) => Pending.Enqueue(entry);

    public static int Count => Pending.Count;

    public static void FlushAll(string testName, string testId) => FlushAll(testName, testId, afterAThrow: false);

    /// <summary>
    /// <see cref="FlushAll(string, string)"/>, for a flush after a send that threw (<paramref name="afterAThrow"/>): before #232
    /// that send flushed nothing, so its calls went to the next send that succeeded, under that send's test, which history's
    /// earlier rules still read as no call under this one (<see cref="RequestResponseLog.RecordedBefore12"/>).
    /// </summary>
    internal static void FlushAll(string testName, string testId, bool afterAThrow)
    {
        while (Pending.TryDequeue(out var entry))
        {
            var traceId = Guid.NewGuid();
            var requestResponseId = Guid.NewGuid();

            RequestResponseLogger.Log(new RequestResponseLog(
                testName, testId, entry.Method, entry.RequestContent, entry.Uri,
                [], entry.ServiceName, entry.CallerName, RequestResponseType.Request,
                traceId, requestResponseId, false,
                DependencyCategory: entry.DependencyCategory)
            {
                Timestamp = entry.Timestamp,
                ActivityTraceId = entry.ActivityTraceId,
                ActivitySpanId = entry.ActivitySpanId,
                RecordedBefore12 = afterAThrow ? RecordedBefore.NotRecorded : null
            });

            RequestResponseLogger.Log(new RequestResponseLog(
                testName, testId, entry.Method, entry.ResponseContent, entry.Uri,
                [], entry.ServiceName, entry.CallerName, RequestResponseType.Response,
                traceId, requestResponseId, false,
                entry.Status ?? (OneOf<HttpStatusCode, string>)entry.StatusCode,
                DependencyCategory: entry.DependencyCategory)
            {
                Timestamp = entry.Timestamp,
                ActivityTraceId = entry.ActivityTraceId,
                ActivitySpanId = entry.ActivitySpanId,
                Error = entry.Error,
                RecordedBefore12 = entry.ResponseBefore12
            });
        }
    }

    public static void Clear()
    {
        while (Pending.TryDequeue(out _)) { }
    }
}
