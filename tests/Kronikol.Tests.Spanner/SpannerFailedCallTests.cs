using Google.Cloud.Spanner.V1;
using Google.Protobuf;
using Grpc.Core;
using Grpc.Core.Interceptors;
using Kronikol.Extensions.Spanner;
using Kronikol.History;
using Kronikol.Tests.Tracking;
using Kronikol.Tracking;

namespace Kronikol.Tests.Spanner;

/// <summary>
/// A Spanner call through the interceptor that throws (plans/FAILED_CALLS_PLAN.md F7, F12, §5.7): whatever it throws, an
/// <see cref="RpcException"/> or anything else, and whether the continuation, its response or its stream throws, the call is
/// answered <c>Error</c> with the call's message as its note and the message chain as its error, and the exception reaches the
/// caller as it was thrown.
/// </summary>
public class SpannerFailedCallTests
{
    private readonly string _testId = Guid.NewGuid().ToString();

    private RequestResponseLog[] Logs() => RequestResponseLogger.RequestAndResponseLogs.Where(l => l.TestId == _testId).ToArray();

    private SpannerTrackingInterceptor Interceptor() => new(new SpannerTrackingOptions
    {
        ServiceName = "Spanner",
        CallerName = "Caller",
        CurrentTestInfoFetcher = () => ("Orders are read", _testId),
    });

    private static readonly ExecuteSqlRequest Sql = new() { Sql = "SELECT * FROM Orders", Session = "projects/p/instances/i/databases/d/sessions/s" };

    private static ClientInterceptorContext<ExecuteSqlRequest, TResponse> Context<TResponse>(string methodName, MethodType type)
        where TResponse : class =>
        new(new Method<ExecuteSqlRequest, TResponse>(type, "google.spanner.v1.Spanner", methodName,
            Marshallers.Create<ExecuteSqlRequest>(m => m.ToByteArray(), _ => default!),
            Marshallers.Create<TResponse>(m => ((IMessage)m).ToByteArray(), _ => default!)), null, new CallOptions());

    private static AsyncUnaryCall<ResultSet> Faulted(Exception exception) =>
        new(Task.FromException<ResultSet>(exception), Task.FromResult(new Metadata()), () => Status.DefaultSuccess, () => new Metadata(), () => { });

    public static TheoryData<string> Failures() => ["rpc", "other"];

    private static Exception Failure(string kind) => kind == "rpc"
        ? new RpcException(new Status(StatusCode.Unavailable, "failed to connect to all addresses"))
        : new InvalidOperationException("The session pool is exhausted.");

    [Theory]
    [Trait(FailedCallContract.Trait, FailedCallContract.Value)]
    [MemberData(nameof(Failures))]
    public async Task A_call_whose_response_fails_is_answered_Error_with_its_message_and_left_as_it_was(string kind)
    {
        var thrown = Failure(kind);

        var call = Interceptor().AsyncUnaryCall(Sql, Context<ResultSet>("ExecuteSql", MethodType.Unary), (_, _) => Faulted(thrown));
        var caught = await Assert.ThrowsAnyAsync<Exception>(() => call.ResponseAsync);

        Assert.Same(thrown, caught);
        var (_, answer) = FailedCallContract.AssertAnswered(Logs(), thrown, status: "Error");
        Assert.Equal(thrown.Message, answer.Content);
    }

    [Fact]
    [Trait(FailedCallContract.Trait, FailedCallContract.Value)]
    public void A_call_whose_continuation_throws_is_answered_where_it_left_the_request_alone()
    {
        var thrown = new InvalidOperationException("The channel is shut down.");

        var caught = Assert.Throws<InvalidOperationException>(() => Interceptor().AsyncUnaryCall(Sql, Context<ResultSet>("ExecuteSql", MethodType.Unary),
            (_, _) => { FailedCallContract.Throw(thrown); return null!; }));

        FailedCallContract.AssertUntouched(caught, thrown);
        FailedCallContract.AssertAnswered(Logs(), thrown, status: "Error");
    }

    [Theory]
    [Trait(FailedCallContract.Trait, FailedCallContract.Value)]
    [MemberData(nameof(Failures))]
    public async Task A_stream_that_faults_is_answered_Error_however_it_faults(string kind)
    {
        var thrown = Failure(kind);
        var call = Interceptor().AsyncServerStreamingCall(Sql, Context<PartialResultSet>("ExecuteStreamingSql", MethodType.ServerStreaming),
            (_, _) => new AsyncServerStreamingCall<PartialResultSet>(new FaultingStream(thrown), Task.FromResult(new Metadata()),
                () => Status.DefaultSuccess, () => new Metadata(), () => { }));

        var caught = await Assert.ThrowsAnyAsync<Exception>(() => call.ResponseStream.MoveNext(TestContext.Current.CancellationToken));
        call.Dispose();

        Assert.Same(thrown, caught);
        FailedCallContract.AssertAnswered(Logs(), thrown, status: "Error");
    }

    [Fact]
    public async Task History_rules_before_12_read_a_failure_that_was_unanswered_or_a_success_as_they_recorded_it()
    {
        var call = Interceptor().AsyncUnaryCall(Sql, Context<ResultSet>("ExecuteSql", MethodType.Unary), (_, _) => Faulted(Failure("other")));
        await Assert.ThrowsAsync<InvalidOperationException>(() => call.ResponseAsync);

        Assert.Equal("Error", Assert.Single(InteractionShape.Calls(Logs(), null, 12)).Status);
        foreach (var rule in InteractionShape.KeptVersions.Where(v => v < 12))
            Assert.Null(Assert.Single(InteractionShape.Calls(Logs(), null, rule)).Status);
    }

    [Fact]
    public async Task An_RpcException_was_answered_Error_before_so_every_rule_reads_it_so()
    {
        var call = Interceptor().AsyncUnaryCall(Sql, Context<ResultSet>("ExecuteSql", MethodType.Unary), (_, _) => Faulted(Failure("rpc")));
        await Assert.ThrowsAsync<RpcException>(() => call.ResponseAsync);

        foreach (var rule in InteractionShape.KeptVersions)
            Assert.Equal("Error", Assert.Single(InteractionShape.Calls(Logs(), null, rule)).Status);
    }

    private sealed class FaultingStream(Exception exception) : IAsyncStreamReader<PartialResultSet>
    {
        public PartialResultSet Current => null!;

        public async Task<bool> MoveNext(CancellationToken cancellationToken)
        {
            await Task.Yield();
            FailedCallContract.Throw(exception);
            return false;
        }
    }
}
