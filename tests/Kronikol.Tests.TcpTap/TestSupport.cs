using System.Net;
using System.Net.Sockets;
using System.Text;
using Kronikol.Extensions.TcpTap;
using Kronikol.Tracking;
using MongoDB.Bson;
using MongoDB.Bson.IO;
using MongoDB.Bson.Serialization;

namespace Kronikol.Tests.TcpTap;

/// <summary>Collects everything a tap records so a test can assert on it.</summary>
internal sealed class RecordingSink : IRequestResponseSink
{
    private readonly List<RequestResponseLog> _logs = [];

    public IReadOnlyList<RequestResponseLog> Logs
    {
        get { lock (_logs) return _logs.ToArray(); }
    }

    public IReadOnlyList<RequestResponseLog> Requests => Logs.Where(l => l.Type == RequestResponseType.Request).ToArray();

    public IReadOnlyList<RequestResponseLog> Responses => Logs.Where(l => l.Type == RequestResponseType.Response).ToArray();

    public void Log(RequestResponseLog log)
    {
        lock (_logs) _logs.Add(log);
    }
}

/// <summary>Drives a decoder directly, without a socket, so protocol tests stay in-memory and fast.</summary>
internal sealed class DecoderHarness : IDisposable
{
    private readonly TcpTapCore _tap;

    private DecoderHarness(TcpTapOptions options)
    {
        Options = options;
        Sink = new RecordingSink();
        options.Sink = Sink;
        options.EmitActivities = false;
        _tap = new TcpTapCore(options);
        Context = new TcpTapConnectionContext(_tap, 1, new IPEndPoint(IPAddress.Loopback, 12345));
        Decoder = options.DecoderFactory!(Context);
    }

    public TcpTapOptions Options { get; }

    public RecordingSink Sink { get; }

    public TcpTapConnectionContext Context { get; }

    public IProtocolDecoder Decoder { get; }

    public long DecodeErrors => _tap.DecodeErrors;

    public static DecoderHarness Redis(Action<RedisTapOptions>? configure = null)
    {
        var options = new RedisTapOptions { ListenPort = 0, ForwardPort = 6379, CallerName = "svc", ServiceName = "redis" };
        configure?.Invoke(options);
        return new DecoderHarness(options);
    }

    public static DecoderHarness Mongo(Action<MongoTapOptions>? configure = null)
    {
        var options = new MongoTapOptions { ListenPort = 0, ForwardPort = 27017, CallerName = "svc", ServiceName = "mongo" };
        configure?.Invoke(options);
        return new DecoderHarness(options);
    }

    public void ClientToServer(byte[] bytes, DateTimeOffset? at = null) =>
        Decoder.OnClientToServer(bytes, at ?? DateTimeOffset.UtcNow);

    public void ServerToClient(byte[] bytes, DateTimeOffset? at = null) =>
        Decoder.OnServerToClient(bytes, at ?? DateTimeOffset.UtcNow);

    public void ClientToServer(string text, DateTimeOffset? at = null) => ClientToServer(Encoding.UTF8.GetBytes(text), at);

    public void ServerToClient(string text, DateTimeOffset? at = null) => ServerToClient(Encoding.UTF8.GetBytes(text), at);

    /// <summary>Feeds bytes one at a time, so a test proves the decoder survives arbitrary segmentation.</summary>
    public void ServerToClientByteByByte(string text)
    {
        foreach (var b in Encoding.UTF8.GetBytes(text))
            ServerToClient([b]);
    }

    public void Dispose() => Decoder.Dispose();
}

/// <summary>Builds RESP wire bytes for tests.</summary>
internal static class Resp
{
    public static string Command(params string[] arguments)
    {
        var builder = new StringBuilder();
        builder.Append('*').Append(arguments.Length).Append("\r\n");
        foreach (var argument in arguments)
            builder.Append('$').Append(Encoding.UTF8.GetByteCount(argument)).Append("\r\n").Append(argument).Append("\r\n");
        return builder.ToString();
    }

