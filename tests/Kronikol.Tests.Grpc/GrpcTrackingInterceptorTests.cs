using System.Net;
using Grpc.Core;
using Grpc.Core.Interceptors;
using Microsoft.AspNetCore.Http;
using Kronikol.Extensions.Grpc;
using Kronikol.InternalFlow;
using Kronikol.Tracking;

namespace Kronikol.Tests.Grpc;

public class GrpcTrackingInterceptorTests
{
    private readonly string _testId = Guid.NewGuid().ToString();

    private RequestResponseLog[] GetLogsFromThisTest()
    {
        return RequestResponseLogger.RequestAndResponseLogs
            .Where(l => l.TestId == _testId)
            .ToArray();
    }

    private GrpcTrackingOptions MakeOptions(
        GrpcTrackingVerbosity verbosity = GrpcTrackingVerbosity.Detailed,
        string serviceName = "GrpcService",
        string callerName = "TestCaller",
        bool useProtoServiceName = false) => new()
    {
        ServiceName = serviceName,
        CallerName = callerName,
        Verbosity = verbosity,
        CurrentTestInfoFetcher = () => ("My gRPC Test", _testId),
        UseProtoServiceNameInDiagram = useProtoServiceName
    };

    // Helpers for creating gRPC contexts
    private static readonly Marshaller<string> StringMarshaller = Marshallers.Create(
        msg => System.Text.Encoding.UTF8.GetBytes(msg),
        bytes => System.Text.Encoding.UTF8.GetString(bytes));

    private static Method<string, string> CreateMethod(string serviceName = "greet.Greeter",
        string methodName = "SayHello", MethodType type = MethodType.Unary)
    {
        return new Method<string, string>(
            type, serviceName, methodName, StringMarshaller, StringMarshaller);
    }

    private static ClientInterceptorContext<string, string> CreateContext(
        Method<string, string>? method = null, Metadata? headers = null)
    {
        method ??= CreateMethod();
        var options = new CallOptions(headers: headers);
        return new ClientInterceptorContext<string, string>(method, null, options);
    }

    // ─── AsyncUnaryCall logging ────────────────────────────

    [Fact]
    public async Task AsyncUnaryCall_Logs_request_and_response()
    {
        var interceptor = new GrpcTrackingInterceptor(MakeOptions());
        var context = CreateContext();

        var call = interceptor.AsyncUnaryCall(
            "Hello", context,
            (req, ctx) => new AsyncUnaryCall<string>(
                Task.FromResult("World"),
                Task.FromResult(new Metadata()),
                () => new Status(StatusCode.OK, ""),
                () => new Metadata(),
                () => { }));

        var response = await call.ResponseAsync;
        Assert.Equal("World", response);

        var logs = GetLogsFromThisTest();
        Assert.Equal(2, logs.Length);
        Assert.Equal(RequestResponseType.Request, logs[0].Type);
        Assert.Equal(RequestResponseType.Response, logs[1].Type);
    }

    [Fact]
    public async Task AsyncUnaryCall_Logs_correct_service_and_caller_names()
    {
        var interceptor = new GrpcTrackingInterceptor(
            MakeOptions(serviceName: "OrdersGrpc", callerName: "MyApi"));
        var context = CreateContext();

        var call = interceptor.AsyncUnaryCall(
            "Hello", context,
            (req, ctx) => new AsyncUnaryCall<string>(
                Task.FromResult("World"),
                Task.FromResult(new Metadata()),
                () => new Status(StatusCode.OK, ""),
                () => new Metadata(),
                () => { }));

        await call.ResponseAsync;

        var logs = GetLogsFromThisTest();
        Assert.Equal("OrdersGrpc", logs[0].ServiceName);
        Assert.Equal("MyApi", logs[0].CallerName);
    }

    [Fact]
    public async Task AsyncUnaryCall_No_test_info_does_not_log()
    {
        var options = MakeOptions();
        options.CurrentTestInfoFetcher = null;
        var interceptor = new GrpcTrackingInterceptor(options);
        var context = CreateContext();

        var call = interceptor.AsyncUnaryCall(
            "Hello", context,
            (req, ctx) => new AsyncUnaryCall<string>(
                Task.FromResult("World"),
                Task.FromResult(new Metadata()),
                () => new Status(StatusCode.OK, ""),
                () => new Metadata(),
                () => { }));

        await call.ResponseAsync;

        var logs = GetLogsFromThisTest();
        Assert.Empty(logs);
    }

