using System.Diagnostics;
using System.Net;
using Grpc.Core;
using Kronikol.Extensions.Grpc;
using Kronikol.Tracking;

namespace Kronikol.Tests.Grpc;

/// <summary>
/// A call that ends other than by its stream's last message or its unary response. A streaming call's span ends with the
/// call (plans/GRPC_IDENTITY_PROPAGATION_PLAN.md section 4.5), so disposing the call stops the span in the flow that
/// disposes it, and <see cref="Activity.Stop"/> sets <c>Activity.Current</c> to the span's predecessor whatever is
/// current there. A call that fails with something other than an <see cref="RpcException"/> is still an answer, as a
/// stream's failure is: an unpaired request reads as a call that never ended.
/// </summary>
public class CallEndingTests
{
    private readonly string _testId = Guid.NewGuid().ToString();

    private GrpcTrackingOptions Options() => new()
    {
        ServiceName = "Orders",
        CallerName = "Test",
        CurrentTestInfoFetcher = () => ("Call ending", _testId),
    };

    private RequestResponseLog[] Responses() => RequestResponseLogger.RequestAndResponseLogs
        .Where(l => l.TestId == _testId && l.Type == RequestResponseType.Response).ToArray();

    public static readonly TheoryData<CallKind> StreamingKinds = new(CallKind.ServerStreaming, CallKind.ClientStreaming, CallKind.Duplex);

    private static ActivityListener Listen(ActivitySource source)
    {
        var listener = new ActivityListener
        {
            ShouldListenTo = s => s == source || s.Name == "Kronikol.Grpc",
            Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllData,
        };
        ActivitySource.AddActivityListener(listener);
        return listener;
    }

    /// <summary>A streaming call of <paramref name="kind"/> whose stream has not ended and whose status is not known.</summary>
    private static IDisposable StartUnfinished(
        GrpcTrackingInterceptor interceptor, CallKind kind, Func<Status>? getStatus = null, Action? dispose = null)
    {
        getStatus ??= () => throw new InvalidOperationException("not finished");
        dispose ??= () => { };
        var context = CallKinds.Context(kind);
        return kind switch
        {
            CallKind.ServerStreaming => interceptor.AsyncServerStreamingCall("request", context, (_, _) =>
                new AsyncServerStreamingCall<string>(new CallKinds.ListReader(["one", "two"]), Task.FromResult(new Metadata()),
                    getStatus, () => new Metadata(), dispose)),
            CallKind.ClientStreaming => interceptor.AsyncClientStreamingCall(context, _ =>
                new AsyncClientStreamingCall<string, string>(new CallKinds.NullWriter(), new TaskCompletionSource<string>().Task,
                    Task.FromResult(new Metadata()), getStatus, () => new Metadata(), dispose)),
            _ => interceptor.AsyncDuplexStreamingCall(context, _ =>
                new AsyncDuplexStreamingCall<string, string>(new CallKinds.NullWriter(), new CallKinds.ListReader(["one", "two"]),
                    Task.FromResult(new Metadata()), getStatus, () => new Metadata(), dispose)),
        };
    }

    // ─── Disposing a stream and the caller's current span ───────

    [Theory]
    [MemberData(nameof(StreamingKinds))]
    public void Disposing_a_stream_while_another_span_is_current_leaves_that_span_current(CallKind kind)
    {
        using var source = new ActivitySource("Kronikol.Tests.CallEnding." + Guid.NewGuid().ToString("N"));
        using var listener = Listen(source);
        var interceptor = new GrpcTrackingInterceptor(Options());

        using var outer = source.StartActivity("outer");
        Assert.NotNull(outer);
        var call = StartUnfinished(interceptor, kind);
        Assert.Same(outer, Activity.Current);

        using (var step = source.StartActivity("step"))
        {
            Assert.NotNull(step);
            call.Dispose();
            Assert.Same(step, Activity.Current);
        }

        Assert.Same(outer, Activity.Current);
        Assert.StartsWith("Cancelled: ", Assert.Single(Responses()).Content);
    }

    [Theory]
    [MemberData(nameof(StreamingKinds))]
    public async Task Disposing_a_stream_from_a_flow_with_no_span_leaves_that_flow_without_one(CallKind kind)
    {
        using var source = new ActivitySource("Kronikol.Tests.CallEnding." + Guid.NewGuid().ToString("N"));
        using var listener = Listen(source);
        var interceptor = new GrpcTrackingInterceptor(Options());

        IDisposable call;
        using (var outer = source.StartActivity("outer"))
        {
            Assert.NotNull(outer);
            call = StartUnfinished(interceptor, kind);
        }

        // Another flow, as a fixture's tear-down is: it inherits nothing, so it has no span of its own.
        Task<Activity?> disposing;
        using (ExecutionContext.SuppressFlow())
            disposing = Task.Run(() =>
            {
                call.Dispose();
                return Activity.Current;
            });

        Assert.Null(await disposing);
        Assert.StartsWith("Cancelled: ", Assert.Single(Responses()).Content);
    }

