namespace Kronikol.Tracking;

/// <summary>
/// A call whose request is logged and whose answer is not (plans/FAILED_CALLS_PLAN.md §5.2). Logging the request through
/// <see cref="Open"/> returns one, and the call then ends with exactly one answer: the tracker's own (<see cref="Answer"/>), or
/// the one the exception the call threw stands for (<see cref="Threw"/>), as <see cref="FailedSend"/> names it. Every tracker
/// that logs a request before its call ends goes through it, so a call that throws can no longer be left with a request alone,
/// and the answer cannot be filed under another test, phase or pair than the request it answers.
/// </summary>
internal sealed class OpenCall
{
    private int _answered;

    private OpenCall(RequestResponseLog request) => Request = request;

    /// <summary>The request, as the tracker built it, before the logger redacted, capped or stamped it.</summary>
    internal RequestResponseLog Request { get; }

    /// <summary>Whether the call has its answer.</summary>
    internal bool IsAnswered => Volatile.Read(ref _answered) == 1;

    /// <summary>Logs <paramref name="request"/> and opens its call.</summary>
    internal static OpenCall Open(RequestResponseLog request)
    {
        ArgumentNullException.ThrowIfNull(request);
        RequestResponseLogger.Log(request);
        return new OpenCall(request);
    }

    /// <summary>
    /// Logs the tracker's answer, unless the call is answered already. The answer is the request's: its test, its phase, how it
    /// got its scenario and its pair are the request's, read once, when it was logged, whatever the tracker read again since.
    /// </summary>
    internal void Answer(RequestResponseLog response)
    {
        ArgumentNullException.ThrowIfNull(response);
        if (Interlocked.Exchange(ref _answered, 1) == 1)
            return;
        RequestResponseLogger.Log(response with
        {
            TestName = Request.TestName,
            TestId = Request.TestId,
            Type = RequestResponseType.Response,
            TraceId = Request.TraceId,
            RequestResponseId = Request.RequestResponseId,
            Phase = Request.Phase,
            AttributionSource = Request.AttributionSource
        });
    }

    /// <summary>
    /// Logs the answer a call that threw has, unless the call is answered already: the exception's type where the status would be,
    /// its message chain in <see cref="RequestResponseLog.Error"/>, no body and no headers, and every field that names and pairs
    /// the call taken from the request (<see cref="FailedSend"/>). The caller rethrows with <c>throw;</c>, so the exception goes on
    /// untouched. It sets no history mark: only the tracker knows which rule first saw its answer, and passes it through
    /// <paramref name="adjust"/>, with anything else it knows about the answer.
    /// </summary>
    internal void Threw(Exception exception, Func<RequestResponseLog, RequestResponseLog>? adjust = null)
    {
        ArgumentNullException.ThrowIfNull(exception);
        if (Interlocked.Exchange(ref _answered, 1) == 1)
            return;
        var answer = new RequestResponseLog(
            Request.TestName,
            Request.TestId,
            Request.Method,
            null,
            Request.Uri,
            [],
            Request.ServiceName,
            Request.CallerName,
            RequestResponseType.Response,
            Request.TraceId,
            Request.RequestResponseId,
            Request.TrackingIgnore,
            FailedSend.Status(exception),
            Request.MetaType,
            Request.DependencyCategory,
            Request.CallerDependencyCategory)
        {
            Error = FailedSend.Describe(exception),
            Phase = Request.Phase,
            AttributionSource = Request.AttributionSource,
            ExpiredFromTestId = Request.ExpiredFromTestId,
            ActivitySpanId = Request.ActivitySpanId,
            ActivityTraceId = Request.ActivityTraceId,
            NamedByRemoteHostFallback = Request.NamedByRemoteHostFallback,
            TestDouble = Request.TestDouble,
            LabelIsFree = Request.LabelIsFree,
            SetupVariant = Unbodied(Request.SetupVariant),
            ActionVariant = Unbodied(Request.ActionVariant)
        };
        RequestResponseLogger.Log(adjust is null ? answer : adjust(answer));
    }

    /// <summary>
    /// The <c>adjust</c> of a tracker that answered no call that threw before #232: rules 11 and earlier read the answer as none
    /// (<see cref="RequestResponseLog.RecordedBefore12"/>), as they recorded none.
    /// </summary>
    internal static RequestResponseLog UnansweredBefore12(RequestResponseLog answer) => answer with { RecordedBefore12 = RecordedBefore.NotRecorded };

    // A phase's rendering of the answer: the request's method and URI, and no body or headers, as the call answered none.
    private static PhaseVariant? Unbodied(PhaseVariant? variant) => variant is null ? null : variant with { Content = null, Headers = [] };
}

/// <summary>
/// The calls a tracker has open, by their request's <see cref="RequestResponseLog.RequestResponseId"/>, for a tracker whose
/// command wrappers carry a call's ids rather than the call itself: the SQL family's and Spanner's. The answer is still the
/// call's, through <see cref="OpenCall"/>, so it is logged once, under the request's test and phase; taking the call ends its
/// place here.
/// </summary>
internal sealed class OpenCalls
{
    private readonly System.Collections.Concurrent.ConcurrentDictionary<Guid, OpenCall> _open = new();

    /// <summary>Logs <paramref name="request"/>, opens its call and holds it until it is taken.</summary>
    internal OpenCall Open(RequestResponseLog request)
    {
        var call = OpenCall.Open(request);
        _open[request.RequestResponseId] = call;
        return call;
    }

    /// <summary>The open call whose request has <paramref name="requestResponseId"/>, left open; null if none is.</summary>
    internal OpenCall? Find(Guid requestResponseId) => _open.TryGetValue(requestResponseId, out var call) ? call : null;

    /// <summary>The open call whose request has <paramref name="requestResponseId"/>, which no longer waits here; null if none does.</summary>
    internal OpenCall? Take(Guid requestResponseId) => _open.TryRemove(requestResponseId, out var call) ? call : null;
}