    // ─── Verbosity ─────────────────────────────────────────

    [Fact]
    public async Task Detailed_Uses_method_name_as_label()
    {
        var interceptor = new GrpcTrackingInterceptor(MakeOptions(GrpcTrackingVerbosity.Detailed));
        var context = CreateContext();

        var call = interceptor.AsyncUnaryCall(
            "Hello", context,
            (req, ctx) => new AsyncUnaryCall<string>(
                Task.FromResult("World"),
                Task.FromResult(new Metadata()),
                () => new Status(StatusCode.OK, ""),
                () => new Metadata(),
                () => { }));

        await call.ResponseAsync;

        var log = GetLogsFromThisTest().First(l => l.Type == RequestResponseType.Request);
        Assert.Equal("SayHello", log.Method.Value?.ToString());
    }

    [Fact]
    public async Task Detailed_Uses_grpc_uri_scheme_with_method()
    {
        var interceptor = new GrpcTrackingInterceptor(MakeOptions(GrpcTrackingVerbosity.Detailed));
        var context = CreateContext();

        var call = interceptor.AsyncUnaryCall(
            "Hello", context,
            (req, ctx) => new AsyncUnaryCall<string>(
                Task.FromResult("World"),
                Task.FromResult(new Metadata()),
                () => new Status(StatusCode.OK, ""),
                () => new Metadata(),
                () => { }));

        await call.ResponseAsync;

        var log = GetLogsFromThisTest().First(l => l.Type == RequestResponseType.Request);
        Assert.Contains("grpc://", log.Uri.ToString());
        Assert.Contains("SayHello", log.Uri.ToString());
    }

    [Fact]
    public async Task Detailed_Includes_request_content()
    {
        var interceptor = new GrpcTrackingInterceptor(MakeOptions(GrpcTrackingVerbosity.Detailed));
        var context = CreateContext();

        var call = interceptor.AsyncUnaryCall(
            "Hello", context,
            (req, ctx) => new AsyncUnaryCall<string>(
                Task.FromResult("World"),
                Task.FromResult(new Metadata()),
                () => new Status(StatusCode.OK, ""),
                () => new Metadata(),
                () => { }));

        await call.ResponseAsync;

        var log = GetLogsFromThisTest().First(l => l.Type == RequestResponseType.Request);
        Assert.NotNull(log.Content);
        Assert.Contains("Hello", log.Content);
    }

    [Fact]
    public async Task Summarised_Omits_request_content()
    {
        var interceptor = new GrpcTrackingInterceptor(MakeOptions(GrpcTrackingVerbosity.Summarised));
        var context = CreateContext();

        var call = interceptor.AsyncUnaryCall(
            "Hello", context,
            (req, ctx) => new AsyncUnaryCall<string>(
                Task.FromResult("World"),
                Task.FromResult(new Metadata()),
                () => new Status(StatusCode.OK, ""),
                () => new Metadata(),
                () => { }));

        await call.ResponseAsync;

        var log = GetLogsFromThisTest().First(l => l.Type == RequestResponseType.Request);
        Assert.Null(log.Content);
    }

    [Fact]
    public async Task Summarised_Omits_response_content()
    {
        var interceptor = new GrpcTrackingInterceptor(MakeOptions(GrpcTrackingVerbosity.Summarised));
        var context = CreateContext();

        var call = interceptor.AsyncUnaryCall(
            "Hello", context,
            (req, ctx) => new AsyncUnaryCall<string>(
                Task.FromResult("World"),
                Task.FromResult(new Metadata()),
                () => new Status(StatusCode.OK, ""),
                () => new Metadata(),
                () => { }));

        await call.ResponseAsync;

        var log = GetLogsFromThisTest().First(l => l.Type == RequestResponseType.Response);
        Assert.Null(log.Content);
    }

    // ─── UseProtoServiceName ──────────────────────────────

    [Fact]
    public async Task UseProtoServiceName_Uses_proto_service_name()
    {
        var interceptor = new GrpcTrackingInterceptor(
            MakeOptions(serviceName: "MyGrpc", useProtoServiceName: true));
        var context = CreateContext(CreateMethod("greet.Greeter", "SayHello"));

        var call = interceptor.AsyncUnaryCall(
            "Hello", context,
            (req, ctx) => new AsyncUnaryCall<string>(
                Task.FromResult("World"),
                Task.FromResult(new Metadata()),
                () => new Status(StatusCode.OK, ""),
                () => new Metadata(),
                () => { }));

        await call.ResponseAsync;

        var log = GetLogsFromThisTest().First(l => l.Type == RequestResponseType.Request);
        Assert.Equal("greet.Greeter", log.ServiceName);
    }

