using System.Diagnostics;
using System.Net;
using Grpc.Core;
using Kronikol.Extensions.Grpc;
using Kronikol.Tests.Grpc.Hop;
using Kronikol.Tests.Grpc.MultiHost;
using Kronikol.Tracking;

namespace Kronikol.Tests.Grpc;

/// <summary>
/// A streaming call's outcome is recorded when its stream ends (plans/GRPC_IDENTITY_PROPAGATION_PLAN.md section 4.5,
/// T40 to T43). Until this release the response of a server-streaming, client-streaming or duplex call was logged as
/// <c>OK</c>, with no content, as soon as the call started, so a stream that failed read as a success.
/// </summary>
public class StreamingOutcomeTests
{
    private readonly string _testId = Guid.NewGuid().ToString();

    private GrpcTrackingOptions Options() => new()
    {
        ServiceName = "Orders",
        CallerName = "Test",
        CurrentTestInfoFetcher = () => ("Streaming outcome", _testId),
    };

    private RequestResponseLog[] Responses() => RequestResponseLogger.RequestAndResponseLogs
        .Where(l => l.TestId == _testId && l.Type == RequestResponseType.Response).ToArray();

    private static RpcException NotFound() => new(new Status(StatusCode.NotFound, "no such order"));

    // ─── T40: a stream that fails ───────────────────────────────