    public static string Bulk(string? value) => value is null ? "$-1\r\n" : $"${Encoding.UTF8.GetByteCount(value)}\r\n{value}\r\n";

    public static string Simple(string value) => $"+{value}\r\n";

    public static string Error(string value) => $"-{value}\r\n";

    public static string Integer(long value) => $":{value}\r\n";

    public static string Array(params string[] encodedItems) => $"*{encodedItems.Length}\r\n{string.Concat(encodedItems)}";

    public static string NullArray() => "*-1\r\n";
}

/// <summary>Builds MongoDB wire-protocol messages for tests.</summary>
internal static class MongoWire
{
    public const int OpMsg = 2013;
    public const int OpQuery = 2004;
    public const int OpReply = 1;
    public const int OpCompressed = 2012;

    /// <summary>An OP_MSG with a kind-0 body and any number of kind-1 document sequences.</summary>
    public static byte[] Msg(
        int requestId, int responseTo, BsonDocument body, uint flags = 0,
        params (string Identifier, BsonDocument[] Documents)[] sequences)
    {
        using var payload = new MemoryStream();
        WriteInt32(payload, unchecked((int)flags));

        payload.WriteByte(0);
        var bodyBytes = ToBson(body);
        payload.Write(bodyBytes, 0, bodyBytes.Length);

        foreach (var (identifier, documents) in sequences)
        {
            payload.WriteByte(1);
            var identifierBytes = Encoding.UTF8.GetBytes(identifier);
            var documentBytes = documents.Select(ToBson).ToArray();
            var size = 4 + identifierBytes.Length + 1 + documentBytes.Sum(d => d.Length);
            WriteInt32(payload, size);
            payload.Write(identifierBytes, 0, identifierBytes.Length);
            payload.WriteByte(0);
            foreach (var document in documentBytes)
                payload.Write(document, 0, document.Length);
        }

        if ((flags & 1) != 0)
            WriteInt32(payload, 0); // a checksum the tap must skip, never validate

        return Frame(requestId, responseTo, OpMsg, payload.ToArray());
    }

    /// <summary>A legacy OP_QUERY, as the driver's connection handshake still sends.</summary>
    public static byte[] Query(int requestId, string fullCollectionName, BsonDocument query)
    {
        using var payload = new MemoryStream();
        WriteInt32(payload, 0); // flags
        var name = Encoding.UTF8.GetBytes(fullCollectionName);
        payload.Write(name, 0, name.Length);
        payload.WriteByte(0);
        WriteInt32(payload, 0); // numberToSkip
        WriteInt32(payload, -1); // numberToReturn
        var queryBytes = ToBson(query);
        payload.Write(queryBytes, 0, queryBytes.Length);
        return Frame(requestId, 0, OpQuery, payload.ToArray());
    }

    /// <summary>A legacy OP_REPLY, the answer to <see cref="Query"/>.</summary>
    public static byte[] Reply(int requestId, int responseTo, BsonDocument document)
    {
        using var payload = new MemoryStream();
        WriteInt32(payload, 8); // responseFlags: AwaitCapable
        payload.Write(BitConverter.GetBytes(0L), 0, 8); // cursorId
        WriteInt32(payload, 0); // startingFrom
        WriteInt32(payload, 1); // numberReturned
        var bytes = ToBson(document);
        payload.Write(bytes, 0, bytes.Length);
        return Frame(requestId, responseTo, OpReply, payload.ToArray());
    }

    /// <summary>An OP_COMPRESSED envelope; the tap must forward it and decode nothing.</summary>
    public static byte[] Compressed(int requestId, int responseTo, int originalOpCode, byte compressorId, byte[] compressedBody)
    {
        using var payload = new MemoryStream();
        WriteInt32(payload, originalOpCode);
        WriteInt32(payload, compressedBody.Length);
        payload.WriteByte(compressorId);
        payload.Write(compressedBody, 0, compressedBody.Length);
        return Frame(requestId, responseTo, OpCompressed, payload.ToArray());
    }