    [Fact]
    public async Task UseProtoServiceName_False_Uses_configured_service_name()
    {
        var interceptor = new GrpcTrackingInterceptor(
            MakeOptions(serviceName: "MyCustomName", useProtoServiceName: false));
        var context = CreateContext();

        var call = interceptor.AsyncUnaryCall(
            "Hello", context,
            (req, ctx) => new AsyncUnaryCall<string>(
                Task.FromResult("World"),
                Task.FromResult(new Metadata()),
                () => new Status(StatusCode.OK, ""),
                () => new Metadata(),
                () => { }));

        await call.ResponseAsync;

        var log = GetLogsFromThisTest().First(l => l.Type == RequestResponseType.Request);
        Assert.Equal("MyCustomName", log.ServiceName);
    }

    // ─── Error handling ────────────────────────────────────

    [Fact]
    public async Task AsyncUnaryCall_RpcException_Logs_error_status()
    {
        var interceptor = new GrpcTrackingInterceptor(MakeOptions());
        var context = CreateContext();

        var call = interceptor.AsyncUnaryCall(
            "Hello", context,
            (req, ctx) => new AsyncUnaryCall<string>(
                Task.FromException<string>(new RpcException(new Status(StatusCode.NotFound, "not found"))),
                Task.FromResult(new Metadata()),
                () => new Status(StatusCode.NotFound, "not found"),
                () => new Metadata(),
                () => { }));

        await Assert.ThrowsAsync<RpcException>(() => call.ResponseAsync);

        var log = GetLogsFromThisTest().First(l => l.Type == RequestResponseType.Response);
        Assert.Equal(HttpStatusCode.NotFound, log.StatusCode?.Value);
    }

    // ─── BlockingUnaryCall ─────────────────────────────────

    [Fact]
    public void BlockingUnaryCall_Logs_request_and_response()
    {
        var interceptor = new GrpcTrackingInterceptor(MakeOptions());
        var context = CreateContext();

        var response = interceptor.BlockingUnaryCall(
            "Hello", context,
            (req, ctx) => "World");

        Assert.Equal("World", response);

        var logs = GetLogsFromThisTest();
        Assert.Equal(2, logs.Length);
        Assert.Equal(RequestResponseType.Request, logs[0].Type);
        Assert.Equal(RequestResponseType.Response, logs[1].Type);
    }

    [Fact]
    public void BlockingUnaryCall_RpcException_Logs_error()
    {
        var interceptor = new GrpcTrackingInterceptor(MakeOptions());
        var context = CreateContext();

        Assert.Throws<RpcException>(() =>
            interceptor.BlockingUnaryCall(
                "Hello", context,
                (req, ctx) => throw new RpcException(new Status(StatusCode.Unavailable, "down"))));

        var log = GetLogsFromThisTest().First(l => l.Type == RequestResponseType.Response);
        Assert.Equal(HttpStatusCode.ServiceUnavailable, log.StatusCode?.Value);
    }

    // ─── Streaming calls ──────────────────────────────────

    // Since R2 of plans/GRPC_IDENTITY_PROPAGATION_PLAN.md a stream's response is logged when the stream ends, not
    // when the call starts (StreamingOutcomeTests has the outcomes).

    [Fact]
    public async Task ServerStreamingCall_Logs_request_and_response()
    {
        var interceptor = new GrpcTrackingInterceptor(MakeOptions());
        var method = CreateMethod(type: MethodType.ServerStreaming);
        var context = CreateContext(method);

        using var call = interceptor.AsyncServerStreamingCall(
            "Hello", context,
            (req, ctx) => TestHelpers.CreateServerStreamingCall<string>());
        while (await call.ResponseStream.MoveNext(CancellationToken.None)) { }

        var logs = GetLogsFromThisTest();
        Assert.Equal(2, logs.Length);
    }

    [Fact]
    public void ClientStreamingCall_Logs_request_and_response()
    {
        var interceptor = new GrpcTrackingInterceptor(MakeOptions());
        var method = CreateMethod(type: MethodType.ClientStreaming);
        var context = CreateContext(method);

        interceptor.AsyncClientStreamingCall(
            context,
            ctx => TestHelpers.CreateClientStreamingCall<string, string>());

        var logs = GetLogsFromThisTest();
        Assert.Equal(2, logs.Length);
    }

