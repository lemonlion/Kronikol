using System.Net;
using Google.Protobuf;
using Google.Protobuf.WellKnownTypes;
using Grpc.Core;
using Grpc.Core.Interceptors;
using Kronikol.Extensions.Grpc;
using Kronikol.Tracking;

namespace Kronikol.Tests.Grpc;

/// <summary>
/// A call's messages are written into its records, and writing them must never fail the call. <c>JsonFormatter.Default</c>
/// throws on a <c>google.protobuf.Any</c> whose type its registry, empty by default, does not hold (any type at all,
/// <c>google.protobuf.Struct</c> included), and until this release that turned a call carrying one, such as a
/// <c>google.rpc.Status</c> with details, into a failed call.
/// </summary>
public class MessageContentTests
{
    private readonly string _testId = Guid.NewGuid().ToString();

    private GrpcTrackingOptions Options() => new()
    {
        ServiceName = "Orders",
        CallerName = "Test",
        CurrentTestInfoFetcher = () => ("Message content", _testId),
    };

    private RequestResponseLog[] Logs() => RequestResponseLogger.RequestAndResponseLogs.Where(l => l.TestId == _testId).ToArray();

    private static readonly Marshaller<Any> AnyMarshaller = Marshallers.Create(m => m.ToByteArray(), Any.Parser.ParseFrom);

    private static ClientInterceptorContext<Any, Any> Context(MethodType type) =>
        new(new Method<Any, Any>(type, "probe.Probe", "Any", AnyMarshaller, AnyMarshaller), null, new CallOptions());

    private static Any Packed(string key) => Any.Pack(new Struct { Fields = { [key] = Value.ForString("value") } });

    private const string PackedType = "type.googleapis.com/google.protobuf.Struct";

    private sealed class NullWriter<T> : IClientStreamWriter<T>
    {
        public WriteOptions? WriteOptions { get; set; }
        public Task WriteAsync(T message) => Task.CompletedTask;
        public Task CompleteAsync() => Task.CompletedTask;
    }

    [Fact]
    public async Task An_async_unary_call_carrying_an_Any_succeeds_and_records_its_messages()
    {
        var reply = Packed("reply");
        var call = new GrpcTrackingInterceptor(Options()).AsyncUnaryCall(Packed("request"), Context(MethodType.Unary), (_, _) =>
            new AsyncUnaryCall<Any>(Task.FromResult(reply), Task.FromResult(new Metadata()), () => Status.DefaultSuccess, () => new Metadata(), () => { }));

        Assert.Same(reply, await call.ResponseAsync);

        Assert.Contains(PackedType, Assert.Single(Logs(), l => l.Type == RequestResponseType.Request).Content);
        var response = Assert.Single(Logs(), l => l.Type == RequestResponseType.Response);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode!.Value);
        Assert.Contains(PackedType, response.Content);
    }

    [Fact]
    public void A_blocking_unary_call_carrying_an_Any_succeeds_and_records_its_messages()
    {
        var reply = Packed("reply");

        Assert.Same(reply, new GrpcTrackingInterceptor(Options()).BlockingUnaryCall(Packed("request"), Context(MethodType.Unary), (_, _) => reply));

        Assert.Contains(PackedType, Assert.Single(Logs(), l => l.Type == RequestResponseType.Request).Content);
        Assert.Contains(PackedType, Assert.Single(Logs(), l => l.Type == RequestResponseType.Response).Content);
    }

    [Fact]
    public async Task A_client_stream_whose_response_is_an_Any_succeeds_and_records_it()
    {
        var reply = Packed("reply");
        using var call = new GrpcTrackingInterceptor(Options()).AsyncClientStreamingCall(Context(MethodType.ClientStreaming), _ =>
            new AsyncClientStreamingCall<Any, Any>(new NullWriter<Any>(), Task.FromResult(reply), Task.FromResult(new Metadata()),
                () => Status.DefaultSuccess, () => new Metadata(), () => { }));

        Assert.Same(reply, await call.ResponseAsync);

        Assert.Contains(PackedType, Assert.Single(Logs(), l => l.Type == RequestResponseType.Response).Content);
    }

    /// <summary>A request whose text cannot be written at all.</summary>
    private sealed class Unwritable
    {
        public override string ToString() => throw new InvalidOperationException("no text for this one");
    }

    [Fact]
    public void A_message_that_cannot_be_written_at_all_does_not_fail_the_call()
    {
        var marshaller = Marshallers.Create<Unwritable>(_ => [], _ => new Unwritable());
        var context = new ClientInterceptorContext<Unwritable, Unwritable>(
            new Method<Unwritable, Unwritable>(MethodType.Unary, "probe.Probe", "Unwritable", marshaller, marshaller), null, new CallOptions());
        var reply = new Unwritable();

        Assert.Same(reply, new GrpcTrackingInterceptor(Options()).BlockingUnaryCall(new Unwritable(), context, (_, _) => reply));

        Assert.All(Logs(), l => Assert.Contains("no text for this one", l.Content));
        Assert.Equal(2, Logs().Length);
    }
}
