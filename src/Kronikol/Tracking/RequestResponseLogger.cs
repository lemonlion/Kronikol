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

    // A test's own store (IsolateForTests). The report flows read the whole process's log, so a test asserting what a
    // run writes would otherwise read every other test's calls, which a run now reports as calls under an unknown id.
    private static readonly AsyncLocal<ConcurrentQueue<RequestResponseLog>?> IsolatedStore = new();

    private static ConcurrentQueue<RequestResponseLog> Store => IsolatedStore.Value ?? RequestsAndResponses;

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

    /// <summary>
    /// Whether a call that resolves to no scenario is captured under the background identity
    /// (<see cref="TestIdentityScope.UnknownIdentity"/>) instead of being dropped. Default <c>false</c>:
    /// today's behaviour. On, the report's background section shows what the system under test did on
    /// its own — a hosted service's polls, a consumer draining a queue — in no scenario's diagram.
    /// </summary>
    public static bool CaptureBackground { get; set; }

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

        // Only the HTTP handler and LogPair stamp the time; every capturer that builds its own log left it
        // null, and two thirds of a docker-lane report's dependency calls could not be placed in time.
        if (log.Timestamp is null)
            log = log with { Timestamp = DateTimeOffset.UtcNow };

        // A call the owner window answered for (DocumentOwnership) is held by its flow until an operation on
        // the owner's document confirms it; a foreign operation or a query drops it instead.
        if (log.AttributionSource == AttributionSource.DocumentFlow && TestIdentityScope.CurrentFlow is { } flow)
        {
            if (flow.TryHold(log))
                return;
            // The window closed between the call's resolution and its capture: the call is nobody's.
            if (!CaptureBackground)
                return;
            log = log with
            {
                TestId = TestIdentityScope.UnknownTestId,
                TestName = TestIdentityScope.UnknownTestName,
                AttributionSource = AttributionSource.Detached
            };
        }

        Store.Enqueue(log);
    }

    /// <summary>Stores an entry that has been through <see cref="Log"/> once already: a held call, confirmed or dropped.</summary>
    internal static void Enqueue(RequestResponseLog log) => Store.Enqueue(log);

    public static RequestResponseLog[] RequestAndResponseLogs => Store.ToArray();

    public static void Clear()
    {
        Store.Clear();
        DetachedFlow.ForgetAll();
    }

    /// <summary>
    /// For tests: until the returned scope is disposed, the calls logged on this flow, and the log a report reads on it,
    /// are a store of their own, empty to begin with. The process-wide store holds every other test's calls.
    /// </summary>
    internal static IDisposable IsolateForTests()
    {
        var previous = IsolatedStore.Value;
        IsolatedStore.Value = new ConcurrentQueue<RequestResponseLog>();
        return new Isolation(previous);
    }

    private sealed class Isolation(ConcurrentQueue<RequestResponseLog>? previous) : IDisposable
    {
        public void Dispose() => IsolatedStore.Value = previous;
    }

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
        string? dependencyCategory = null,
        AttributionSource? source = null)
    {
        // Logged after the call, so both records carry this one instant, which the data files read as a call that
        // measured nothing (no durationMs) rather than one that took no time.
        var now = DateTimeOffset.UtcNow;
        LogPair(testName, testId, method, uri, serviceName, callerName, requestContent, responseContent, statusCode, phase,
            dependencyCategory, source, requestAt: now, responseAt: now);
    }

    /// <summary>
    /// The same pair, stamped when the call started and when it ended, by a caller that saw both (the tracking proxy).
    /// </summary>
    internal static void LogPair(
        string testName,
        string testId,
        OneOf<HttpMethod, string> method,
        Uri uri,
        string serviceName,
        string callerName,
        string? requestContent,
        string? responseContent,
        HttpStatusCode? statusCode,
        TestPhase phase,
        string? dependencyCategory,
        AttributionSource? source,
        DateTimeOffset requestAt,
        DateTimeOffset responseAt)
    {
        var traceId = Guid.NewGuid();
        var requestResponseId = Guid.NewGuid();

        Log(new RequestResponseLog(testName, testId, method, requestContent, uri,
            [], serviceName, callerName, RequestResponseType.Request, traceId, requestResponseId, false,
            DependencyCategory: dependencyCategory)
        {
            Timestamp = requestAt,
            Phase = phase,
            AttributionSource = source
        });

        Log(new RequestResponseLog(testName, testId, method, responseContent, uri,
            [], serviceName, callerName, RequestResponseType.Response, traceId, requestResponseId, false,
            statusCode is not null ? (OneOf<HttpStatusCode, string>)statusCode.Value : null,
            DependencyCategory: dependencyCategory)
        {
            Timestamp = responseAt,
            Phase = phase,
            AttributionSource = source
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
        var who = TestInfoResolver.ResolveWithSource(null, testInfoFetcher);
        if (who is null)
            return;

        LogPair(who.Value.Name, who.Value.Id, method, uri, serviceName, callerName,
            requestContent, responseContent, statusCode, phase, dependencyCategory, who.Value.Source);
    }
}