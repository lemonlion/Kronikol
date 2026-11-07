using Kronikol.Constants;
using System.Diagnostics;
using System.Net;
using System.Reflection;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Http;
using Kronikol.InternalFlow;

namespace Kronikol.Tracking;

/// <summary>
/// Configuration for <see cref="TrackingProxy{T}"/>, controlling how proxied method calls
/// are logged as request/response pairs in sequence diagrams.
/// </summary>
public record TrackingProxyOptions
{
    public required string ServiceName { get; init; }

    /// <summary>The participant name for the calling service in diagrams.</summary>
    public string CallerName { get; init; } = TrackingDefaults.CallerName;

    /// <summary>Use <see cref="CallerName"/> instead.</summary>
    [Obsolete("Use CallerName instead. CallingServiceName will be removed in a future version.")]
    public string CallingServiceName { get => CallerName; init => CallerName = value; }
    public string? ActivitySourceName { get; init; }
    public string UriScheme { get; init; } = "mock";
    public TrackingSerializerOptions? SerializerOptions { get; init; }
    public TrackingLogMode LogMode { get; init; } = TrackingLogMode.Immediate;
    public Func<(string Name, string Id)>? CurrentTestInfoFetcher { get; init; }
    public IHttpContextAccessor? HttpContextAccessor { get; init; }

    /// <summary>When <c>false</c>, calls during the Setup phase are not tracked. Default: <c>true</c>.</summary>
    public bool TrackDuringSetup { get; init; } = true;

    /// <summary>When <c>false</c>, calls during the Action phase are not tracked. Default: <c>true</c>.</summary>
    public bool TrackDuringAction { get; init; } = true;

    /// <summary>
    /// The dependency category string used for component diagram participant shapes and colours.
    /// Examples: <c>"Email"</c>, <c>"SMTP"</c>, <c>"gRPC"</c>, <c>"FTP"</c>.
    /// When <c>null</c>, defaults to HTTP API styling.
    /// </summary>
    public string? DependencyCategory { get; init; }
}

/// <summary>
/// Controls when <see cref="TrackingProxy{T}"/> logs request/response pairs.
/// </summary>
public enum TrackingLogMode
{
    /// <summary>Logs are written immediately when the proxied method completes.</summary>
    Immediate,

    /// <summary>Logs are queued and flushed later by <see cref="DeferredLogFlushHandler"/>.</summary>
    Deferred
}

/// <summary>
/// A <see cref="DispatchProxy"/>-based tracking proxy that intercepts all method calls on
/// interface <typeparamref name="T"/>, logging each invocation as a request/response pair for
/// inclusion in sequence diagrams. Used by MediatR, DispatchProxy, and custom tracking extensions.
/// </summary>
/// <typeparam name="T">The interface type being proxied.</typeparam>
public partial class TrackingProxy<T> : DispatchProxy where T : class
{
    private T _target = null!;
    private TrackingProxyOptions _options = null!;
    private string _sanitisedHostname = null!;
    private ActivitySource? _activitySource;

    public static T Create(T target, TrackingProxyOptions options)
    {
        var proxy = Create<T, TrackingProxy<T>>() as TrackingProxy<T>;
        proxy!._target = target;
        proxy._options = options;
        proxy._sanitisedHostname = SanitiseForUriHostname(options.ServiceName);
        if (options.ActivitySourceName is not null)
            proxy._activitySource = new ActivitySource(options.ActivitySourceName);
        return (proxy as T)!;
    }

    internal static string SanitiseForUriHostname(string name)
        => InvalidHostnameCharsRegex().Replace(name, "-").Trim('-');

    [GeneratedRegex(@"[^a-zA-Z0-9._~-]")]
    private static partial Regex InvalidHostnameCharsRegex();

