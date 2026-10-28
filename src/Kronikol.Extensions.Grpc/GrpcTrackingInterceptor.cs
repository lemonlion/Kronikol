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
/// A gRPC <see cref="Interceptor"/> that logs all gRPC calls for inclusion in test diagrams, and puts the test's identity
/// into each call's metadata (<see cref="GrpcTrackingOptions.PropagateTestIdentity"/>), so a host that calls
/// <c>AddTestTrackingContextPropagation()</c> attributes its own calls to the scenario.
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

    /// <summary>
    /// <c>true</c> when the interceptor holds an <see cref="IHttpContextAccessor"/>: the one its constructor was given,
    /// else <see cref="GrpcTrackingOptions.HttpContextAccessor"/>. Through it, a call a host makes while it serves a
    /// request takes the identity, and the Kronikol trace id, of that request.
    /// </summary>
    public bool HasHttpContextAccessor => _httpContextAccessor is not null;

    public override AsyncUnaryCall<TResponse> AsyncUnaryCall<TRequest, TResponse>(
        TRequest request,
        ClientInterceptorContext<TRequest, TResponse> context,
        AsyncUnaryCallContinuation<TRequest, TResponse> continuation)
    {
        Interlocked.Increment(ref _invocationCount);

        if (Plan(context) is not { } plan)
            return continuation(request, context);
        if (!plan.Tracked)
            return continuation(request, plan.Propagates ? WithCallMetadata(context, plan) : context);
        var effectiveVerbosity = PhaseConfiguration.GetEffectiveVerbosity(_options.Verbosity, _options.SetupVerbosity, _options.ActionVerbosity);
        // A message is written when any verbosity the call may be drawn at shows it, and each note is made at its own:
        // a phase's variant was given the effective verbosity's, so a Detailed phase of a Summarised interceptor had
        // neither message.
        var drawnAt = PhaseConfiguration.DrawnAt(_options.Verbosity, _options.SetupVerbosity, _options.ActionVerbosity);

        var testInfo = plan.Identity;

        var opInfo = Classify(context);
        var label = GrpcOperationClassifier.GetDiagramLabel(opInfo, effectiveVerbosity);
        var serviceName = ResolveServiceName(opInfo);
        var uri = BuildUri(opInfo, effectiveVerbosity);
        var requestMessage = SerializeMessage(request, drawnAt);
        var headers = plan.LoggedHeaders;

        var traceId = plan.TraceId;
        var requestResponseId = Guid.NewGuid();

        EnsureListenerStarted();
        // The span lasts until the response arrives (WrapUnaryResponse completes it), but this method returns as
        // soon as the call starts: the caller's own Activity.Current is given back then, or the span would stay
        // current in the caller's flow and parent every call it makes next.
        var callerActivity = Activity.Current;
        var activity = GrpcActivitySource.StartActivity(opInfo.FullMethodName ?? "gRPC");
        AsyncUnaryCall<TResponse> call;
        Task<TResponse> wrappedResponseAsync;
        string? activityTraceId = null, activitySpanId = null;
        var requestLogged = false;
        try
        {
            (activityTraceId, activitySpanId, var recorded) = CaptureActivityContext();

            context = WithCallMetadata(context, plan, activityTraceId, activitySpanId, recorded);

            LogRequest(testInfo, label, MessageNote(requestMessage), effectiveVerbosity, uri, headers, serviceName, traceId, requestResponseId, activityTraceId, activitySpanId, opInfo);
            requestLogged = true;

            // Started under the span, so the transport's own spans are its children.
            call = continuation(request, context);

            wrappedResponseAsync = WrapUnaryResponse(
                call.ResponseAsync, testInfo, label, uri, serviceName, traceId, requestResponseId, effectiveVerbosity, drawnAt,
                activityTraceId, activitySpanId, activity, opInfo);
        }
        catch (Exception ex)
        {
            // A continuation that throws as the call starts (a disposed channel does) is the call's answer.
            if (requestLogged)
            {
                var (status, content) = Failure(ex);
                LogResponse(testInfo, label, _ => content, effectiveVerbosity, uri, serviceName, traceId, requestResponseId, status,
                    activityTraceId, activitySpanId, opInfo);
            }
            InternalFlowSpanStore.Complete(activity);
            throw;
        }
        finally
        {
            Activity.Current = callerActivity;
        }

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

        if (Plan(context) is not { } plan)
            return continuation(request, context);
        if (!plan.Tracked)
            return continuation(request, plan.Propagates ? WithCallMetadata(context, plan) : context);
        var effectiveVerbosity = PhaseConfiguration.GetEffectiveVerbosity(_options.Verbosity, _options.SetupVerbosity, _options.ActionVerbosity);
        var drawnAt = PhaseConfiguration.DrawnAt(_options.Verbosity, _options.SetupVerbosity, _options.ActionVerbosity);

        var testInfo = plan.Identity;

        var opInfo = Classify(context);
        var label = GrpcOperationClassifier.GetDiagramLabel(opInfo, effectiveVerbosity);
        var serviceName = ResolveServiceName(opInfo);
        var uri = BuildUri(opInfo, effectiveVerbosity);
        var requestMessage = SerializeMessage(request, drawnAt);
        var headers = plan.LoggedHeaders;

        var traceId = plan.TraceId;
        var requestResponseId = Guid.NewGuid();

        EnsureListenerStarted();
        using var activity = GrpcActivitySource.StartActivity(opInfo.FullMethodName ?? "gRPC");
        var (activityTraceId, activitySpanId, recorded) = CaptureActivityContext();

        context = WithCallMetadata(context, plan, activityTraceId, activitySpanId, recorded);

        LogRequest(testInfo, label, MessageNote(requestMessage), effectiveVerbosity, uri, headers, serviceName, traceId, requestResponseId, activityTraceId, activitySpanId, opInfo);

        try
        {
            var response = continuation(request, context);
            var responseMessage = SerializeMessage(response, drawnAt);
            LogResponse(testInfo, label, MessageNote(responseMessage), effectiveVerbosity, uri, serviceName, traceId, requestResponseId, HttpStatusCode.OK, activityTraceId, activitySpanId, opInfo);
            return response;
        }
        catch (Exception ex)
        {
            var (status, content) = Failure(ex);
            LogResponse(testInfo, label, _ => content, effectiveVerbosity, uri, serviceName,
                traceId, requestResponseId, status, activityTraceId, activitySpanId, opInfo);
            throw;
        }
    }

    public override AsyncServerStreamingCall<TResponse> AsyncServerStreamingCall<TRequest, TResponse>(
        TRequest request,
        ClientInterceptorContext<TRequest, TResponse> context,
        AsyncServerStreamingCallContinuation<TRequest, TResponse> continuation)
    {
        Interlocked.Increment(ref _invocationCount);

        if (Plan(context) is not { } plan)
            return continuation(request, context);
        if (!plan.Tracked)
            return continuation(request, plan.Propagates ? WithCallMetadata(context, plan) : context);
        var effectiveVerbosity = PhaseConfiguration.GetEffectiveVerbosity(_options.Verbosity, _options.SetupVerbosity, _options.ActionVerbosity);
        var drawnAt = PhaseConfiguration.DrawnAt(_options.Verbosity, _options.SetupVerbosity, _options.ActionVerbosity);

        var testInfo = plan.Identity;

        var opInfo = Classify(context);
        var label = GrpcOperationClassifier.GetDiagramLabel(opInfo, effectiveVerbosity);
        var serviceName = ResolveServiceName(opInfo);
        var uri = BuildUri(opInfo, effectiveVerbosity);
        var requestMessage = SerializeMessage(request, drawnAt);
        var headers = plan.LoggedHeaders;

        var traceId = plan.TraceId;
        var requestResponseId = Guid.NewGuid();

        EnsureListenerStarted();
        // The span lasts until the stream ends (StreamOutcome completes it); the caller gets its own Activity.Current
        // back when the call starts, as an async unary call's caller does.
        var callerActivity = Activity.Current;
        var activity = GrpcActivitySource.StartActivity(opInfo.FullMethodName ?? "gRPC");
        StreamOutcome? started = null;
        try
        {
            var (activityTraceId, activitySpanId, recorded) = CaptureActivityContext();

            context = WithCallMetadata(context, plan, activityTraceId, activitySpanId, recorded);

            LogRequest(testInfo, label, MessageNote(requestMessage), effectiveVerbosity, uri, headers, serviceName, traceId, requestResponseId, activityTraceId, activitySpanId, opInfo);

            var outcome = new StreamOutcome(this, testInfo, label, uri, serviceName, traceId, requestResponseId,
                activityTraceId, activitySpanId, opInfo, activity, TestPhaseContext.Current, effectiveVerbosity);
            started = outcome;

            var call = continuation(request, context);

            return new AsyncServerStreamingCall<TResponse>(
                new OutcomeReader<TResponse>(call.ResponseStream, outcome),
                call.ResponseHeadersAsync,
                call.GetStatus,
                call.GetTrailers,
                () => outcome.Dispose(call.GetStatus, call.Dispose));
        }
        catch (Exception ex)
        {
            // A continuation that throws as the call starts (a disposed channel does) is the call's answer.
            if (started is not null)
                started.Failed(ex);
            else
                InternalFlowSpanStore.Complete(activity);
            throw;
        }
        finally
        {
            Activity.Current = callerActivity;
        }
    }

    public override AsyncClientStreamingCall<TRequest, TResponse> AsyncClientStreamingCall<TRequest, TResponse>(
        ClientInterceptorContext<TRequest, TResponse> context,
        AsyncClientStreamingCallContinuation<TRequest, TResponse> continuation)
    {
        Interlocked.Increment(ref _invocationCount);

        if (Plan(context) is not { } plan)
            return continuation(context);
        if (!plan.Tracked)
            return continuation(plan.Propagates ? WithCallMetadata(context, plan) : context);
        var effectiveVerbosity = PhaseConfiguration.GetEffectiveVerbosity(_options.Verbosity, _options.SetupVerbosity, _options.ActionVerbosity);
        var drawnAt = PhaseConfiguration.DrawnAt(_options.Verbosity, _options.SetupVerbosity, _options.ActionVerbosity);

        var testInfo = plan.Identity;

        var opInfo = Classify(context);
        var label = GrpcOperationClassifier.GetDiagramLabel(opInfo, effectiveVerbosity);
        var serviceName = ResolveServiceName(opInfo);
        var uri = BuildUri(opInfo, effectiveVerbosity);
        var headers = plan.LoggedHeaders;

        var traceId = plan.TraceId;
        var requestResponseId = Guid.NewGuid();

        EnsureListenerStarted();
        // The span lasts until the stream ends (StreamOutcome completes it); the caller gets its own Activity.Current
        // back when the call starts, as an async unary call's caller does.
        var callerActivity = Activity.Current;
        var activity = GrpcActivitySource.StartActivity(opInfo.FullMethodName ?? "gRPC");
        StreamOutcome? started = null;
        try
        {
            var (activityTraceId, activitySpanId, recorded) = CaptureActivityContext();

            context = WithCallMetadata(context, plan, activityTraceId, activitySpanId, recorded);

            LogRequest(testInfo, label, _ => null, effectiveVerbosity, uri, headers, serviceName, traceId, requestResponseId, activityTraceId, activitySpanId, opInfo);

            var outcome = new StreamOutcome(this, testInfo, label, uri, serviceName, traceId, requestResponseId,
                activityTraceId, activitySpanId, opInfo, activity, TestPhaseContext.Current, effectiveVerbosity);
            started = outcome;

            var call = continuation(context);

            return new AsyncClientStreamingCall<TRequest, TResponse>(
                call.RequestStream,
                WrapStreamResponse(call.ResponseAsync, outcome, drawnAt),
                call.ResponseHeadersAsync,
                call.GetStatus,
                call.GetTrailers,
                () => outcome.Dispose(call.GetStatus, call.Dispose));
        }
        catch (Exception ex)
        {
            // A continuation that throws as the call starts (a disposed channel does) is the call's answer.
            if (started is not null)
                started.Failed(ex);
            else
                InternalFlowSpanStore.Complete(activity);
            throw;
        }
        finally
        {
            Activity.Current = callerActivity;
        }
    }

    public override AsyncDuplexStreamingCall<TRequest, TResponse> AsyncDuplexStreamingCall<TRequest, TResponse>(
        ClientInterceptorContext<TRequest, TResponse> context,
        AsyncDuplexStreamingCallContinuation<TRequest, TResponse> continuation)
    {
        Interlocked.Increment(ref _invocationCount);

        if (Plan(context) is not { } plan)
            return continuation(context);
        if (!plan.Tracked)
            return continuation(plan.Propagates ? WithCallMetadata(context, plan) : context);
        var effectiveVerbosity = PhaseConfiguration.GetEffectiveVerbosity(_options.Verbosity, _options.SetupVerbosity, _options.ActionVerbosity);

        var testInfo = plan.Identity;

        var opInfo = Classify(context);
        var label = GrpcOperationClassifier.GetDiagramLabel(opInfo, effectiveVerbosity);
        var serviceName = ResolveServiceName(opInfo);
        var uri = BuildUri(opInfo, effectiveVerbosity);
        var headers = plan.LoggedHeaders;

        var traceId = plan.TraceId;
        var requestResponseId = Guid.NewGuid();

        EnsureListenerStarted();
        // The span lasts until the stream ends (StreamOutcome completes it); the caller gets its own Activity.Current
        // back when the call starts, as an async unary call's caller does.
        var callerActivity = Activity.Current;
        var activity = GrpcActivitySource.StartActivity(opInfo.FullMethodName ?? "gRPC");
        StreamOutcome? started = null;
        try
        {
            var (activityTraceId, activitySpanId, recorded) = CaptureActivityContext();

            context = WithCallMetadata(context, plan, activityTraceId, activitySpanId, recorded);

            LogRequest(testInfo, label, _ => null, effectiveVerbosity, uri, headers, serviceName, traceId, requestResponseId, activityTraceId, activitySpanId, opInfo);

            var outcome = new StreamOutcome(this, testInfo, label, uri, serviceName, traceId, requestResponseId,
                activityTraceId, activitySpanId, opInfo, activity, TestPhaseContext.Current, effectiveVerbosity);
            started = outcome;

            var call = continuation(context);

            return new AsyncDuplexStreamingCall<TRequest, TResponse>(
                call.RequestStream,
                new OutcomeReader<TResponse>(call.ResponseStream, outcome),
                call.ResponseHeadersAsync,
                call.GetStatus,
                call.GetTrailers,
                () => outcome.Dispose(call.GetStatus, call.Dispose));
        }
        catch (Exception ex)
        {
            // A continuation that throws as the call starts (a disposed channel does) is the call's answer.
            if (started is not null)
                started.Failed(ex);
            else
                InternalFlowSpanStore.Complete(activity);
            throw;
        }
        finally
        {
            Activity.Current = callerActivity;
        }
    }

    private async Task<TResponse> WrapUnaryResponse<TResponse>(
        Task<TResponse> responseTask,
        TestIdentity testInfo,
        string label, Uri uri, string serviceName,
        Guid traceId, Guid requestResponseId, GrpcTrackingVerbosity effectiveVerbosity, GrpcTrackingVerbosity[] drawnAt,
        string? activityTraceId, string? activitySpanId,
        Activity? activity, GrpcOperationInfo? opInfo = null)
    {
        try
        {
            var response = await responseTask;
            var responseMessage = SerializeMessage(response, drawnAt);
            LogResponse(testInfo, label, MessageNote(responseMessage), effectiveVerbosity, uri, serviceName, traceId, requestResponseId, HttpStatusCode.OK, activityTraceId, activitySpanId, opInfo);
            return response;
        }
        catch (Exception ex)
        {
            var (status, content) = Failure(ex);
            LogResponse(testInfo, label, _ => content, effectiveVerbosity, uri, serviceName,
                traceId, requestResponseId, status, activityTraceId, activitySpanId, opInfo);
            throw;
        }
        finally
        {
            InternalFlowSpanStore.Complete(activity);
        }
    }

    /// <summary>
    /// How a call that failed is recorded: an <see cref="RpcException"/> as its status, an
    /// <see cref="OperationCanceledException"/> as <c>Cancelled</c> (a channel with
    /// <c>ThrowOperationCanceledOnCancellation</c> throws one where it would throw <c>RpcException(Cancelled)</c>), and
    /// anything else as <c>Unknown</c>, each with <c>"{code}: {message}"</c>, its note at every verbosity. Every failure is
    /// recorded, so no request is left without its response.
    /// </summary>
    private static (HttpStatusCode Status, string Content) Failure(Exception exception)
    {
        var (code, message) = exception switch
        {
            RpcException rpc => (rpc.StatusCode, rpc.Message),
            OperationCanceledException => (StatusCode.Cancelled, exception.Message),
            _ => (StatusCode.Unknown, exception.Message),
        };
        return (MapGrpcStatusToHttp(code), $"{code}: {message}");
    }

    /// <summary>A client stream's response, recorded as an async unary call's is, through its outcome.</summary>
    private async Task<TResponse> WrapStreamResponse<TResponse>(
        Task<TResponse> responseTask, StreamOutcome outcome, GrpcTrackingVerbosity[] drawnAt)
    {
        TResponse response;
        try
        {
            response = await responseTask.ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            outcome.Failed(ex);
            throw;
        }

        outcome.Completed(SerializeMessage(response, drawnAt));
        return response;
    }

    /// <summary>
    /// The outcome of a streaming call, recorded once, when the call ends, with the span ending with it. The response
    /// stream read to its end, or a client stream's response, is <c>OK</c>; a failure is its mapped status with
    /// <c>"{code}: {message}"</c>, as for a unary call. A call disposed first is <c>Cancelled</c>, unless its status
    /// was already known; a call neither read to its end nor disposed has no response recorded.
    /// </summary>
    private sealed class StreamOutcome(
        GrpcTrackingInterceptor owner, TestIdentity testInfo, string label, Uri uri, string serviceName,
        Guid traceId, Guid requestResponseId, string? activityTraceId, string? activitySpanId,
        GrpcOperationInfo opInfo, Activity? activity, TestPhase phase, GrpcTrackingVerbosity verbosity)
    {
        private int _recorded;

        public void Completed(string? message = null) => Record(MessageNote(message), HttpStatusCode.OK);

        public void Failed(Exception exception)
        {
            var (status, content) = Failure(exception);
            Record(_ => content, status);
        }

        /// <summary>
        /// The call is being disposed: its status when it already has one, else <c>Cancelled</c>. The call is disposed,
        /// and the outcome recorded, whatever reading its status or its own dispose throws.
        /// </summary>
        public void Dispose(Func<Status> getStatus, Action dispose)
        {
            Status? status = null;
            try
            {
                status = getStatus();
            }
            catch
            {
                // The call has not finished (InvalidOperationException), or it cannot say: disposing it cancels it.
            }

            try
            {
                dispose();
            }
            finally
            {
                if (status is not { } known)
                    Record(_ => $"{StatusCode.Cancelled}: the call was disposed before its stream ended", MapGrpcStatusToHttp(StatusCode.Cancelled));
                else if (known.StatusCode == StatusCode.OK)
                    Completed();
                else
                    Failed(new RpcException(known));
            }
        }

        private void Record(Func<GrpcTrackingVerbosity, string?> note, HttpStatusCode statusCode)
        {
            if (Interlocked.Exchange(ref _recorded, 1) != 0)
                return;

            owner.LogResponse(testInfo, label, note, verbosity, uri, serviceName, traceId, requestResponseId, statusCode,
                activityTraceId, activitySpanId, opInfo, phase);
            InternalFlowSpanStore.Complete(activity);
        }
    }

    /// <summary>A response stream that records its call's outcome when it ends or fails.</summary>
    private sealed class OutcomeReader<T>(IAsyncStreamReader<T> inner, StreamOutcome outcome) : IAsyncStreamReader<T>
    {
        public T Current => inner.Current;

        public async Task<bool> MoveNext(CancellationToken cancellationToken)
        {
            bool more;
            try
            {
                more = await inner.MoveNext(cancellationToken).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                outcome.Failed(ex);
                throw;
            }

            if (!more)
                outcome.Completed();
            return more;
        }
    }

    /// <summary>
    /// The request half of a call, with the note <paramref name="note"/> gives it at <paramref name="verbosity"/>, the
    /// call's, and at each phase's verbosity on the phase's variant.
    /// </summary>
    private void LogRequest(
        TestIdentity testInfo, string label, Func<GrpcTrackingVerbosity, string?> note, GrpcTrackingVerbosity verbosity,
        Uri uri, (string Key, string? Value)[] headers, string serviceName,
        Guid traceId, Guid requestResponseId,
        string? activityTraceId, string? activitySpanId,
        GrpcOperationInfo? opInfo = null)
    {
        RequestResponseLogger.Log(new RequestResponseLog(
            testInfo.Name, testInfo.Id,
            label, note(verbosity), uri,
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
                note(v),
                headers, false)));
    }

    /// <summary>The response half of a call, with its note made as <see cref="LogRequest"/> makes the request's.</summary>
    private void LogResponse(
        TestIdentity testInfo, string label, Func<GrpcTrackingVerbosity, string?> note, GrpcTrackingVerbosity verbosity,
        Uri uri, string serviceName,
        Guid traceId, Guid requestResponseId, HttpStatusCode statusCode,
        string? activityTraceId, string? activitySpanId,
        GrpcOperationInfo? opInfo = null, TestPhase? phase = null)
    {
        RequestResponseLogger.Log(new RequestResponseLog(
            testInfo.Name, testInfo.Id,
            label, note(verbosity), uri,
            [], serviceName, _options.CallerName,
            RequestResponseType.Response, traceId, requestResponseId, false,
            statusCode,
            DependencyCategory: DependencyCategories.Grpc)
        {
            Phase = phase ?? TestPhaseContext.Current,
            AttributionSource = testInfo.Source,
            Timestamp = DateTimeOffset.UtcNow,
            ActivityTraceId = activityTraceId,
            ActivitySpanId = activitySpanId
        }.WithVariants(_options.Verbosity, _options.SetupVerbosity, _options.ActionVerbosity,
            v => new PhaseVariant(
                GrpcOperationClassifier.GetDiagramLabel(opInfo!, v),
                BuildUri(opInfo!, v),
                note(v),
                [], false)));
    }

    /// <summary>A message's note: its JSON at Detailed and Raw, none at Summarised.</summary>
    private static Func<GrpcTrackingVerbosity, string?> MessageNote(string? message) =>
        verbosity => verbosity == GrpcTrackingVerbosity.Summarised ? null : message;

    private void EnsureListenerStarted()
    {
        if (_listenerStarted) return;
        InternalFlowActivityListener.EnsureStarted();
        _listenerStarted = true;
    }

    private static (string? TraceId, string? SpanId, bool Recorded) CaptureActivityContext()
    {
        if (Activity.Current is { } current)
            return (current.TraceId.ToString(), current.SpanId.ToString(), current.Recorded);

        return (ActivityTraceId.CreateRandom().ToString(), ActivitySpanId.CreateRandom().ToString(), false);
    }

    /// <summary>What one call carries and logs.</summary>
    /// <param name="Identity">The identity the call resolved.</param>
    /// <param name="TraceId">
    /// The call's Kronikol trace id: the one the caller put in the call's metadata, which is the one sent; else the one
    /// the request being served carries, else a new one. The called host's HTTP handler and gRPC interceptor, given its
    /// accessor, reuse it for their own calls, so <c>kronikol query flow</c> nests those under this call even when the
    /// two sides name the service differently; a tracker that makes a trace id of its own (a database's) nests only by
    /// name.
    /// </param>
    /// <param name="LoggedHeaders">The caller's metadata, read before the interceptor adds any, so diagrams show no new lines.</param>
    /// <param name="Tracked">Whether the call is logged in the current phase.</param>
    /// <param name="Propagates">Whether the call carries the identity to the host it calls.</param>
    private readonly record struct CallPlan(
        TestIdentity Identity, Guid TraceId, (string Key, string? Value)[] LoggedHeaders, bool Tracked, bool Propagates);

    /// <summary>
    /// Resolves the call's identity, or null when nothing resolves (the call is then passed on untouched). It is resolved
    /// before the phase is checked: a call not drawn in the current phase still carries its scenario to the next host,
    /// as the HTTP handler's calls do. Only an identity that names a scenario travels; the background identity is not
    /// sent, so the called host resolves its own, with the true source.
    /// </summary>
    private CallPlan? Plan<TRequest, TResponse>(ClientInterceptorContext<TRequest, TResponse> context)
        where TRequest : class
        where TResponse : class
    {
        var identity = TestInfoResolver.ResolveWithSource(_httpContextAccessor, _options.CurrentTestInfoFetcher);
        if (identity is null)
            return null;

        return new CallPlan(
            identity.Value,
            CallerTraceId(context) ?? InboundTraceId() ?? Guid.NewGuid(),
            GetCallHeaders(context),
            PhaseConfiguration.ShouldTrack(_options.TrackDuringSetup, _options.TrackDuringAction),
            _options.PropagateTestIdentity && identity.Value.IsAttributed);
    }

    /// <summary>
    /// The Kronikol trace id the caller put in the call's metadata itself, when it is a GUID: the interceptor leaves that
    /// header as it is, so the called host reads that id, and the call is logged with it too.
    /// </summary>
    private static Guid? CallerTraceId<TRequest, TResponse>(ClientInterceptorContext<TRequest, TResponse> context)
        where TRequest : class
        where TResponse : class
    {
        if (context.Options.Headers is not { } headers)
            return null;

        foreach (var entry in headers)
        {
            if (!entry.IsBinary && string.Equals(entry.Key, TestTrackingHttpHeaders.TraceIdHeader, StringComparison.OrdinalIgnoreCase))
                return Guid.TryParse(entry.Value, out var traceId) ? traceId : null;
        }

        return null;
    }

    /// <summary>The Kronikol trace id of the request the host is serving, when it carries one that is a GUID.</summary>
    private Guid? InboundTraceId()
    {
        try
        {
            var headers = _httpContextAccessor?.HttpContext?.Request.Headers;
            if (headers is not null &&
                headers.TryGetValue(TestTrackingHttpHeaders.TraceIdHeader, out var values) &&
                Guid.TryParse(values.FirstOrDefault(), out var traceId))
                return traceId;
        }
        catch
        {
            // HttpContext access can fail in edge cases, as TestInfoResolver notes: the call gets a trace id of its own.
        }

        return null;
    }

    /// <summary>
    /// The call's options with metadata of its own. The caller's <see cref="Metadata"/> is never written to: it may be
    /// reused for other calls (each would add one more <c>traceparent</c>, and the server would receive them joined
    /// into one value it cannot parse), or be the frozen <see cref="Metadata.Empty"/>. Its entries are copied, then:
    /// <list type="bullet">
    /// <item>when the call <see cref="CallPlan.Propagates"/>, the four identity headers the HTTP handler sends, each
    /// only when the caller's metadata lacks it, with the name, id and caller name in the form
    /// <see cref="TrackingHeaderValue"/> writes;</item>
    /// <item>a <c>traceparent</c> for the span, unless the caller set one. Its flags say sampled (<c>01</c>) when the
    /// span is recorded, so a parent-based sampler in the called host keeps the server span.</item>
    /// </list>
    /// </summary>
    private ClientInterceptorContext<TRequest, TResponse> WithCallMetadata<TRequest, TResponse>(
        ClientInterceptorContext<TRequest, TResponse> context, CallPlan plan,
        string? activityTraceId = null, string? activitySpanId = null, bool recorded = false)
        where TRequest : class
        where TResponse : class
    {
        var headers = new Metadata();
        if (context.Options.Headers is { } callerHeaders)
        {
            foreach (var entry in callerHeaders)
                headers.Add(entry);
        }

        if (plan.Propagates)
        {
            AddIfAbsent(headers, TestTrackingHttpHeaders.CurrentTestNameHeader, TrackingHeaderValue.Encode(plan.Identity.Name));
            AddIfAbsent(headers, TestTrackingHttpHeaders.CurrentTestIdHeader, TrackingHeaderValue.Encode(plan.Identity.Id));
            AddIfAbsent(headers, TestTrackingHttpHeaders.TraceIdHeader, plan.TraceId.ToString());
            AddIfAbsent(headers, TestTrackingHttpHeaders.CallerNameHeader, TrackingHeaderValue.Encode(_options.CallerName));
        }

        if (activityTraceId is not null && activitySpanId is not null)
            AddIfAbsent(headers, TraceParentKey, $"00-{activityTraceId}-{activitySpanId}-{(recorded ? "01" : "00")}");

        var newOptions = context.Options.WithHeaders(headers);
        return new ClientInterceptorContext<TRequest, TResponse>(context.Method, context.Host, newOptions);
    }

    private static void AddIfAbsent(Metadata headers, string key, string? value)
    {
        if (value is not null && !HasEntry(headers, key))
            headers.Add(key, value);
    }

    private const string TraceParentKey = "traceparent";

    private static bool HasEntry(Metadata headers, string key)
    {
        foreach (var entry in headers)
        {
            if (string.Equals(entry.Key, key, StringComparison.OrdinalIgnoreCase))
                return true;
        }

        return false;
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

    /// <summary>
    /// The message as the call's record shows it, which never fails the call: JSON through
    /// <see cref="JsonFormatter.ToDiagnosticString"/>, the same text <c>JsonFormatter.Default</c> writes except for a
    /// <c>google.protobuf.Any</c> whose type no registry holds, written as its type URL and bytes where
    /// <c>JsonFormatter.Default</c> throws; and a short note in place of a message that cannot be written at all. It is
    /// written only when a verbosity in <paramref name="drawnAt"/> shows it, as Detailed and Raw do and Summarised does not.
    /// </summary>
    private string? SerializeMessage<T>(T message, GrpcTrackingVerbosity[] drawnAt)
    {
        if (drawnAt.All(v => v == GrpcTrackingVerbosity.Summarised)) return null;

        try
        {
            if (message is IMessage protoMsg)
                return JsonFormatter.ToDiagnosticString(protoMsg);

            return message?.ToString();
        }
        catch (Exception ex)
        {
            return $"({message?.GetType().Name} could not be written: {ex.Message})";
        }
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
