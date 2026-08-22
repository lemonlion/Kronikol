using System.Net;
using Kronikol.Ingestion;
using Kronikol.Tracking;

namespace Kronikol.Extensions.Otlp;

/// <summary>
/// One span turned into the Kronikol call it stands for: who called whom, with what label, over what
/// interval. Materialise it as the request/response pair that renders as a single arrow with
/// <see cref="ToRecords"/> (NDJSON) or <see cref="ToLogs"/> (in-process store / any
/// <see cref="IRequestResponseSink"/>).
/// </summary>
/// <param name="TestId">The scenario the call belongs to — the span's trace id on the exact-attribution path.</param>
/// <param name="TestName">Display name of the test (cosmetic; the tests file wins at ingest).</param>
/// <param name="Method">The arrow label (<c>Find ← Trial</c>, <c>GET</c>, <c>SELECT</c>).</param>
/// <param name="Uri">The call's URI (<c>mongodb:///db/Trial</c>, <c>redis://db0/key</c>, the HTTP URL).</param>
/// <param name="ServiceName">The receiving participant.</param>
/// <param name="CallerName">The calling participant (the span's <c>service.name</c>, mapped).</param>
/// <param name="RequestContent">The statement/query, capped — null when the producer does not capture it.</param>
/// <param name="ResponseContent">The status message on a failed span, else null (spans carry no payload).</param>
/// <param name="StatusCode">HTTP status, <c>OK</c>, or <c>500</c> for a span whose status is ERROR.</param>
/// <param name="DependencyCategory">A <c>Kronikol.Constants.DependencyCategories</c> value, or null for a plain HTTP service.</param>
/// <param name="Start">Span start (<c>startTimeUnixNano</c>).</param>
/// <param name="End">Span end (<c>endTimeUnixNano</c>) — the response timestamp, so call-tree nesting and durations work.</param>
/// <param name="TraceId">W3C trace id of the span.</param>
/// <param name="SpanId">W3C span id of the span.</param>
public sealed record MappedSpan(
    string TestId,
    string? TestName,
    string Method,
    string Uri,
    string ServiceName,
    string CallerName,
    string? RequestContent,
    string? ResponseContent,
    string? StatusCode,
    string? DependencyCategory,
    DateTimeOffset Start,
    DateTimeOffset End,
    string TraceId,
    string SpanId)
{
    /// <summary>How long the call took, in milliseconds.</summary>
    public double DurationMs => (End - Start).TotalMilliseconds;

    /// <summary>
    /// The <see cref="InteractionRecord"/> pair for this call: request at <see cref="Start"/>, response at
    /// <see cref="End"/>, both stamped <c>capturedBy: span</c> so the ingest-time merge
    /// (<see cref="InteractionMerger"/>) can tell them from a wire capture of the same call.
    /// </summary>
    public (InteractionRecord Request, InteractionRecord Response) ToRecords(string? phase = null)
    {
        var (request, response) = InteractionRecord.Pair(
            TestId, TestName, Method, Uri, ServiceName, CallerName,
            requestContent: RequestContent,
            responseContent: ResponseContent,
            statusCode: StatusCode,
            requestTimestamp: Start,
            responseTimestamp: End,
            requestResponseId: SpanId,
            traceId: TraceId,
            dependencyCategory: DependencyCategory,
            phase: phase,
            activityTraceId: TraceId,
            activitySpanId: SpanId);

        return (request with { CapturedBy = InteractionMerger.SpanSource },
                response with { CapturedBy = InteractionMerger.SpanSource });
    }

    /// <summary>The same pair as <see cref="RequestResponseLog"/> entries, ready for any <see cref="IRequestResponseSink"/>.</summary>
    public (RequestResponseLog Request, RequestResponseLog Response) ToLogs(TestPhase phase = TestPhase.Unknown)
    {
        var requestResponseId = InteractionRecord.ToGuid(SpanId);
        var traceId = InteractionRecord.ToGuid(TraceId);
        var uri = System.Uri.TryCreate(Uri, UriKind.Absolute, out var absolute) ? absolute : new Uri("http://unknown/");
        var name = TestName ?? TestIdentityScope.UnknownTestName;

        OneOf<HttpStatusCode, string>? status = null;
        if (!string.IsNullOrWhiteSpace(StatusCode))
            status = int.TryParse(StatusCode, out var numeric) ? (HttpStatusCode)numeric : StatusCode;

        var request = new RequestResponseLog(
            name, TestId, InteractionRecord.ParseMethod(Method), RequestContent, uri, [],
            ServiceName, CallerName, RequestResponseType.Request, traceId, requestResponseId, false,
            DependencyCategory: DependencyCategory)
        {
            Timestamp = Start,
            ActivityTraceId = TraceId,
            ActivitySpanId = SpanId,
            Phase = phase,
            CapturedBy = InteractionMerger.SpanSource,
        };

        var response = new RequestResponseLog(
            name, TestId, InteractionRecord.ParseMethod(Method), ResponseContent, uri, [],
            ServiceName, CallerName, RequestResponseType.Response, traceId, requestResponseId, false,
            status, DependencyCategory: DependencyCategory)
        {
            Timestamp = End,
            ActivityTraceId = TraceId,
            ActivitySpanId = SpanId,
            Phase = phase,
            CapturedBy = InteractionMerger.SpanSource,
        };

        return (request, response);
    }
}
