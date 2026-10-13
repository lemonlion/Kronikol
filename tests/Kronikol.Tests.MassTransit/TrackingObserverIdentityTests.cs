using System.Reflection;
using MassTransit;
using Kronikol.Constants;
using Kronikol.Extensions.MassTransit;
using Kronikol.Tests.Tracking;

namespace Kronikol.Tests.MassTransit;

/// <summary>
/// A message sent or published while a host serves a request carries the scenario its send is logged under, so the
/// consumer that handles it lands in the same one.
/// </summary>
public class TrackingObserverIdentityTests
{
    private readonly string _testId = Guid.NewGuid().ToString();

    private MassTransitTrackingOptions MakeOptions() => new()
    {
        ServiceName = "MassTransit",
        CallerName = "TestCaller",
        CurrentTestInfoFetcher = () => ("My MassTransit Test", _testId),
    };

    public record OrderPlaced(string OrderId);

    [Fact]
    public async Task PreSend_stamps_the_trackers_scenario_when_the_tracker_was_given_an_accessor()
    {
        var headerTestId = Guid.NewGuid().ToString();
        var options = MakeOptions();
        var observer = new TrackingSendObserver(
            new MassTransitTracker(options, RequestHeaderAccessor.For("Header Test", headerTestId)), options);
        var headers = new Dictionary<string, object?>();

        await observer.PreSend(ContextProxy.Create<SendContext<OrderPlaced>>(new OrderPlaced("1"), headers));

        Assert.Equal("Header Test", headers[TestTrackingMessageHeaders.TestName]);
        Assert.Equal(headerTestId, headers[TestTrackingMessageHeaders.TestId]);
    }

    [Fact]
    public async Task PrePublish_stamps_the_trackers_scenario_when_the_tracker_was_given_an_accessor()
    {
        var headerTestId = Guid.NewGuid().ToString();
        var options = MakeOptions();
        var observer = new TrackingPublishObserver(
            new MassTransitTracker(options, RequestHeaderAccessor.For("Header Test", headerTestId)), options);
        var headers = new Dictionary<string, object?>();

        await observer.PrePublish(ContextProxy.Create<PublishContext<OrderPlaced>>(new OrderPlaced("1"), headers));

        Assert.Equal("Header Test", headers[TestTrackingMessageHeaders.TestName]);
        Assert.Equal(headerTestId, headers[TestTrackingMessageHeaders.TestId]);
    }

    [Fact]
    public async Task PreSend_stamps_the_requests_scenario_when_the_options_carry_an_accessor()
    {
        var headerTestId = Guid.NewGuid().ToString();
        var options = MakeOptions();
        options.HttpContextAccessor = RequestHeaderAccessor.For("Header Test", headerTestId);
        var observer = new TrackingSendObserver(new MassTransitTracker(options), options);
        var headers = new Dictionary<string, object?>();

        await observer.PreSend(ContextProxy.Create<SendContext<OrderPlaced>>(new OrderPlaced("1"), headers));

        Assert.Equal(headerTestId, headers[TestTrackingMessageHeaders.TestId]);
    }
}

/// <summary>
/// A send or publish context whose headers record what is set on them; every other member answers its default.
/// </summary>
public class ContextProxy : DispatchProxy
{
    private object? _message;
    private Dictionary<string, object?> _headers = [];

    public static T Create<T>(object message, Dictionary<string, object?> headers) where T : class
    {
        var proxy = Create<T, ContextProxy>();
        var self = (ContextProxy)(object)proxy;
        self._message = message;
        self._headers = headers;
        return proxy;
    }

    protected override object? Invoke(MethodInfo? targetMethod, object?[]? args) => targetMethod?.Name switch
    {
        "get_Headers" => HeadersProxy.Create(_headers),
        "get_Message" => _message,
        _ => DefaultOf(targetMethod?.ReturnType),
    };

    internal static object? DefaultOf(Type? type)
        => type is { IsValueType: true } && type != typeof(void) ? Activator.CreateInstance(type) : null;
}

/// <summary>Send headers that write every <c>Set</c> into a dictionary.</summary>
public class HeadersProxy : DispatchProxy
{
    private Dictionary<string, object?> _values = [];

    public static SendHeaders Create(Dictionary<string, object?> values)
    {
        var proxy = Create<SendHeaders, HeadersProxy>();
        ((HeadersProxy)(object)proxy)._values = values;
        return proxy;
    }

    protected override object? Invoke(MethodInfo? targetMethod, object?[]? args)
    {
        if (targetMethod?.Name == "Set" && args is { Length: >= 2 } && args[0] is string key)
            _values[key] = args[1];
        return ContextProxy.DefaultOf(targetMethod?.ReturnType);
    }
}