    [Fact]
    public async Task DuplexStreamingCall_Logs_request_and_response()
    {
        var interceptor = new GrpcTrackingInterceptor(MakeOptions());
        var method = CreateMethod(type: MethodType.DuplexStreaming);
        var context = CreateContext(method);

        using var call = interceptor.AsyncDuplexStreamingCall(
            context,
            ctx => TestHelpers.CreateDuplexStreamingCall<string, string>());
        while (await call.ResponseStream.MoveNext(CancellationToken.None)) { }

        var logs = GetLogsFromThisTest();
        Assert.Equal(2, logs.Length);
    }

    // ─── Status code in response ──────────────────────────

    [Fact]
    public async Task Response_IncludesStatusCode()
    {
        var interceptor = new GrpcTrackingInterceptor(MakeOptions());
        var context = CreateContext();

        var call = interceptor.AsyncUnaryCall(
            "Hello", context,
            (req, ctx) => new AsyncUnaryCall<string>(
                Task.FromResult("World"),
                Task.FromResult(new Metadata()),
                () => new Status(StatusCode.OK, ""),
                () => new Metadata(),
                () => { }));

        await call.ResponseAsync;

        var log = GetLogsFromThisTest().First(l => l.Type == RequestResponseType.Response);
        Assert.Equal(HttpStatusCode.OK, log.StatusCode?.Value);
    }

    // ─── ITrackingComponent ────────────────────────────────

    [Fact]
    public void Implements_ITrackingComponent()
    {
        var interceptor = new GrpcTrackingInterceptor(MakeOptions());
        Assert.IsAssignableFrom<ITrackingComponent>(interceptor);
    }

    [Fact]
    public void WasInvoked_IsFalse_BeforeAnyCalls()
    {
        var interceptor = new GrpcTrackingInterceptor(MakeOptions());
        Assert.False(interceptor.WasInvoked);
    }

    [Fact]
    public async Task WasInvoked_IsTrue_AfterCall()
    {
        var interceptor = new GrpcTrackingInterceptor(MakeOptions());
        var context = CreateContext();

        var call = interceptor.AsyncUnaryCall(
            "Hello", context,
            (req, ctx) => new AsyncUnaryCall<string>(
                Task.FromResult("World"),
                Task.FromResult(new Metadata()),
                () => new Status(StatusCode.OK, ""),
                () => new Metadata(),
                () => { }));

        await call.ResponseAsync;
        Assert.True(interceptor.WasInvoked);
    }

    [Fact]
    public void InvocationCount_StartsAtZero()
    {
        var interceptor = new GrpcTrackingInterceptor(MakeOptions());
        Assert.Equal(0, interceptor.InvocationCount);
    }

    [Fact]
    public async Task InvocationCount_IncreasesWithEachCall()
    {
        var interceptor = new GrpcTrackingInterceptor(MakeOptions());
        var context = CreateContext();

        AsyncUnaryCall<string> MakeCall() => interceptor.AsyncUnaryCall(
            "Hello", context,
            (req, ctx) => new AsyncUnaryCall<string>(
                Task.FromResult("World"),
                Task.FromResult(new Metadata()),
                () => new Status(StatusCode.OK, ""),
                () => new Metadata(),
                () => { }));

        await MakeCall().ResponseAsync;
        await MakeCall().ResponseAsync;
        await MakeCall().ResponseAsync;

        Assert.Equal(3, interceptor.InvocationCount);
    }

    [Fact]
    public void ComponentName_MatchesServiceName()
    {
        var interceptor = new GrpcTrackingInterceptor(MakeOptions(serviceName: "MyGrpcService"));
        Assert.Equal("GrpcTrackingInterceptor (MyGrpcService)", interceptor.ComponentName);
    }

    [Fact]
    public void Constructor_AutoRegistersWithTrackingComponentRegistry()
    {
        TrackingComponentRegistry.Clear();
        var interceptor = new GrpcTrackingInterceptor(MakeOptions());

        var components = TrackingComponentRegistry.GetRegisteredComponents();
        Assert.Contains(components, c => ReferenceEquals(c, interceptor));
    }

    // ─── Activity / Span Tracking ──────────────────────────

