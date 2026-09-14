using Kronikol.Constants;
using System.Diagnostics;
using System.Net;
using Google.Protobuf;
using Grpc.Core;
using Grpc.Core.Interceptors;
using Microsoft.AspNetCore.Http;
using Kronikol.InternalFlow;
using Kronikol.Tracking;

namespace Kronikol.Extensions.Grpc;

/// <summary>
/// A gRPC <see cref="Interceptor"/> that logs all gRPC calls for inclusion in test diagrams.
/// </summary>
public class GrpcTrackingInterceptor : Interceptor, ITrackingComponent
{
    private static readonly ActivitySource GrpcActivitySource = new("Kronikol.Grpc");
    private readonly GrpcTrackingOptions _options;
    private readonly IHttpContextAccessor? _httpContextAccessor;
    private int _invocationCount;
    private bool _listenerStarted;

    public GrpcTrackingInterceptor(GrpcTrackingOptions options, IHttpContextAccessor? httpContextAccessor = null)
    {
        _options = options;
        _httpContextAccessor = httpContextAccessor ?? options.HttpContextAccessor;
        TrackingComponentRegistry.Register(this);
    }

    public string ComponentName => $"GrpcTrackingInterceptor ({_options.ServiceName})";
    public bool WasInvoked => _invocationCount > 0;
    public int InvocationCount => _invocationCount;

    public override AsyncUnaryCall<TResponse> AsyncUnaryCall<TRequest, TResponse>(
        TRequest request,
        ClientInterceptorContext<TRequest, TResponse> context,
        AsyncUnaryCallContinuation<TRequest, TResponse> continuation)
    {
        Interlocked.Increment(ref _invocationCount);

        if (!PhaseConfiguration.ShouldTrack(_options.TrackDuringSetup, _options.TrackDuringAction))
            return continuation(request, context);
        var effectiveVerbosity = PhaseConfiguration.GetEffectiveVerbosity(_options.Verbosity, _options.SetupVerbosity, _options.ActionVerbosity);

        var testInfo = TestInfoResolver.ResolveWithSource(_httpContextAccessor, _options.CurrentTestInfoFetcher);
        if (testInfo is null)
            return continuation(request, context);

        var opInfo = Classify(context);
        var label = GrpcOperationClassifier.GetDiagramLabel(opInfo, effectiveVerbosity);
        var serviceName = ResolveServiceName(opInfo);
        var uri = BuildUri(opInfo, effectiveVerbosity);
        var requestContent = SerializeMessage(request, effectiveVerbosity);
        var headers = GetCallHeaders(context);

        var traceId = Guid.NewGuid();
        var requestResponseId = Guid.NewGuid();

        EnsureListenerStarted();
        var activity = GrpcActivitySource.StartActivity(opInfo.FullMethodName ?? "gRPC");
        var (activityTraceId, activitySpanId) = CaptureActivityContext();

        context = InjectTraceParent(context, activityTraceId, activitySpanId);

        LogRequest(testInfo.Value, label, requestContent, uri, headers, serviceName, traceId, requestResponseId, activityTraceId, activitySpanId, opInfo);

        var call = continuation(request, context);

        var wrappedResponseAsync = WrapUnaryResponse(
            call.ResponseAsync, testInfo.Value, label, uri, serviceName, traceId, requestResponseId, effectiveVerbosity,
            activityTraceId, activitySpanId, activity, opInfo);

        return new AsyncUnaryCall<TResponse>(
            wrappedResponseAsync,
            call.ResponseHeadersAsync,
            call.GetStatus,
            call.GetTrailers,
            call.Dispose);
    }