    // ─── A stream whose own call misbehaves at its end ──────────

    [Theory]
    [MemberData(nameof(StreamingKinds))]
    public void A_stream_whose_status_cannot_be_read_is_still_disposed_and_logged_Cancelled(CallKind kind)
    {
        var interceptor = new GrpcTrackingInterceptor(Options());
        var disposed = 0;
        var call = StartUnfinished(interceptor, kind,
            getStatus: () => throw new NotSupportedException("this call has no status"), dispose: () => disposed++);

        call.Dispose();

        Assert.Equal(1, disposed);
        var response = Assert.Single(Responses());
        Assert.Equal(HttpStatusCode.RequestTimeout, response.StatusCode!.Value);
        Assert.StartsWith("Cancelled: ", response.Content);
    }

    [Theory]
    [MemberData(nameof(StreamingKinds))]
    public void A_stream_whose_dispose_throws_is_still_logged_and_its_span_ended(CallKind kind)
    {
        var method = CallKinds.Method(kind, "ending.Probe", "Span" + Guid.NewGuid().ToString("N"));
        var stopped = 0;
        using var listener = new ActivityListener
        {
            ShouldListenTo = s => s.Name == "Kronikol.Grpc",
            Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllData,
            ActivityStopped = a => { if (a.OperationName == method.FullName) Interlocked.Increment(ref stopped); },
        };
        ActivitySource.AddActivityListener(listener);
        var interceptor = new GrpcTrackingInterceptor(Options());
        var context = CallKinds.Context(kind, method: method);
        Action dispose = () => throw new ObjectDisposedException("channel");
        Func<Status> unfinished = () => throw new InvalidOperationException("not finished");
        IDisposable call = kind switch
        {
            CallKind.ServerStreaming => interceptor.AsyncServerStreamingCall("request", context, (_, _) =>
                new AsyncServerStreamingCall<string>(new CallKinds.ListReader(["one"]), Task.FromResult(new Metadata()),
                    unfinished, () => new Metadata(), dispose)),
            CallKind.ClientStreaming => interceptor.AsyncClientStreamingCall(context, _ =>
                new AsyncClientStreamingCall<string, string>(new CallKinds.NullWriter(), new TaskCompletionSource<string>().Task,
                    Task.FromResult(new Metadata()), unfinished, () => new Metadata(), dispose)),
            _ => interceptor.AsyncDuplexStreamingCall(context, _ =>
                new AsyncDuplexStreamingCall<string, string>(new CallKinds.NullWriter(), new CallKinds.ListReader(["one"]),
                    Task.FromResult(new Metadata()), unfinished, () => new Metadata(), dispose)),
        };

        Assert.Throws<ObjectDisposedException>(call.Dispose);

        Assert.StartsWith("Cancelled: ", Assert.Single(Responses()).Content);
        Assert.Equal(1, stopped);
    }

    // ─── Unary calls that fail without an RpcException ─────────

    [Theory]
    [InlineData(CallKind.AsyncUnary, "cancelled")]
    [InlineData(CallKind.AsyncUnary, "other")]
    [InlineData(CallKind.BlockingUnary, "cancelled")]
    [InlineData(CallKind.BlockingUnary, "other")]
    public async Task A_unary_call_that_fails_without_an_RpcException_is_logged_as_its_failure(CallKind kind, string failure)
    {
        // A channel with ThrowOperationCanceledOnCancellation throws OperationCanceledException where it would throw
        // RpcException(Cancelled); anything else the transport throws is a failure the call did not describe.
        Exception thrown = failure == "cancelled"
            ? new OperationCanceledException("the caller cancelled")
            : new InvalidOperationException("the channel is shut down");
        var interceptor = new GrpcTrackingInterceptor(Options());
        var context = CallKinds.Context(kind);

        if (kind == CallKind.AsyncUnary)
        {
            var call = interceptor.AsyncUnaryCall("request", context, (_, _) =>
                new AsyncUnaryCall<string>(Task.FromException<string>(thrown), Task.FromResult(new Metadata()),
                    () => throw new InvalidOperationException("not finished"), () => new Metadata(), () => { }));
            Assert.Same(thrown, await Assert.ThrowsAnyAsync<Exception>(() => call.ResponseAsync));
        }
        else
        {
            Assert.Same(thrown, Assert.ThrowsAny<Exception>(() =>
                interceptor.BlockingUnaryCall<string, string>("request", context, (_, _) => throw thrown)));
        }

        var response = Assert.Single(Responses());
        if (failure == "cancelled")
        {
            Assert.Equal(HttpStatusCode.RequestTimeout, response.StatusCode!.Value);
            Assert.Equal("Cancelled: the caller cancelled", response.Content);
        }
        else
        {
            Assert.Equal(HttpStatusCode.InternalServerError, response.StatusCode!.Value);
            Assert.Equal("Unknown: the channel is shut down", response.Content);
        }
    }