    [Fact]
    public async Task AsyncUnaryCall_sets_ActivityTraceId_on_request_log()
    {
        var interceptor = new GrpcTrackingInterceptor(MakeOptions());
        var context = CreateContext();

        var call = interceptor.AsyncUnaryCall(
            "Hello", context,
            (req, ctx) => new AsyncUnaryCall<string>(
                Task.FromResult("World"),
                Task.FromResult(new Metadata()),
                () => new Status(StatusCode.OK, ""),
                () => new Metadata(),
                () => { }));

        await call.ResponseAsync;

        var requestLog = GetLogsFromThisTest().First(l => l.Type == RequestResponseType.Request);
        Assert.NotNull(requestLog.ActivityTraceId);
        Assert.Matches(@"^[0-9a-f]{32}$", requestLog.ActivityTraceId);
    }

    [Fact]
    public async Task AsyncUnaryCall_sets_ActivitySpanId_on_request_log()
    {
        var interceptor = new GrpcTrackingInterceptor(MakeOptions());
        var context = CreateContext();

        var call = interceptor.AsyncUnaryCall(
            "Hello", context,
            (req, ctx) => new AsyncUnaryCall<string>(
                Task.FromResult("World"),
                Task.FromResult(new Metadata()),
                () => new Status(StatusCode.OK, ""),
                () => new Metadata(),
                () => { }));

        await call.ResponseAsync;

        var requestLog = GetLogsFromThisTest().First(l => l.Type == RequestResponseType.Request);
        Assert.NotNull(requestLog.ActivitySpanId);
        Assert.Matches(@"^[0-9a-f]{16}$", requestLog.ActivitySpanId);
    }

    [Fact]
    public async Task AsyncUnaryCall_sets_Timestamp_on_logs()
    {
        var before = DateTimeOffset.UtcNow;
        var interceptor = new GrpcTrackingInterceptor(MakeOptions());
        var context = CreateContext();

        var call = interceptor.AsyncUnaryCall(
            "Hello", context,
            (req, ctx) => new AsyncUnaryCall<string>(
                Task.FromResult("World"),
                Task.FromResult(new Metadata()),
                () => new Status(StatusCode.OK, ""),
                () => new Metadata(),
                () => { }));

        await call.ResponseAsync;
        var after = DateTimeOffset.UtcNow;

        var logs = GetLogsFromThisTest();
        Assert.All(logs, l =>
        {
            Assert.NotNull(l.Timestamp);
            Assert.InRange(l.Timestamp.Value, before, after);
        });
    }

    [Fact]
    public void BlockingUnaryCall_sets_ActivityTraceId_on_request_log()
    {
        var interceptor = new GrpcTrackingInterceptor(MakeOptions());
        var context = CreateContext();

        interceptor.BlockingUnaryCall(
            "Hello", context,
            (req, ctx) => "World");

        var requestLog = GetLogsFromThisTest().First(l => l.Type == RequestResponseType.Request);
        Assert.NotNull(requestLog.ActivityTraceId);
        Assert.Matches(@"^[0-9a-f]{32}$", requestLog.ActivityTraceId);
    }

    // ─── Activity Lifecycle & Store ────────────────────────

    [Fact]
    public async Task AsyncUnaryCall_activity_is_captured_in_SpanStore_with_nonzero_duration()
    {
        var interceptor = new GrpcTrackingInterceptor(MakeOptions());
        var context = CreateContext();

        var tcs = new TaskCompletionSource<string>();
        var call = interceptor.AsyncUnaryCall(
            "Hello", context,
            (req, ctx) => new AsyncUnaryCall<string>(
                tcs.Task,
                Task.FromResult(new Metadata()),
                () => new Status(StatusCode.OK, ""),
                () => new Metadata(),
                () => { }));

        // Let some time elapse before completing the response
        await Task.Delay(50, TestContext.Current.CancellationToken);
        tcs.SetResult("World");
        await call.ResponseAsync;

        var requestLog = GetLogsFromThisTest().First(l => l.Type == RequestResponseType.Request);
        var spans = InternalFlowSpanStore.GetSpans()
            .Where(s => s.Source.Name == "Kronikol.Grpc"
                     && s.TraceId.ToString() == requestLog.ActivityTraceId)
            .ToArray();

        Assert.Single(spans);
        Assert.True(spans[0].Duration > TimeSpan.Zero,
            $"Expected span duration > 0 but was {spans[0].Duration}");
    }