    public override TResponse BlockingUnaryCall<TRequest, TResponse>(
        TRequest request,
        ClientInterceptorContext<TRequest, TResponse> context,
        BlockingUnaryCallContinuation<TRequest, TResponse> continuation)
    {
        Interlocked.Increment(ref _invocationCount);

        if (!PhaseConfiguration.ShouldTrack(_options.TrackDuringSetup, _options.TrackDuringAction))
            return continuation(request, context);
        var effectiveVerbosity = PhaseConfiguration.GetEffectiveVerbosity(_options.Verbosity, _options.SetupVerbosity, _options.ActionVerbosity);

        var testInfo = TestInfoResolver.ResolveWithSource(_httpContextAccessor, _options.CurrentTestInfoFetcher);
        if (testInfo is null)
            return continuation(request, context);

        var opInfo = Classify(context);
        var label = GrpcOperationClassifier.GetDiagramLabel(opInfo, effectiveVerbosity);
        var serviceName = ResolveServiceName(opInfo);
        var uri = BuildUri(opInfo, effectiveVerbosity);
        var requestContent = SerializeMessage(request, effectiveVerbosity);
        var headers = GetCallHeaders(context);

        var traceId = Guid.NewGuid();
        var requestResponseId = Guid.NewGuid();

        EnsureListenerStarted();
        using var activity = GrpcActivitySource.StartActivity(opInfo.FullMethodName ?? "gRPC");
        var (activityTraceId, activitySpanId) = CaptureActivityContext();

        context = InjectTraceParent(context, activityTraceId, activitySpanId);

        LogRequest(testInfo.Value, label, requestContent, uri, headers, serviceName, traceId, requestResponseId, activityTraceId, activitySpanId, opInfo);

        try
        {
            var response = continuation(request, context);
            var responseContent = SerializeMessage(response, effectiveVerbosity);
            LogResponse(testInfo.Value, label, responseContent, uri, serviceName, traceId, requestResponseId, HttpStatusCode.OK, activityTraceId, activitySpanId, opInfo);
            return response;
        }
        catch (RpcException ex)
        {
            LogResponse(testInfo.Value, label, $"{ex.StatusCode}: {ex.Message}", uri, serviceName,
                traceId, requestResponseId, MapGrpcStatusToHttp(ex.StatusCode), activityTraceId, activitySpanId, opInfo);
            throw;
        }
    }

    public override AsyncServerStreamingCall<TResponse> AsyncServerStreamingCall<TRequest, TResponse>(
        TRequest request,
        ClientInterceptorContext<TRequest, TResponse> context,
        AsyncServerStreamingCallContinuation<TRequest, TResponse> continuation)
    {
        Interlocked.Increment(ref _invocationCount);

        if (!PhaseConfiguration.ShouldTrack(_options.TrackDuringSetup, _options.TrackDuringAction))
            return continuation(request, context);
        var effectiveVerbosity = PhaseConfiguration.GetEffectiveVerbosity(_options.Verbosity, _options.SetupVerbosity, _options.ActionVerbosity);

        var testInfo = TestInfoResolver.ResolveWithSource(_httpContextAccessor, _options.CurrentTestInfoFetcher);
        if (testInfo is null)
            return continuation(request, context);

        var opInfo = Classify(context);
        var label = GrpcOperationClassifier.GetDiagramLabel(opInfo, effectiveVerbosity);
        var serviceName = ResolveServiceName(opInfo);
        var uri = BuildUri(opInfo, effectiveVerbosity);
        var requestContent = SerializeMessage(request, effectiveVerbosity);
        var headers = GetCallHeaders(context);

        var traceId = Guid.NewGuid();
        var requestResponseId = Guid.NewGuid();

        EnsureListenerStarted();
        using var activity = GrpcActivitySource.StartActivity(opInfo.FullMethodName ?? "gRPC");
        var (activityTraceId, activitySpanId) = CaptureActivityContext();

        context = InjectTraceParent(context, activityTraceId, activitySpanId);

        LogRequest(testInfo.Value, label, requestContent, uri, headers, serviceName, traceId, requestResponseId, activityTraceId, activitySpanId, opInfo);

        var call = continuation(request, context);

        LogResponse(testInfo.Value, label, null, uri, serviceName, traceId, requestResponseId, HttpStatusCode.OK, activityTraceId, activitySpanId, opInfo);

        return call;
    }