    protected override object? Invoke(MethodInfo? targetMethod, object?[]? args)
    {
        if (targetMethod is null) return null;

        // Stamped on the request record, so a proxied call is timed from here to its end: until 4.7.3 both records were
        // stamped when it ended, and every proxied call took 0 ms.
        var startedAt = DateTimeOffset.UtcNow;
        var methodName = targetMethod.Name;
        var uri = new Uri($"{_options.UriScheme}://{_sanitisedHostname}/{typeof(T).Name}/{methodName}");
        var requestContent = args is { Length: > 0 }
            ? TrackingSafeSerializer.Serialize(args, _options.SerializerOptions)
            : null;

        // The target runs under the span, but an async target returns before the span ends: the caller's own current
        // span is given back then, or the proxy's would stay current in the caller's flow and parent its next call.
        var callerActivity = Activity.Current;
        Activity? activity = null;
        if (_activitySource is not null)
        {
            var parentContext = TrackingTraceContext.CreateParentContext();
            activity = parentContext == default
                ? _activitySource.StartActivity(methodName, ActivityKind.Internal)
                : _activitySource.StartActivity(methodName, ActivityKind.Internal, parentContext);
        }

        try
        {
            // The target's own exception, its stack as it was: MethodInfo.Invoke wraps one in a TargetInvocationException,
            // which was unwrapped with `throw ex.InnerException;` and so lost the frames that threw it (FAILED_CALLS_PLAN.md F17).
            var result = targetMethod.Invoke(_target, BindingFlags.DoNotWrapExceptions, null, args, null);

            if (result is Task task)
            {
                var handled = HandleAsyncResult(task, targetMethod, methodName, uri, requestContent, activity, startedAt);
                if (activity is not null)
                    Activity.Current = callerActivity;
                return handled;
            }

            // A ValueTask is a struct, boxed here: logged as it was, the call ended when the method returned, its response
            // was the struct (whose Result blocks until the call ends) and a failure after that was a success (until
            // 4.16.3). It is awaited as the Task it wraps, and handed back as a ValueTask of that Task.
            if (result is ValueTask valueTask)
                return new ValueTask(HandleAsyncResultVoid(valueTask.AsTask(), methodName, uri, requestContent, activity, startedAt));
            if (targetMethod.ReturnType is { IsGenericType: true } valueTaskType && valueTaskType.GetGenericTypeDefinition() == typeof(ValueTask<>))
                return typeof(TrackingProxy<T>)
                    .GetMethod(nameof(HandleValueTaskTyped), BindingFlags.NonPublic | BindingFlags.Instance)!
                    .MakeGenericMethod(valueTaskType.GetGenericArguments()[0])
                    .Invoke(this, [result, methodName, uri, requestContent, activity, startedAt]);

            // A stream is answered when its enumeration ends, or fails, where it was answered as it was returned, with the
            // iterator's type name as its note (FAILED_CALLS_PLAN.md F24).
            if (targetMethod.ReturnType is { IsGenericType: true } streamType && streamType.GetGenericTypeDefinition() == typeof(IAsyncEnumerable<>) && result is not null)
                return typeof(TrackingProxy<T>)
                    .GetMethod(nameof(TrackStream), BindingFlags.NonPublic | BindingFlags.Instance)!
                    .MakeGenericMethod(streamType.GetGenericArguments()[0])
                    .Invoke(this, BindingFlags.DoNotWrapExceptions, null, [result, methodName, uri, requestContent, activity, startedAt, CancellationToken.None], null);

            var responseContent = TrackingSafeSerializer.Serialize(result, _options.SerializerOptions);
            LogInteraction(methodName, uri, requestContent, responseContent, HttpStatusCode.OK, activity, startedAt);
            InternalFlowSpanStore.Complete(activity);
            return result;
        }
        catch (Exception ex)
        {
            LogFailure(methodName, uri, requestContent, ex, activity, startedAt, HttpStatusCode.InternalServerError);
            throw;
        }
    }

    private async Task<TResult> HandleAsyncResultTyped<TResult>(
        Task<TResult> task, string methodName, Uri uri, string? requestContent, Activity? activity, DateTimeOffset startedAt)
    {
        try
        {
            var result = await task;
            var responseContent = TrackingSafeSerializer.Serialize(result, _options.SerializerOptions);
            LogInteraction(methodName, uri, requestContent, responseContent, HttpStatusCode.OK, activity, startedAt);
            InternalFlowSpanStore.Complete(activity);
            return result;
        }
        catch (Exception ex)
        {
            LogFailure(methodName, uri, requestContent, ex, activity, startedAt, HttpStatusCode.InternalServerError);
            throw;
        }
    }

    private ValueTask<TResult> HandleValueTaskTyped<TResult>(
        ValueTask<TResult> valueTask, string methodName, Uri uri, string? requestContent, Activity? activity, DateTimeOffset startedAt) =>
        new(HandleAsyncResultTyped(valueTask.AsTask(), methodName, uri, requestContent, activity, startedAt));

    private async Task HandleAsyncResultVoid(
        Task task, string methodName, Uri uri, string? requestContent, Activity? activity, DateTimeOffset startedAt)
    {
        try
        {
            await task;
            LogInteraction(methodName, uri, requestContent, null, HttpStatusCode.OK, activity, startedAt);
            InternalFlowSpanStore.Complete(activity);
        }
        catch (Exception ex)
        {
            LogFailure(methodName, uri, requestContent, ex, activity, startedAt, HttpStatusCode.InternalServerError);
            throw;
        }
    }