    [Fact]
    public async Task AsyncUnaryCall_activity_spans_from_request_to_response()
    {
        var interceptor = new GrpcTrackingInterceptor(MakeOptions());
        var context = CreateContext();

        var tcs = new TaskCompletionSource<string>();
        var call = interceptor.AsyncUnaryCall(
            "Hello", context,
            (req, ctx) => new AsyncUnaryCall<string>(
                tcs.Task,
                Task.FromResult(new Metadata()),
                () => new Status(StatusCode.OK, ""),
                () => new Metadata(),
                () => { }));

        await Task.Delay(30, TestContext.Current.CancellationToken);
        tcs.SetResult("World");
        await call.ResponseAsync;

        var requestLog = GetLogsFromThisTest().First(l => l.Type == RequestResponseType.Request);
        var responseLog = GetLogsFromThisTest().First(l => l.Type == RequestResponseType.Response);
        var span = InternalFlowSpanStore.GetSpans()
            .First(s => s.TraceId.ToString() == requestLog.ActivityTraceId);

        // Span should start before or at request time and end after or at response time
        var spanStart = new DateTimeOffset(span.StartTimeUtc, TimeSpan.Zero);
        var spanEnd = spanStart + span.Duration;

        Assert.True(spanStart <= requestLog.Timestamp!.Value.AddMilliseconds(5),
            $"Span start {spanStart} should be at or before request time {requestLog.Timestamp}");
        Assert.True(spanEnd >= responseLog.Timestamp!.Value.AddMilliseconds(-5),
            $"Span end {spanEnd} should be at or after response time {responseLog.Timestamp}");
    }

    [Fact]
    public async Task AsyncUnaryCall_gRPC_spans_pass_AutoInstrumentation_filter()
    {
        var interceptor = new GrpcTrackingInterceptor(MakeOptions());
        var context = CreateContext();

        var call = interceptor.AsyncUnaryCall(
            "Hello", context,
            (req, ctx) => new AsyncUnaryCall<string>(
                Task.FromResult("World"),
                Task.FromResult(new Metadata()),
                () => new Status(StatusCode.OK, ""),
                () => new Metadata(),
                () => { }));

        await call.ResponseAsync;

        var collected = InternalFlowSpanCollector.CollectSpans(
            InternalFlowSpanGranularity.AutoInstrumentation);

        var requestLog = GetLogsFromThisTest().First(l => l.Type == RequestResponseType.Request);
        Assert.Contains(collected, s =>
            s.Source.Name == "Kronikol.Grpc"
            && s.TraceId.ToString() == requestLog.ActivityTraceId);
    }

    // ─── Traceparent Propagation ───────────────────────────

    [Fact]
    public async Task AsyncUnaryCall_injects_traceparent_metadata_header()
    {
        var interceptor = new GrpcTrackingInterceptor(MakeOptions());
        var context = CreateContext();
        Metadata? capturedHeaders = null;

        var call = interceptor.AsyncUnaryCall(
            "Hello", context,
            (req, ctx) =>
            {
                capturedHeaders = ctx.Options.Headers;
                return new AsyncUnaryCall<string>(
                    Task.FromResult("World"),
                    Task.FromResult(new Metadata()),
                    () => new Status(StatusCode.OK, ""),
                    () => new Metadata(),
                    () => { });
            });

        await call.ResponseAsync;

        Assert.NotNull(capturedHeaders);
        var traceparent = capturedHeaders!.FirstOrDefault(h => h.Key == "traceparent");
        Assert.NotNull(traceparent);
        Assert.Matches(@"^00-[0-9a-f]{32}-[0-9a-f]{16}-00$", traceparent.Value);
    }

    [Fact]
    public void BlockingUnaryCall_injects_traceparent_metadata_header()
    {
        var interceptor = new GrpcTrackingInterceptor(MakeOptions());
        var context = CreateContext();
        Metadata? capturedHeaders = null;

        interceptor.BlockingUnaryCall(
            "Hello", context,
            (req, ctx) =>
            {
                capturedHeaders = ctx.Options.Headers;
                return "World";
            });

        Assert.NotNull(capturedHeaders);
        var traceparent = capturedHeaders!.FirstOrDefault(h => h.Key == "traceparent");
        Assert.NotNull(traceparent);
        Assert.Matches(@"^00-[0-9a-f]{32}-[0-9a-f]{16}-00$", traceparent.Value);
    }