    // ─── A call that fails as it starts ─────────────────────────

    [Theory]
    [MemberData(nameof(CallKinds.All), MemberType = typeof(CallKinds))]
    public void A_call_whose_continuation_throws_as_it_starts_is_logged_as_its_failure(CallKind kind)
    {
        // A disposed channel throws as the call starts, after the request is logged: that is the call's answer.
        var thrown = new ObjectDisposedException("GrpcChannel");
        var interceptor = new GrpcTrackingInterceptor(Options());
        var context = CallKinds.Context(kind);

        Assert.Same(thrown, Assert.ThrowsAny<Exception>(() =>
        {
            switch (kind)
            {
                case CallKind.AsyncUnary: interceptor.AsyncUnaryCall<string, string>("request", context, (_, _) => throw thrown); break;
                case CallKind.BlockingUnary: interceptor.BlockingUnaryCall<string, string>("request", context, (_, _) => throw thrown); break;
                case CallKind.ServerStreaming: interceptor.AsyncServerStreamingCall<string, string>("request", context, (_, _) => throw thrown); break;
                case CallKind.ClientStreaming: interceptor.AsyncClientStreamingCall<string, string>(context, _ => throw thrown); break;
                default: interceptor.AsyncDuplexStreamingCall<string, string>(context, _ => throw thrown); break;
            }
        }));

        var response = Assert.Single(Responses());
        Assert.Equal(HttpStatusCode.InternalServerError, response.StatusCode!.Value);
        Assert.StartsWith("Unknown: ", response.Content);
    }

    // ─── A stream's other endings ───────────────────────────────

    /// <summary>A response stream whose next read throws <paramref name="failure"/>.</summary>
    private sealed class ThrowingReader(Exception failure) : IAsyncStreamReader<string>
    {
        public string Current => throw new InvalidOperationException("no message");
        public Task<bool> MoveNext(CancellationToken cancellationToken) => Task.FromException<bool>(failure);
    }

    [Theory]
    [InlineData("cancelled")]
    [InlineData("other")]
    public async Task A_stream_whose_read_fails_without_an_RpcException_is_logged_as_its_failure(string failure)
    {
        Exception thrown = failure == "cancelled"
            ? new OperationCanceledException("the caller cancelled")
            : new IOException("the connection dropped");
        var interceptor = new GrpcTrackingInterceptor(Options());

        using var call = interceptor.AsyncServerStreamingCall("request", CallKinds.Context(CallKind.ServerStreaming), (_, _) =>
            new AsyncServerStreamingCall<string>(new ThrowingReader(thrown), Task.FromResult(new Metadata()),
                () => throw new InvalidOperationException("not finished"), () => new Metadata(), () => { }));

        Assert.Same(thrown, await Assert.ThrowsAnyAsync<Exception>(() => call.ResponseStream.MoveNext(CancellationToken.None)));

        var response = Assert.Single(Responses());
        Assert.Equal(failure == "cancelled" ? HttpStatusCode.RequestTimeout : HttpStatusCode.InternalServerError, response.StatusCode!.Value);
        Assert.Equal(failure == "cancelled" ? "Cancelled: the caller cancelled" : "Unknown: the connection dropped", response.Content);
    }

    [Theory]
    [MemberData(nameof(StreamingKinds))]
    public void A_stream_disposed_once_it_has_failed_is_logged_with_its_failure(CallKind kind)
    {
        var interceptor = new GrpcTrackingInterceptor(Options());
        var call = StartUnfinished(interceptor, kind, getStatus: () => new Status(StatusCode.NotFound, "no such order"));

        call.Dispose();

        var response = Assert.Single(Responses());
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode!.Value);
        Assert.StartsWith("NotFound: ", response.Content);
    }

    [Fact]
    public async Task A_streams_response_is_logged_in_the_phase_the_call_started_in()
    {
        var interceptor = new GrpcTrackingInterceptor(Options());

        TestPhaseContext.Current = TestPhase.Setup;
        try
        {
            using var call = interceptor.AsyncServerStreamingCall("request", CallKinds.Context(CallKind.ServerStreaming),
                (_, _) => CallKinds.ServerStream("one"));
            TestPhaseContext.Current = TestPhase.Action;
            while (await call.ResponseStream.MoveNext(CancellationToken.None)) { }
        }
        finally
        {
            TestPhaseContext.Reset();
        }

        Assert.Equal(TestPhase.Setup, Assert.Single(Responses()).Phase);
    }
}