    private object? HandleAsyncResult(Task task, MethodInfo targetMethod, string methodName,
        Uri uri, string? requestContent, Activity? activity, DateTimeOffset startedAt)
    {
        var returnType = targetMethod.ReturnType;

        if (returnType.IsGenericType && returnType.GetGenericTypeDefinition() == typeof(Task<>))
        {
            var resultType = returnType.GetGenericArguments()[0];
            var method = typeof(TrackingProxy<T>)
                .GetMethod(nameof(HandleAsyncResultTyped), BindingFlags.NonPublic | BindingFlags.Instance)!
                .MakeGenericMethod(resultType);
            return method.Invoke(this, [task, methodName, uri, requestContent, activity, startedAt]);
        }

        return HandleAsyncResultVoid(task, methodName, uri, requestContent, activity, startedAt);
    }

    private async IAsyncEnumerable<TItem> TrackStream<TItem>(IAsyncEnumerable<TItem> stream, string methodName, Uri uri,
        string? requestContent, Activity? activity, DateTimeOffset startedAt,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        var count = 0;
        var answered = false;
        IAsyncEnumerator<TItem> items;
        try
        {
            items = stream.GetAsyncEnumerator(cancellationToken);
        }
        catch (Exception ex)
        {
            LogFailure(methodName, uri, requestContent, ex, activity, startedAt, HttpStatusCode.OK);
            throw;
        }
        await using var _ = items;
        try
        {
            while (true)
            {
                bool more;
                try
                {
                    more = await items.MoveNextAsync();
                }
                catch (Exception ex)
                {
                    answered = true;
                    LogFailure(methodName, uri, requestContent, ex, activity, startedAt, HttpStatusCode.OK);
                    throw;
                }
                if (!more)
                    break;
                count++;
                yield return items.Current;
            }
        }
        finally
        {
            // Read to its end, or left by its caller: answered with how many items it gave.
            if (!answered)
            {
                LogInteraction(methodName, uri, requestContent,
                    count.ToString(System.Globalization.CultureInfo.InvariantCulture) + (count == 1 ? " item" : " items"),
                    HttpStatusCode.OK, activity, startedAt);
                InternalFlowSpanStore.Complete(activity);
            }
        }
    }

    // A call that threw: the exception's type where the status would be and its message chain as the error (FailedSend), its
    // type and message kept as the note. Before #232 the proxy made up a 500 for it, or answered a stream that failed as the
    // success it was returned as, which history's earlier rules still read: statusThen.
    private void LogFailure(string methodName, Uri uri, string? requestContent, Exception exception, Activity? activity,
        DateTimeOffset startedAt, HttpStatusCode statusThen)
    {
        var responseContent = $"{exception.GetType().Name}: {exception.Message}";
        activity?.SetStatus(ActivityStatusCode.Error, responseContent);
        LogInteraction(methodName, uri, requestContent, responseContent, FailedSend.Status(exception), activity, startedAt,
            FailedSend.Describe(exception), RecordedBefore.AnsweredWith(statusThen));
        InternalFlowSpanStore.Complete(activity);
    }

    private void LogInteraction(string methodName, Uri uri, string? requestContent,
        string? responseContent, OneOf<HttpStatusCode, string> statusCode, Activity? activity, DateTimeOffset startedAt,
        string? error = null, RecordedBefore? before12 = null)
    {
        if (!PhaseConfiguration.ShouldTrack(_options.TrackDuringSetup, _options.TrackDuringAction))
            return;

        if (_options.LogMode == TrackingLogMode.Deferred)
        {
            PendingRequestResponseLogs.Enqueue(new PendingLogEntry(
                _options.ServiceName, _options.CallerName,
                methodName, requestContent, responseContent, uri,
                statusCode.Value is HttpStatusCode code ? code : HttpStatusCode.InternalServerError,
                activity?.TraceId.ToString(), activity?.SpanId.ToString(),
                _options.DependencyCategory)
            {
                Status = statusCode,
                Error = error,
                ResponseBefore12 = before12
            });
            return;
        }

        var testInfo = TestInfoResolver.ResolveWithSource(_options.HttpContextAccessor, _options.CurrentTestInfoFetcher);
        if (testInfo is null) return;

        RequestResponseLogger.LogPair(
            testInfo.Value.Name, testInfo.Value.Id,
            (OneOf<HttpMethod, string>)methodName,
            uri,
            _options.ServiceName,
            _options.CallerName,
            requestContent,
            responseContent,
            statusCode,
            TestPhaseContext.Current,
            _options.DependencyCategory, testInfo.Value.Source,
            requestAt: startedAt, responseAt: DateTimeOffset.UtcNow,
            error: error, responseBefore12: before12);
    }
}