    [Fact]
    public async Task AsyncUnaryCall_traceparent_matches_log_ActivityTraceId()
    {
        var interceptor = new GrpcTrackingInterceptor(MakeOptions());
        var context = CreateContext();
        Metadata? capturedHeaders = null;

        var call = interceptor.AsyncUnaryCall(
            "Hello", context,
            (req, ctx) =>
            {
                capturedHeaders = ctx.Options.Headers;
                return new AsyncUnaryCall<string>(
                    Task.FromResult("World"),
                    Task.FromResult(new Metadata()),
                    () => new Status(StatusCode.OK, ""),
                    () => new Metadata(),
                    () => { });
            });

        await call.ResponseAsync;

        var requestLog = GetLogsFromThisTest().First(l => l.Type == RequestResponseType.Request);
        var traceparent = capturedHeaders!.First(h => h.Key == "traceparent").Value;

        // traceparent format: 00-{traceId}-{spanId}-00
        var parts = traceparent.Split('-');
        Assert.Equal(requestLog.ActivityTraceId, parts[1]);
        Assert.Equal(requestLog.ActivitySpanId, parts[2]);
    }

    // ─── HttpContextAccessor via Options ───────────────────

    [Fact]
    public async Task AsyncUnaryCall_resolves_test_info_from_HttpContextAccessor_on_options()
    {
        var httpContext = new DefaultHttpContext();
        httpContext.Request.Headers["test-tracking-current-test-name"] = "HTTP Context Test";
        httpContext.Request.Headers["test-tracking-current-test-id"] = _testId;

        var options = new GrpcTrackingOptions
        {
            ServiceName = "GrpcService",
            CallerName = "TestCaller",
            HttpContextAccessor = new TestHttpContextAccessor(httpContext),
            CurrentTestInfoFetcher = null // no delegate — must resolve from HTTP context
        };

        var interceptor = new GrpcTrackingInterceptor(options);
        var context = CreateContext();

        var call = interceptor.AsyncUnaryCall(
            "Hello", context,
            (req, ctx) => new AsyncUnaryCall<string>(
                Task.FromResult("World"),
                Task.FromResult(new Metadata()),
                () => new Status(StatusCode.OK, ""),
                () => new Metadata(),
                () => { }));

        await call.ResponseAsync;

        var logs = GetLogsFromThisTest();
        Assert.Equal(2, logs.Length);
        Assert.Equal("HTTP Context Test", logs[0].TestName);
    }

    [Fact]
    public async Task AsyncUnaryCall_HttpContextAccessor_on_options_takes_priority_over_delegate()
    {
        var httpContext = new DefaultHttpContext();
        httpContext.Request.Headers["test-tracking-current-test-name"] = "From HTTP Context";
        httpContext.Request.Headers["test-tracking-current-test-id"] = _testId;

        var options = new GrpcTrackingOptions
        {
            ServiceName = "GrpcService",
            CallerName = "TestCaller",
            HttpContextAccessor = new TestHttpContextAccessor(httpContext),
            CurrentTestInfoFetcher = () => ("From Delegate", _testId)
        };

        var interceptor = new GrpcTrackingInterceptor(options);
        var context = CreateContext();

        var call = interceptor.AsyncUnaryCall(
            "Hello", context,
            (req, ctx) => new AsyncUnaryCall<string>(
                Task.FromResult("World"),
                Task.FromResult(new Metadata()),
                () => new Status(StatusCode.OK, ""),
                () => new Metadata(),
                () => { }));

        await call.ResponseAsync;

        var log = GetLogsFromThisTest().First(l => l.Type == RequestResponseType.Request);
        Assert.Equal("From HTTP Context", log.TestName);
    }

    [Fact]
    public async Task AsyncUnaryCall_falls_back_to_delegate_when_HttpContextAccessor_has_no_headers()
    {
        var httpContext = new DefaultHttpContext(); // no test-tracking headers

        var options = new GrpcTrackingOptions
        {
            ServiceName = "GrpcService",
            CallerName = "TestCaller",
            HttpContextAccessor = new TestHttpContextAccessor(httpContext),
            CurrentTestInfoFetcher = () => ("Delegate Fallback", _testId)
        };

        var interceptor = new GrpcTrackingInterceptor(options);
        var context = CreateContext();

        var call = interceptor.AsyncUnaryCall(
            "Hello", context,
            (req, ctx) => new AsyncUnaryCall<string>(
                Task.FromResult("World"),
                Task.FromResult(new Metadata()),
                () => new Status(StatusCode.OK, ""),
                () => new Metadata(),
                () => { }));

        await call.ResponseAsync;

        var log = GetLogsFromThisTest().First(l => l.Type == RequestResponseType.Request);
        Assert.Equal("Delegate Fallback", log.TestName);
    }

