using Kronikol.CallGraph;

namespace Kronikol.Tests.CallGraph;

/// <summary>
/// Hand-written call records in the order a capture would record them, so a fact can open a request, make calls inside
/// it and answer it afterwards, as nesting reads it. Each scenario numbers its own records, responses included, as the
/// data file's list does.
/// </summary>
internal sealed class Records
{
    private readonly List<CallRecord> _all = [];
    private readonly Dictionary<int, int> _next = [];
    private int _nextBackground;
    private int _ids;

    public IReadOnlyList<CallRecord> All => _all;

    public static CallScenario Scenario(int ordinal, string name, string feature = "Breakfast", params string[] tags) => new()
    {
        Ordinal = ordinal,
        StableId = $"{ordinal:x16}",
        Feature = feature,
        Name = name,
        Tags = tags,
        Result = "Passed"
    };

    /// <summary>Opens a call: its request record. Answer it with <see cref="Answer"/>.</summary>
    public CallRecord Request(CallScenario? scenario, string caller, string service, string method, string uri,
        string? attribution = null, string? phase = null, string? category = null, string? callerCategory = null,
        bool isEvent = false, string? trace = null, string? content = null, bool userAction = false, string? testDouble = null)
    {
        var request = new CallRecord
        {
            Scenario = scenario,
            Position = NextPosition(scenario),
            IsRequest = true,
            Caller = caller,
            Service = service,
            Method = method,
            Uri = uri,
            Category = category,
            CallerCategory = callerCategory,
            IsEvent = isEvent,
            Phase = phase,
            AttributionSource = attribution,
            RequestResponseId = $"call-{++_ids}",
            TraceId = trace,
            IsUserAction = userAction,
            TestDouble = testDouble,
            Content = content
        };
        _all.Add(request);
        return request;
    }

    /// <summary>Answers an open call with its response record.</summary>
    public CallRecord Answer(CallRecord request, string? status = "200", bool isError = false)
    {
        var response = request with
        {
            Position = NextPosition(request.Scenario),
            IsRequest = false,
            Status = status,
            IsError = isError,
            Content = null
        };
        _all.Add(response);
        return response;
    }

    /// <summary>A call answered at once: its request, then its response.</summary>
    public CallRecord Call(CallScenario? scenario, string caller, string service, string method, string uri,
        string? attribution = null, string? phase = null, string? category = null, string? callerCategory = null,
        bool isEvent = false, string? trace = null, string? content = null, bool userAction = false, string? testDouble = null,
        string? status = "200", bool isError = false)
    {
        var request = Request(scenario, caller, service, method, uri, attribution, phase, category, callerCategory, isEvent, trace, content, userAction, testDouble);
        Answer(request, status, isError);
        return request;
    }

    private int NextPosition(CallScenario? scenario)
    {
        if (scenario is null)
            return _nextBackground++;
        var position = _next.GetValueOrDefault(scenario.Ordinal);
        _next[scenario.Ordinal] = position + 1;
        return position;
    }
}