    public override AsyncClientStreamingCall<TRequest, TResponse> AsyncClientStreamingCall<TRequest, TResponse>(
        ClientInterceptorContext<TRequest, TResponse> context,
        AsyncClientStreamingCallContinuation<TRequest, TResponse> continuation)
    {
        Interlocked.Increment(ref _invocationCount);

        if (!PhaseConfiguration.ShouldTrack(_options.TrackDuringSetup, _options.TrackDuringAction))
            return continuation(context);
        var effectiveVerbosity = PhaseConfiguration.GetEffectiveVerbosity(_options.Verbosity, _options.SetupVerbosity, _options.ActionVerbosity);

        var testInfo = TestInfoResolver.ResolveWithSource(_httpContextAccessor, _options.CurrentTestInfoFetcher);
        if (testInfo is null)
            return continuation(context);

        var opInfo = Classify(context);
        var label = GrpcOperationClassifier.GetDiagramLabel(opInfo, effectiveVerbosity);
        var serviceName = ResolveServiceName(opInfo);
        var uri = BuildUri(opInfo, effectiveVerbosity);
        var headers = GetCallHeaders(context);

        var traceId = Guid.NewGuid();
        var requestResponseId = Guid.NewGuid();

        EnsureListenerStarted();
        using var activity = GrpcActivitySource.StartActivity(opInfo.FullMethodName ?? "gRPC");
        var (activityTraceId, activitySpanId) = CaptureActivityContext();

        context = InjectTraceParent(context, activityTraceId, activitySpanId);

        LogRequest(testInfo.Value, label, null, uri, headers, serviceName, traceId, requestResponseId, activityTraceId, activitySpanId, opInfo);

        var call = continuation(context);

        LogResponse(testInfo.Value, label, null, uri, serviceName, traceId, requestResponseId, HttpStatusCode.OK, activityTraceId, activitySpanId, opInfo);

        return call;
    }

    public override AsyncDuplexStreamingCall<TRequest, TResponse> AsyncDuplexStreamingCall<TRequest, TResponse>(
        ClientInterceptorContext<TRequest, TResponse> context,
        AsyncDuplexStreamingCallContinuation<TRequest, TResponse> continuation)
    {
        Interlocked.Increment(ref _invocationCount);

        if (!PhaseConfiguration.ShouldTrack(_options.TrackDuringSetup, _options.TrackDuringAction))
            return continuation(context);
        var effectiveVerbosity = PhaseConfiguration.GetEffectiveVerbosity(_options.Verbosity, _options.SetupVerbosity, _options.ActionVerbosity);

        var testInfo = TestInfoResolver.ResolveWithSource(_httpContextAccessor, _options.CurrentTestInfoFetcher);
        if (testInfo is null)
            return continuation(context);

        var opInfo = Classify(context);
        var label = GrpcOperationClassifier.GetDiagramLabel(opInfo, effectiveVerbosity);
        var serviceName = ResolveServiceName(opInfo);
        var uri = BuildUri(opInfo, effectiveVerbosity);
        var headers = GetCallHeaders(context);

        var traceId = Guid.NewGuid();
        var requestResponseId = Guid.NewGuid();

        EnsureListenerStarted();
        using var activity = GrpcActivitySource.StartActivity(opInfo.FullMethodName ?? "gRPC");
        var (activityTraceId, activitySpanId) = CaptureActivityContext();

        context = InjectTraceParent(context, activityTraceId, activitySpanId);

        LogRequest(testInfo.Value, label, null, uri, headers, serviceName, traceId, requestResponseId, activityTraceId, activitySpanId, opInfo);

        var call = continuation(context);

        LogResponse(testInfo.Value, label, null, uri, serviceName, traceId, requestResponseId, HttpStatusCode.OK, activityTraceId, activitySpanId, opInfo);

        return call;
    }