    [Fact]
    public void BlockingUnaryCall_resolves_test_info_from_HttpContextAccessor_on_options()
    {
        var httpContext = new DefaultHttpContext();
        httpContext.Request.Headers["test-tracking-current-test-name"] = "Blocking HTTP Context";
        httpContext.Request.Headers["test-tracking-current-test-id"] = _testId;

        var options = new GrpcTrackingOptions
        {
            ServiceName = "GrpcService",
            CallerName = "TestCaller",
            HttpContextAccessor = new TestHttpContextAccessor(httpContext),
            CurrentTestInfoFetcher = null
        };

        var interceptor = new GrpcTrackingInterceptor(options);
        var context = CreateContext();

        interceptor.BlockingUnaryCall("Hello", context, (req, ctx) => "World");

        var logs = GetLogsFromThisTest();
        Assert.Equal(2, logs.Length);
        Assert.Equal("Blocking HTTP Context", logs[0].TestName);
    }

    [Fact]
    public async Task ServerStreamingCall_resolves_test_info_from_HttpContextAccessor_on_options()
    {
        var httpContext = new DefaultHttpContext();
        httpContext.Request.Headers["test-tracking-current-test-name"] = "Streaming HTTP Context";
        httpContext.Request.Headers["test-tracking-current-test-id"] = _testId;

        var options = new GrpcTrackingOptions
        {
            ServiceName = "GrpcService",
            CallerName = "TestCaller",
            HttpContextAccessor = new TestHttpContextAccessor(httpContext),
            CurrentTestInfoFetcher = null
        };

        var interceptor = new GrpcTrackingInterceptor(options);
        var method = CreateMethod(type: MethodType.ServerStreaming);
        var context = CreateContext(method);

        using var call = interceptor.AsyncServerStreamingCall(
            "Hello", context,
            (req, ctx) => TestHelpers.CreateServerStreamingCall<string>());
        while (await call.ResponseStream.MoveNext(CancellationToken.None)) { }

        var logs = GetLogsFromThisTest();
        Assert.Equal(2, logs.Length);
        Assert.Equal("Streaming HTTP Context", logs[0].TestName);
    }

    private class TestHttpContextAccessor : IHttpContextAccessor
    {
        public TestHttpContextAccessor(HttpContext? httpContext) => HttpContext = httpContext;
        public HttpContext? HttpContext { get; set; }
    }
}

// Helper class for creating streaming call stubs
internal static class TestHelpers
{
    public static AsyncServerStreamingCall<TResponse> CreateServerStreamingCall<TResponse>()
    {
        return new AsyncServerStreamingCall<TResponse>(
            new StubAsyncStreamReader<TResponse>(),
            Task.FromResult(new Metadata()),
            () => new Status(StatusCode.OK, ""),
            () => new Metadata(),
            () => { });
    }

    public static AsyncClientStreamingCall<TRequest, TResponse> CreateClientStreamingCall<TRequest, TResponse>()
        where TRequest : class
    {
        return new AsyncClientStreamingCall<TRequest, TResponse>(
            new StubClientStreamWriter<TRequest>(),
            Task.FromResult(default(TResponse)!),
            Task.FromResult(new Metadata()),
            () => new Status(StatusCode.OK, ""),
            () => new Metadata(),
            () => { });
    }

    public static AsyncDuplexStreamingCall<TRequest, TResponse> CreateDuplexStreamingCall<TRequest, TResponse>()
        where TRequest : class
    {
        return new AsyncDuplexStreamingCall<TRequest, TResponse>(
            new StubClientStreamWriter<TRequest>(),
            new StubAsyncStreamReader<TResponse>(),
            Task.FromResult(new Metadata()),
            () => new Status(StatusCode.OK, ""),
            () => new Metadata(),
            () => { });
    }

    private class StubAsyncStreamReader<T> : IAsyncStreamReader<T>
    {
        public T Current => default!;
        public Task<bool> MoveNext(CancellationToken cancellationToken) => Task.FromResult(false);
    }

    private class StubClientStreamWriter<T> : IClientStreamWriter<T> where T : class
    {
        public WriteOptions? WriteOptions { get; set; }
        public Task WriteAsync(T message) => Task.CompletedTask;
        public Task CompleteAsync() => Task.CompletedTask;
    }
}