    public static byte[] ToBson(BsonDocument document)
    {
        using var stream = new MemoryStream();
        using (var writer = new BsonBinaryWriter(stream))
            BsonSerializer.Serialize(writer, document);
        return stream.ToArray();
    }

    private static byte[] Frame(int requestId, int responseTo, int opCode, byte[] payload)
    {
        using var stream = new MemoryStream();
        WriteInt32(stream, 16 + payload.Length);
        WriteInt32(stream, requestId);
        WriteInt32(stream, responseTo);
        WriteInt32(stream, opCode);
        stream.Write(payload, 0, payload.Length);
        return stream.ToArray();
    }

    private static void WriteInt32(Stream stream, int value)
    {
        Span<byte> bytes = stackalloc byte[4];
        System.Buffers.Binary.BinaryPrimitives.WriteInt32LittleEndian(bytes, value);
        stream.Write(bytes);
    }
}

/// <summary>A TCP server that answers whatever the test tells it to, and remembers exactly what arrived.</summary>
internal sealed class StubServer : IAsyncDisposable
{
    private readonly TcpListener _listener;
    private readonly Func<byte[], byte[]?> _reply;
    private readonly CancellationTokenSource _cts = new();
    private readonly List<byte> _received = [];
    private readonly TaskCompletionSource _closed = new(TaskCreationOptions.RunContinuationsAsynchronously);

    public StubServer(Func<byte[], byte[]?> reply)
    {
        _reply = reply;
        _listener = new TcpListener(IPAddress.Loopback, 0);
        _listener.Start();
        Port = ((IPEndPoint)_listener.LocalEndpoint).Port;
        _ = Task.Run(AcceptAsync);
    }

    public int Port { get; }

    /// <summary>Every byte that reached the server, in order.</summary>
    public byte[] Received
    {
        get { lock (_received) return _received.ToArray(); }
    }

    /// <summary>Completes when a connection's read side has seen EOF (the tap propagated a half-close).</summary>
    public Task ClientHalfClosed => _closed.Task;

    private async Task AcceptAsync()
    {
        while (!_cts.IsCancellationRequested)
        {
            TcpClient client;
            try { client = await _listener.AcceptTcpClientAsync(_cts.Token); }
            catch (Exception ex) when (ex is OperationCanceledException or ObjectDisposedException or SocketException) { return; }
            _ = Task.Run(() => ServeAsync(client));
        }
    }

    private async Task ServeAsync(TcpClient client)
    {
        using (client)
        {
            var stream = client.GetStream();
            var buffer = new byte[16384];
            while (true)
            {
                int read;
                try { read = await stream.ReadAsync(buffer, _cts.Token); }
                catch (Exception) { break; }
                if (read == 0)
                {
                    _closed.TrySetResult();
                    break;
                }

                var chunk = buffer[..read];
                lock (_received) _received.AddRange(chunk);

                var reply = _reply(chunk);
                if (reply is { Length: > 0 })
                {
                    try { await stream.WriteAsync(reply, _cts.Token); }
                    catch (Exception) { break; }
                }
            }
        }
    }

    public async ValueTask DisposeAsync()
    {
        await _cts.CancelAsync();
        _listener.Stop();
        _cts.Dispose();
    }
}

internal static class Wait
{
    /// <summary>Polls until the condition holds or the timeout elapses; returns whether it held.</summary>
    public static async Task<bool> UntilAsync(Func<bool> condition, int timeoutMs = 5000)
    {
        var deadline = Environment.TickCount64 + timeoutMs;
        while (Environment.TickCount64 < deadline)
        {
            if (condition())
                return true;
            await Task.Delay(20);
        }

        return condition();
    }
}