    private async Task<TResponse> WrapUnaryResponse<TResponse>(
        Task<TResponse> responseTask,
        TestIdentity testInfo,
        string label, Uri uri, string serviceName,
        Guid traceId, Guid requestResponseId, GrpcTrackingVerbosity effectiveVerbosity,
        string? activityTraceId, string? activitySpanId,
        Activity? activity, GrpcOperationInfo? opInfo = null)
    {
        try
        {
            var response = await responseTask;
            var responseContent = SerializeMessage(response, effectiveVerbosity);
            LogResponse(testInfo, label, responseContent, uri, serviceName, traceId, requestResponseId, HttpStatusCode.OK, activityTraceId, activitySpanId, opInfo);
            return response;
        }
        catch (RpcException ex)
        {
            LogResponse(testInfo, label, $"{ex.StatusCode}: {ex.Message}", uri, serviceName,
                traceId, requestResponseId, MapGrpcStatusToHttp(ex.StatusCode), activityTraceId, activitySpanId, opInfo);
            throw;
        }
        finally
        {
            InternalFlowSpanStore.Complete(activity);
        }
    }

    private void LogRequest(
        TestIdentity testInfo, string label, string? content,
        Uri uri, (string Key, string? Value)[] headers, string serviceName,
        Guid traceId, Guid requestResponseId,
        string? activityTraceId, string? activitySpanId,
        GrpcOperationInfo? opInfo = null)
    {
        RequestResponseLogger.Log(new RequestResponseLog(
            testInfo.Name, testInfo.Id,
            label, content, uri,
            headers, serviceName, _options.CallerName,
            RequestResponseType.Request, traceId, requestResponseId, false,
            DependencyCategory: DependencyCategories.Grpc)
        {
            Phase = TestPhaseContext.Current,
            AttributionSource = testInfo.Source,
            Timestamp = DateTimeOffset.UtcNow,
            ActivityTraceId = activityTraceId,
            ActivitySpanId = activitySpanId
        }.WithVariants(_options.Verbosity, _options.SetupVerbosity, _options.ActionVerbosity,
            v => new PhaseVariant(
                GrpcOperationClassifier.GetDiagramLabel(opInfo!, v),
                BuildUri(opInfo!, v),
                v == GrpcTrackingVerbosity.Summarised ? null : content,
                headers, false)));
    }

    private void LogResponse(
        TestIdentity testInfo, string label, string? content,
        Uri uri, string serviceName,
        Guid traceId, Guid requestResponseId, HttpStatusCode statusCode,
        string? activityTraceId, string? activitySpanId,
        GrpcOperationInfo? opInfo = null)
    {
        RequestResponseLogger.Log(new RequestResponseLog(
            testInfo.Name, testInfo.Id,
            label, content, uri,
            [], serviceName, _options.CallerName,
            RequestResponseType.Response, traceId, requestResponseId, false,
            statusCode,
            DependencyCategory: DependencyCategories.Grpc)
        {
            Phase = TestPhaseContext.Current,
            AttributionSource = testInfo.Source,
            Timestamp = DateTimeOffset.UtcNow,
            ActivityTraceId = activityTraceId,
            ActivitySpanId = activitySpanId
        }.WithVariants(_options.Verbosity, _options.SetupVerbosity, _options.ActionVerbosity,
            v => new PhaseVariant(
                GrpcOperationClassifier.GetDiagramLabel(opInfo!, v),
                BuildUri(opInfo!, v),
                v == GrpcTrackingVerbosity.Summarised ? null : content,
                [], false)));
    }

    private void EnsureListenerStarted()
    {
        if (_listenerStarted) return;
        InternalFlowActivityListener.EnsureStarted();
        _listenerStarted = true;
    }

