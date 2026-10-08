using Grpc.Core;
using Grpc.Core.Interceptors;
using Kronikol.Extensions.Grpc;

namespace Kronikol.Tests.Grpc;

public enum CallKind
{
    AsyncUnary,
    BlockingUnary,
    ServerStreaming,
    ClientStreaming,
    Duplex,
}

/// <summary>
/// Runs one call of each of the interceptor's five kinds against a fake continuation that records the context it was
/// handed. Facts that read <c>Activity.Current</c> after a call must call the interceptor in their own body instead:
/// a change an async helper makes to an <c>AsyncLocal</c> never reaches its caller.
/// </summary>
public static class CallKinds
{
    public static readonly TheoryData<CallKind> All =
        new(CallKind.AsyncUnary, CallKind.BlockingUnary, CallKind.ServerStreaming, CallKind.ClientStreaming, CallKind.Duplex);

    private static readonly Marshaller<string> StringMarshaller = Marshallers.Create(
        msg => System.Text.Encoding.UTF8.GetBytes(msg),
        bytes => System.Text.Encoding.UTF8.GetString(bytes));

    public static Method<string, string> Method(CallKind kind, string service = "probe.Probe", string name = "Call") =>
        new(kind switch
        {
            CallKind.ServerStreaming => MethodType.ServerStreaming,
            CallKind.ClientStreaming => MethodType.ClientStreaming,
            CallKind.Duplex => MethodType.DuplexStreaming,
            _ => MethodType.Unary,
        }, service, name, StringMarshaller, StringMarshaller);

    public static ClientInterceptorContext<string, string> Context(CallKind kind = CallKind.AsyncUnary, Metadata? headers = null, Method<string, string>? method = null) =>
        new(method ?? Method(kind), null, new CallOptions(headers: headers));

    /// <summary>One call of <paramref name="kind"/>, run to its end; returns the context the continuation was handed.</summary>
    public static async Task<ClientInterceptorContext<string, string>> InvokeAsync(
        GrpcTrackingInterceptor interceptor, CallKind kind, Metadata? headers = null, Method<string, string>? method = null)
    {
        ClientInterceptorContext<string, string>? seen = null;
        var context = Context(kind, headers, method);
        switch (kind)
        {
            case CallKind.AsyncUnary:
                await interceptor.AsyncUnaryCall("request", context, (_, c) => { seen = c; return Unary(); }).ResponseAsync;
                break;
            case CallKind.BlockingUnary:
                interceptor.BlockingUnaryCall("request", context, (_, c) => { seen = c; return "response"; });
                break;
            case CallKind.ServerStreaming:
                using (var call = interceptor.AsyncServerStreamingCall("request", context, (_, c) => { seen = c; return ServerStream("one"); }))
                    while (await call.ResponseStream.MoveNext(CancellationToken.None)) { }
                break;
            case CallKind.ClientStreaming:
                using (var call = interceptor.AsyncClientStreamingCall(context, c => { seen = c; return ClientStream(); }))
                {
                    await call.RequestStream.CompleteAsync();
                    await call.ResponseAsync;
                }
                break;
            case CallKind.Duplex:
                using (var call = interceptor.AsyncDuplexStreamingCall(context, c => { seen = c; return DuplexStream("one"); }))
                {
                    await call.RequestStream.CompleteAsync();
                    while (await call.ResponseStream.MoveNext(CancellationToken.None)) { }
                }
                break;
        }

        return seen!.Value;
    }

    public static AsyncUnaryCall<string> Unary(string response = "response") =>
        new(Task.FromResult(response), Task.FromResult(new Metadata()), () => Status.DefaultSuccess, () => new Metadata(), () => { });

    public static AsyncServerStreamingCall<string> ServerStream(params string[] messages) =>
        new(new ListReader(messages), Task.FromResult(new Metadata()), () => Status.DefaultSuccess, () => new Metadata(), () => { });

    public static AsyncClientStreamingCall<string, string> ClientStream(string response = "response") =>
        new(new NullWriter(), Task.FromResult(response), Task.FromResult(new Metadata()), () => Status.DefaultSuccess, () => new Metadata(), () => { });

    public static AsyncDuplexStreamingCall<string, string> DuplexStream(params string[] messages) =>
        new(new NullWriter(), new ListReader(messages), Task.FromResult(new Metadata()), () => Status.DefaultSuccess, () => new Metadata(), () => { });

    /// <summary>A response stream that yields its messages, then ends, or throws <see cref="Failure"/> when one is set.</summary>
    public sealed class ListReader(IEnumerable<string> messages, RpcException? failure = null) : IAsyncStreamReader<string>
    {
        private readonly Queue<string> _messages = new(messages);

        public RpcException? Failure { get; } = failure;
        public string Current { get; private set; } = "";

        public Task<bool> MoveNext(CancellationToken cancellationToken)
        {
            if (_messages.TryDequeue(out var next))
            {
                Current = next;
                return Task.FromResult(true);
            }

            return Failure is null ? Task.FromResult(false) : Task.FromException<bool>(Failure);
        }
    }

    public sealed class NullWriter : IClientStreamWriter<string>
    {
        public WriteOptions? WriteOptions { get; set; }
        public Task WriteAsync(string message) => Task.CompletedTask;
        public Task CompleteAsync() => Task.CompletedTask;
    }
}
