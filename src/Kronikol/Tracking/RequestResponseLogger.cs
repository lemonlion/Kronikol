using System.Collections.Concurrent;
using System.Net;

namespace Kronikol.Tracking;

/// <summary>
/// Central static store for all tracked request/response log entries.
/// All tracking extensions ultimately write to this static collection, which is consumed
/// by report generators to produce sequence diagrams and other visualisations.
/// </summary>
public static class RequestResponseLogger
{
    private static readonly ConcurrentQueue<RequestResponseLog> RequestsAndResponses = new();

    /// <summary>
    /// When set, content longer than this value is truncated at capture time.
    /// The truncated content includes a marker showing the original size.
    /// Default is <c>null</c> (no limit).
    /// </summary>
    public static int? MaxContentLength { get; set; }

    /// <summary>
    /// Capture-time redaction applied to every entry before it is stored (see <see cref="CaptureRedaction"/>).
    /// Default <c>null</c> (no redaction). Set to <c>CaptureRedaction.Secrets()</c> to keep credential
    /// headers out of the in-memory store and every data file derived from it. This is the security
    /// boundary; <c>ReportConfigurationOptions.ExcludedHeaders</c> only hides headers in the diagram.
    /// </summary>
    public static CaptureRedaction? Redaction { get; set; }

    public static void Log(RequestResponseLog log)
    {
        if (Redaction is { } redaction)
        {
            var redacted = redaction.Apply(log);
            if (redacted is null)
                return;
            log = redacted;
        }

        if (MaxContentLength is { } max && log.Content is { Length: var len } && len > max)
            log = log with { Content = $"{log.Content[..max]}\n\n…truncated ({len} chars total)" };

        RequestsAndResponses.Enqueue(log);
    }

    public static RequestResponseLog[] RequestAndResponseLogs => RequestsAndResponses.ToArray();
    public static void Clear() => RequestsAndResponses.Clear();

    /// <summary>
    /// Logs a matched request/response pair sharing the same TraceId and RequestResponseId.
    /// Useful for recording interactions that are not captured by the HTTP pipeline
    /// (e.g. in-process calls, Cosmos, Redis, MediatR, blob storage).
    /// </summary>
    public static void LogPair(
        string testName,
        string testId,
        OneOf<HttpMethod, string> method,
        Uri uri,
        string serviceName,
        string callerName,
        string? requestContent = null,
        string? responseContent = null,
        HttpStatusCode? statusCode = null,
        TestPhase phase = TestPhase.Unknown,
        string? dependencyCategory = null)
    {
        var traceId = Guid.NewGuid();
        var requestResponseId = Guid.NewGuid();
        var now = DateTimeOffset.UtcNow;

        Log(new RequestResponseLog(testName, testId, method, requestContent, uri,
            [], serviceName, callerName, RequestResponseType.Request, traceId, requestResponseId, false,
            DependencyCategory: dependencyCategory)
        {
            Timestamp = now,
            Phase = phase
        });

        Log(new RequestResponseLog(testName, testId, method, responseContent, uri,
            [], serviceName, callerName, RequestResponseType.Response, traceId, requestResponseId, false,
            statusCode is not null ? (OneOf<HttpStatusCode, string>)statusCode.Value : null,
            DependencyCategory: dependencyCategory)
        {
            Timestamp = now,
            Phase = phase
        });
    }

    /// <summary>
    /// Logs a matched request/response pair with automatic test identity resolution.
    /// Resolves the current test via <paramref name="testInfoFetcher"/> (if provided),
    /// then falls back to <see cref="TestIdentityScope.Current"/>.
    /// If neither resolves, the call is silently skipped.
    /// </summary>
    /// <param name="method">The operation type. Use <see cref="HttpMethod"/> for HTTP operations, or a string for custom labels.</param>
    /// <param name="uri">A URI identifying the target resource.</param>
    /// <param name="serviceName">The target service name as it should appear in diagrams.</param>
    /// <param name="callerName">The calling service name as it should appear in diagrams.</param>
    /// <param name="testInfoFetcher">Optional delegate that returns the current test's (Name, Id). Falls back to <see cref="TestIdentityScope.Current"/>.</param>
    /// <param name="requestContent">Content to display as the request body.</param>
    /// <param name="responseContent">Content to display as the response body.</param>
    /// <param name="statusCode">HTTP status code for diagram colouring, or null for non-HTTP operations.</param>
    /// <param name="phase">The test phase (default: <see cref="TestPhase.Unknown"/>).</param>
    /// <param name="dependencyCategory">Optional dependency category for diagram grouping.</param>
    public static void LogPair(
        OneOf<HttpMethod, string> method,
        Uri uri,
        string serviceName,
        string callerName,
        Func<(string Name, string Id)>? testInfoFetcher = null,
        string? requestContent = null,
        string? responseContent = null,
        HttpStatusCode? statusCode = null,
        TestPhase phase = TestPhase.Unknown,
        string? dependencyCategory = null)
    {
        (string Name, string Id)? testInfo = null;

        if (testInfoFetcher is not null)
        {
            try
            {
                var info = testInfoFetcher();
                if (!string.Equals(info.Id, TestIdentityScope.UnknownTestId, StringComparison.OrdinalIgnoreCase))
                    testInfo = info;
            }
            catch { /* Delegate threw — fall through to scope */ }
        }

        testInfo ??= TestIdentityScope.Current;

        if (testInfo is null)
            return;

        LogPair(testInfo.Value.Name, testInfo.Value.Id, method, uri, serviceName, callerName,
            requestContent, responseContent, statusCode, phase, dependencyCategory);
    }
}