    private static (string? TraceId, string? SpanId) CaptureActivityContext()
    {
        if (Activity.Current != null)
            return (Activity.Current.TraceId.ToString(), Activity.Current.SpanId.ToString());

        return (ActivityTraceId.CreateRandom().ToString(), ActivitySpanId.CreateRandom().ToString());
    }

    private static ClientInterceptorContext<TRequest, TResponse> InjectTraceParent<TRequest, TResponse>(
        ClientInterceptorContext<TRequest, TResponse> context,
        string? activityTraceId, string? activitySpanId)
        where TRequest : class
        where TResponse : class
    {
        if (activityTraceId is null || activitySpanId is null)
            return context;

        var headers = context.Options.Headers ?? new Metadata();
        headers.Add("traceparent", $"00-{activityTraceId}-{activitySpanId}-00");

        var newOptions = context.Options.WithHeaders(headers);
        return new ClientInterceptorContext<TRequest, TResponse>(context.Method, context.Host, newOptions);
    }

    private static GrpcOperationInfo Classify<TRequest, TResponse>(
        ClientInterceptorContext<TRequest, TResponse> context)
        where TRequest : class
        where TResponse : class
    {
        return GrpcOperationClassifier.Classify(
            context.Method.Type,
            context.Method.FullName,
            context.Method.ServiceName,
            context.Method.Name);
    }

    private string ResolveServiceName(GrpcOperationInfo opInfo)
    {
        return _options.UseProtoServiceNameInDiagram
            ? opInfo.ServiceName ?? _options.ServiceName
            : _options.ServiceName;
    }

    private Uri BuildUri(GrpcOperationInfo opInfo, GrpcTrackingVerbosity verbosity)
    {
        return verbosity switch
        {
            GrpcTrackingVerbosity.Summarised =>
                new Uri($"grpc:///{opInfo.ServiceName ?? "service"}/"),
            GrpcTrackingVerbosity.Detailed =>
                new Uri($"grpc:///{opInfo.ServiceName ?? "service"}/{opInfo.MethodName ?? "method"}"),
            _ => // Raw
                new Uri($"grpc:///{opInfo.FullMethodName ?? (opInfo.ServiceName + "/" + opInfo.MethodName)}")
        };
    }

    private string? SerializeMessage<T>(T message, GrpcTrackingVerbosity verbosity)
    {
        if (verbosity == GrpcTrackingVerbosity.Summarised) return null;

        if (message is IMessage protoMsg)
            return JsonFormatter.Default.Format(protoMsg);

        return message?.ToString();
    }

    public static HttpStatusCode MapGrpcStatusToHttp(StatusCode grpcStatus)
    {
        return grpcStatus switch
        {
            StatusCode.OK => HttpStatusCode.OK,
            StatusCode.NotFound => HttpStatusCode.NotFound,
            StatusCode.PermissionDenied => HttpStatusCode.Forbidden,
            StatusCode.Unauthenticated => HttpStatusCode.Unauthorized,
            StatusCode.InvalidArgument => HttpStatusCode.BadRequest,
            StatusCode.DeadlineExceeded => HttpStatusCode.RequestTimeout,
            StatusCode.AlreadyExists => HttpStatusCode.Conflict,
            StatusCode.ResourceExhausted => (HttpStatusCode)429,
            StatusCode.Unavailable => HttpStatusCode.ServiceUnavailable,
            StatusCode.Unimplemented => HttpStatusCode.NotImplemented,
            StatusCode.Cancelled => HttpStatusCode.RequestTimeout,
            _ => HttpStatusCode.InternalServerError
        };
    }

    private static (string Key, string? Value)[] GetCallHeaders<TRequest, TResponse>(
        ClientInterceptorContext<TRequest, TResponse> context)
        where TRequest : class
        where TResponse : class
    {
        if (context.Options.Headers is null) return [];

        return context.Options.Headers
            .Where(h => !h.IsBinary)
            .Select(h => (h.Key, (string?)h.Value))
            .ToArray();
    }
}
