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

        var methodName = targetMethod.Name;
        var uri = new Uri($"{_options.UriScheme}://{_sanitisedHostname}/{typeof(T).Name}/{methodName}");
        var requestContent = args is { Length: > 0 }
            ? TrackingSafeSerializer.Serialize(args, _options.SerializerOptions)
            : null;

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
            var result = targetMethod.Invoke(_target, args);

            if (result is Task task)
                return HandleAsyncResult(task, targetMethod, methodName, uri, requestContent, activity);

            var responseContent = TrackingSafeSerializer.Serialize(result, _options.SerializerOptions);
            LogInteraction(methodName, uri, requestContent, responseContent, HttpStatusCode.OK, activity);
            InternalFlowSpanStore.Complete(activity);
            return result;
        }
        catch (TargetInvocationException ex) when (ex.InnerException is not null)
        {
            var responseContent = $"{ex.InnerException.GetType().Name}: {ex.InnerException.Message}";
            activity?.SetStatus(ActivityStatusCode.Error, responseContent);
            LogInteraction(methodName, uri, requestContent, responseContent, HttpStatusCode.InternalServerError, activity);
            InternalFlowSpanStore.Complete(activity);
            throw ex.InnerException;
        }
        catch (Exception ex)
        {
            var responseContent = $"{ex.GetType().Name}: {ex.Message}";
            activity?.SetStatus(ActivityStatusCode.Error, responseContent);
            LogInteraction(methodName, uri, requestContent, responseContent, HttpStatusCode.InternalServerError, activity);
            InternalFlowSpanStore.Complete(activity);
            throw;
        }
    }

    private async Task<TResult> HandleAsyncResultTyped<TResult>(
        Task<TResult> task, string methodName, Uri uri, string? requestContent, Activity? activity)
    {
        try
        {
            var result = await task;
            var responseContent = TrackingSafeSerializer.Serialize(result, _options.SerializerOptions);
            LogInteraction(methodName, uri, requestContent, responseContent, HttpStatusCode.OK, activity);
            InternalFlowSpanStore.Complete(activity);
            return result;
        }
        catch (Exception ex)
        {
            var responseContent = $"{ex.GetType().Name}: {ex.Message}";
            activity?.SetStatus(ActivityStatusCode.Error, responseContent);
            LogInteraction(methodName, uri, requestContent, responseContent, HttpStatusCode.InternalServerError, activity);
            InternalFlowSpanStore.Complete(activity);
            throw;
        }
    }

    private async Task HandleAsyncResultVoid(
        Task task, string methodName, Uri uri, string? requestContent, Activity? activity)
    {
        try
        {
            await task;
            LogInteraction(methodName, uri, requestContent, null, HttpStatusCode.OK, activity);
            InternalFlowSpanStore.Complete(activity);
        }
        catch (Exception ex)
        {
            var responseContent = $"{ex.GetType().Name}: {ex.Message}";
            activity?.SetStatus(ActivityStatusCode.Error, responseContent);
            LogInteraction(methodName, uri, requestContent, responseContent, HttpStatusCode.InternalServerError, activity);
            InternalFlowSpanStore.Complete(activity);
            throw;
        }
    }

    private object? HandleAsyncResult(Task task, MethodInfo targetMethod, string methodName,
        Uri uri, string? requestContent, Activity? activity)
    {
        var returnType = targetMethod.ReturnType;

        if (returnType.IsGenericType && returnType.GetGenericTypeDefinition() == typeof(Task<>))
        {
            var resultType = returnType.GetGenericArguments()[0];
            var method = typeof(TrackingProxy<T>)
                .GetMethod(nameof(HandleAsyncResultTyped), BindingFlags.NonPublic | BindingFlags.Instance)!
                .MakeGenericMethod(resultType);
            return method.Invoke(this, [task, methodName, uri, requestContent, activity]);
        }

        return HandleAsyncResultVoid(task, methodName, uri, requestContent, activity);
    }

    private void LogInteraction(string methodName, Uri uri, string? requestContent,
        string? responseContent, HttpStatusCode statusCode, Activity? activity)
    {
        if (!PhaseConfiguration.ShouldTrack(_options.TrackDuringSetup, _options.TrackDuringAction))
            return;

        if (_options.LogMode == TrackingLogMode.Deferred)
        {
            PendingRequestResponseLogs.Enqueue(new PendingLogEntry(
                _options.ServiceName, _options.CallerName,
                methodName, requestContent, responseContent, uri, statusCode,
                activity?.TraceId.ToString(), activity?.SpanId.ToString(),
                _options.DependencyCategory));
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
            _options.DependencyCategory, source: testInfo.Value.Source);
    }
}