    [Fact]
    public async Task A_server_stream_that_fails_after_one_reply_is_logged_once_as_the_failure()
    {
        var interceptor = new GrpcTrackingInterceptor(Options());

        using var call = interceptor.AsyncServerStreamingCall("request", CallKinds.Context(CallKind.ServerStreaming), (_, _) =>
            new AsyncServerStreamingCall<string>(new CallKinds.ListReader(["one"], NotFound()), Task.FromResult(new Metadata()),
                () => throw new InvalidOperationException("not finished"), () => new Metadata(), () => { }));

        Assert.True(await call.ResponseStream.MoveNext(CancellationToken.None));
        Assert.Empty(Responses());
        await Assert.ThrowsAsync<RpcException>(() => call.ResponseStream.MoveNext(CancellationToken.None));

        var response = Assert.Single(Responses());
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode!.Value);
        Assert.StartsWith("NotFound: ", response.Content);
    }

    [Fact]
    public async Task A_real_server_stream_that_fails_NotFound_is_logged_as_NotFound()
    {
        await using var b = await HopHost.StartAsync("Orders");
        var client = b.CreateTestClient(Options());

        using var call = client.Stream(new HopRequest { Replies = 1, FailWith = (int)StatusCode.NotFound },
            cancellationToken: TestContext.Current.CancellationToken);
        Assert.True(await call.ResponseStream.MoveNext(TestContext.Current.CancellationToken));
        var thrown = await Assert.ThrowsAsync<RpcException>(() => call.ResponseStream.MoveNext(TestContext.Current.CancellationToken));
        Assert.Equal(StatusCode.NotFound, thrown.StatusCode);

        var response = Assert.Single(Responses());
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode!.Value);
        Assert.StartsWith("NotFound: ", response.Content);
    }

    // ─── T41: a stream that completes ───────────────────────────

    [Fact]
    public async Task A_completed_server_stream_is_logged_OK_after_its_last_message_and_its_span_ends_then()
    {
        var method = CallKinds.Method(CallKind.ServerStreaming, "outcome.Probe", "Span" + Guid.NewGuid().ToString("N"));
        var stopped = new List<Activity>();
        using var listener = new ActivityListener
        {
            ShouldListenTo = source => source.Name == "Kronikol.Grpc",
            Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllData,
            ActivityStopped = a => { if (a.OperationName == method.FullName) lock (stopped) stopped.Add(a); },
        };
        ActivitySource.AddActivityListener(listener);
        var interceptor = new GrpcTrackingInterceptor(Options());

        using var call = interceptor.AsyncServerStreamingCall("request", CallKinds.Context(CallKind.ServerStreaming, method: method),
            (_, _) => CallKinds.ServerStream("one", "two"));

        Assert.True(await call.ResponseStream.MoveNext(CancellationToken.None));
        Assert.True(await call.ResponseStream.MoveNext(CancellationToken.None));
        Assert.Empty(Responses());
        Assert.Empty(stopped);
        var lastRead = DateTimeOffset.UtcNow;
        Assert.False(await call.ResponseStream.MoveNext(CancellationToken.None));

        var response = Assert.Single(Responses());
        Assert.Equal(HttpStatusCode.OK, response.StatusCode!.Value);
        Assert.True(response.Timestamp >= lastRead);
        Assert.Single(stopped);
    }

    // ─── T42: client streams and duplex streams ─────────────────

    [Fact]
    public async Task A_client_stream_is_logged_when_its_response_arrives_with_the_response()
    {
        var interceptor = new GrpcTrackingInterceptor(Options());
        var response = new TaskCompletionSource<string>();

        using var call = interceptor.AsyncClientStreamingCall(CallKinds.Context(CallKind.ClientStreaming), _ =>
            new AsyncClientStreamingCall<string, string>(new CallKinds.NullWriter(), response.Task, Task.FromResult(new Metadata()),
                () => Status.DefaultSuccess, () => new Metadata(), () => { }));
        await call.RequestStream.WriteAsync("one");
        Assert.Empty(Responses());

        response.SetResult("total: 1");
        await call.ResponseAsync;

        var logged = Assert.Single(Responses());
        Assert.Equal(HttpStatusCode.OK, logged.StatusCode!.Value);
        Assert.Equal("total: 1", logged.Content);
    }

    [Fact]
    public async Task A_client_stream_whose_response_fails_is_logged_as_the_failure()
    {
        var interceptor = new GrpcTrackingInterceptor(Options());

        using var call = interceptor.AsyncClientStreamingCall(CallKinds.Context(CallKind.ClientStreaming), _ =>
            new AsyncClientStreamingCall<string, string>(new CallKinds.NullWriter(), Task.FromException<string>(NotFound()),
                Task.FromResult(new Metadata()), () => new Status(StatusCode.NotFound, "no such order"), () => new Metadata(), () => { }));

        await Assert.ThrowsAsync<RpcException>(() => call.ResponseAsync);

        var logged = Assert.Single(Responses());
        Assert.Equal(HttpStatusCode.NotFound, logged.StatusCode!.Value);
        Assert.StartsWith("NotFound: ", logged.Content);
    }

    [Fact]
    public async Task A_duplex_stream_is_logged_when_it_ends_and_as_its_failure_when_it_fails()
    {
        var interceptor = new GrpcTrackingInterceptor(Options());

        using (var call = interceptor.AsyncDuplexStreamingCall(CallKinds.Context(CallKind.Duplex), _ => CallKinds.DuplexStream("one")))
        {
            Assert.True(await call.ResponseStream.MoveNext(CancellationToken.None));
            Assert.Empty(Responses());
            Assert.False(await call.ResponseStream.MoveNext(CancellationToken.None));
        }

        Assert.Equal(HttpStatusCode.OK, Assert.Single(Responses()).StatusCode!.Value);

        using (var failing = interceptor.AsyncDuplexStreamingCall(CallKinds.Context(CallKind.Duplex), _ =>
                   new AsyncDuplexStreamingCall<string, string>(new CallKinds.NullWriter(), new CallKinds.ListReader([], NotFound()),
                       Task.FromResult(new Metadata()), () => throw new InvalidOperationException("not finished"), () => new Metadata(), () => { })))
        {
            await Assert.ThrowsAsync<RpcException>(() => failing.ResponseStream.MoveNext(CancellationToken.None));
        }

        Assert.Contains(Responses(), r => Equals(r.StatusCode!.Value, HttpStatusCode.NotFound) && r.Content!.StartsWith("NotFound: ", StringComparison.Ordinal));
        Assert.Equal(2, Responses().Length);
    }

    [Fact]
    public async Task A_real_duplex_stream_that_fails_is_logged_as_the_failure()
    {
        await using var b = await HopHost.StartAsync("Orders");
        var client = b.CreateTestClient(Options());

        using var call = client.Chat(cancellationToken: TestContext.Current.CancellationToken);
        await call.RequestStream.WriteAsync(new HopRequest { Replies = 1, FailWith = (int)StatusCode.PermissionDenied }, TestContext.Current.CancellationToken);
        Assert.True(await call.ResponseStream.MoveNext(TestContext.Current.CancellationToken));
        await Assert.ThrowsAsync<RpcException>(() => call.ResponseStream.MoveNext(TestContext.Current.CancellationToken));

        var response = Assert.Single(Responses());
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode!.Value);
        Assert.StartsWith("PermissionDenied: ", response.Content);
    }

    // ─── T43: a call disposed mid-stream ────────────────────────

    [Fact]
    public async Task A_call_disposed_mid_stream_is_logged_Cancelled_once()
    {
        var interceptor = new GrpcTrackingInterceptor(Options());
        var call = interceptor.AsyncServerStreamingCall("request", CallKinds.Context(CallKind.ServerStreaming), (_, _) =>
            new AsyncServerStreamingCall<string>(new CallKinds.ListReader(["one", "two"]), Task.FromResult(new Metadata()),
                () => throw new InvalidOperationException("not finished"), () => new Metadata(), () => { }));

        Assert.True(await call.ResponseStream.MoveNext(CancellationToken.None));
        call.Dispose();
        call.Dispose();

        var response = Assert.Single(Responses());
        Assert.Equal(HttpStatusCode.RequestTimeout, response.StatusCode!.Value); // Cancelled, as a unary call maps it
        Assert.StartsWith("Cancelled: ", response.Content);
    }

    [Fact]
    public async Task A_call_disposed_once_its_status_is_known_is_logged_with_that_status()
    {
        var interceptor = new GrpcTrackingInterceptor(Options());
        var call = interceptor.AsyncServerStreamingCall("request", CallKinds.Context(CallKind.ServerStreaming), (_, _) =>
            new AsyncServerStreamingCall<string>(new CallKinds.ListReader(["one"]), Task.FromResult(new Metadata()),
                () => Status.DefaultSuccess, () => new Metadata(), () => { }));

        Assert.True(await call.ResponseStream.MoveNext(CancellationToken.None));
        call.Dispose();

        Assert.Equal(HttpStatusCode.OK, Assert.Single(Responses()).StatusCode!.Value);
    }

    [Fact]
    public async Task A_stream_read_to_its_end_and_then_disposed_is_logged_once()
    {
        var interceptor = new GrpcTrackingInterceptor(Options());

        using (var call = interceptor.AsyncServerStreamingCall("request", CallKinds.Context(CallKind.ServerStreaming), (_, _) => CallKinds.ServerStream("one")))
        {
            while (await call.ResponseStream.MoveNext(CancellationToken.None)) { }
        }

        Assert.Equal(HttpStatusCode.OK, Assert.Single(Responses()).StatusCode!.Value);
    }
}
