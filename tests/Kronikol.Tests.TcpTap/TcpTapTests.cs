using System.Diagnostics;
using System.Net.Sockets;
using System.Text;
using Kronikol.Extensions.TcpTap;
using Kronikol.Ingestion;
using Kronikol.Tracking;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using MongoDB.Bson;

namespace Kronikol.Tests.TcpTap;

public class TcpTapTests
{
    private static RedisTapOptions RedisOptions(StubServer server, IRequestResponseSink sink, Action<RedisTapOptions>? configure = null)
    {
        var options = new RedisTapOptions
        {
            ListenPort = 0,
            ForwardHost = "127.0.0.1",
            ForwardPort = server.Port,
            CallerName = "svc",
            ServiceName = "redis",
            Sink = sink,
            EmitActivities = false,
        };
        configure?.Invoke(options);
        return options;
    }

    private static async Task<(TcpClient Client, NetworkStream Stream)> ConnectAsync(TcpTapCore tap)
    {
        var client = new TcpClient();
        await client.ConnectAsync("127.0.0.1", tap.BoundPort);
        client.NoDelay = true;
        return (client, client.GetStream());
    }

    private static async Task<byte[]> ReadAtLeastAsync(NetworkStream stream, int count, int timeoutMs = 5000)
    {
        var buffer = new byte[Math.Max(count, 4096)];
        var read = 0;
        using var cts = new CancellationTokenSource(timeoutMs);
        while (read < count)
        {
            var n = await stream.ReadAsync(buffer.AsMemory(read), cts.Token);
            if (n == 0)
                break;
            read += n;
        }

        return buffer[..read];
    }

    // ---- the tee ----------------------------------------------------------------------------------

    [Fact]
    public async Task EveryByteIsForwardedUnchangedInBothDirections()
    {
        // A payload with every byte value, and no protocol structure at all: the tee must not care.
        var payload = Enumerable.Range(0, 256).Select(i => (byte)i).ToArray();
        var reply = payload.Reverse().ToArray();

        await using var server = new StubServer(_ => reply);
        var sink = new RecordingSink();
        var options = RedisOptions(server, sink);
        options.DecoderFactory = _ => new NullDecoder();

        await using var tap = new TcpTapCore(options);
        await tap.StartAsync();

        var (client, stream) = await ConnectAsync(tap);
        using (client)
        {
            await stream.WriteAsync(payload);
            var received = await ReadAtLeastAsync(stream, reply.Length);
            Assert.Equal(reply, received);
        }

        Assert.True(await Wait.UntilAsync(() => server.Received.Length == payload.Length));
        Assert.Equal(payload, server.Received);
        Assert.Equal(1, tap.ConnectionsAccepted);
        Assert.Equal(payload.Length, tap.BytesClientToServer);
        Assert.Equal(reply.Length, tap.BytesServerToClient);
    }

    [Fact]
    public async Task ALargePipelinedBurstArrivesByteForByte()
    {
        var payload = new byte[512 * 1024];
        Random.Shared.NextBytes(payload);

        await using var server = new StubServer(_ => null);
        var sink = new RecordingSink();
        var options = RedisOptions(server, sink);
        options.DecoderFactory = _ => new NullDecoder();

        await using var tap = new TcpTapCore(options);
        await tap.StartAsync();

        var (client, stream) = await ConnectAsync(tap);
        using (client)
        {
            await stream.WriteAsync(payload);
            Assert.True(await Wait.UntilAsync(() => server.Received.Length == payload.Length, 15000));
        }

        Assert.Equal(payload, server.Received);
    }

    [Fact]
    public async Task AHalfCloseIsPropagatedUpstreamWhileTheReplyStillFlowsBack()
    {
        var replied = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        await using var server = new StubServer(_ =>
        {
            replied.TrySetResult();
            return "late reply"u8.ToArray();
        });

        var options = RedisOptions(server, new RecordingSink());
        options.DecoderFactory = _ => new NullDecoder();

        await using var tap = new TcpTapCore(options);
        await tap.StartAsync();

        var (client, stream) = await ConnectAsync(tap);
        using (client)
        {
            await stream.WriteAsync("bye"u8.ToArray());
            await replied.Task.WaitAsync(TimeSpan.FromSeconds(5));

            // Shut down only our send side; the reply must still reach us.
            client.Client.Shutdown(SocketShutdown.Send);
            Assert.True(await server.ClientHalfClosed.WaitAsync(TimeSpan.FromSeconds(5)).ContinueWith(t => t.IsCompletedSuccessfully));

            var received = await ReadAtLeastAsync(stream, "late reply".Length);
            Assert.Equal("late reply", Encoding.UTF8.GetString(received));
        }
    }

    [Fact]
    public async Task AListenPortOfZeroBindsAFreeEphemeralPort()
    {
        await using var server = new StubServer(_ => null);
        var options = RedisOptions(server, new RecordingSink());
        options.DecoderFactory = _ => new NullDecoder();

        await using var tap = new TcpTapCore(options);
        await tap.StartAsync();

        Assert.True(tap.BoundPort > 0);
        Assert.NotEqual(server.Port, tap.BoundPort);
    }

    // ---- capture through the real decoders --------------------------------------------------------

    [Fact]
    public async Task ARedisExchangeThroughTheTapIsRecorded()
    {
        await using var server = new StubServer(_ => Encoding.UTF8.GetBytes(Resp.Bulk("cached")));
        var sink = new RecordingSink();

        await using var tap = new RedisTap(RedisOptions(server, sink));
        await tap.StartAsync();

        var (client, stream) = await ConnectAsync(tap);
        using (client)
        {
            await stream.WriteAsync(Encoding.UTF8.GetBytes(Resp.Command("GET", "k")));
            await ReadAtLeastAsync(stream, Resp.Bulk("cached").Length);
        }

        Assert.True(await Wait.UntilAsync(() => sink.Responses.Count == 1));
        var response = sink.Responses[0];
        Assert.Equal("Get (Hit)", response.Method.Value?.ToString());
        Assert.Equal("redis://db0/k", response.Uri.ToString());
        Assert.Equal("cached", response.Content);
        Assert.Equal(1, tap.InteractionsCaptured);
        Assert.Equal(0, tap.SegmentsDropped);
        Assert.Equal(0, tap.DecodeErrors);
    }

    [Fact]
    public async Task AMongoExchangeThroughTheTapIsRecorded()
    {
        var reply = MongoWire.Msg(2, 1, new BsonDocument
        {
            { "cursor", new BsonDocument { { "firstBatch", new BsonArray() }, { "id", 0L }, { "ns", "app.Trial" } } },
            { "ok", 1.0 },
        });

        await using var server = new StubServer(_ => reply);
        var sink = new RecordingSink();

        var options = new MongoTapOptions
        {
            ListenPort = 0,
            ForwardHost = "127.0.0.1",
            ForwardPort = server.Port,
            CallerName = "svc",
            ServiceName = "mongo",
            Sink = sink,
            EmitActivities = false,
        };

        await using var tap = new MongoTap(options);
        await tap.StartAsync();

        var (client, stream) = await ConnectAsync(tap);
        using (client)
        {
            await stream.WriteAsync(MongoWire.Msg(1, 0, new BsonDocument { { "find", "Trial" }, { "$db", "app" } }));
            await ReadAtLeastAsync(stream, reply.Length);
        }

        Assert.True(await Wait.UntilAsync(() => sink.Responses.Count == 1));
        Assert.Equal("Find ← Trial", sink.Responses[0].Method.Value?.ToString());
        Assert.Equal("mongodb:///app/Trial", sink.Responses[0].Uri.ToString());
    }

    [Fact]
    public async Task ADecoderThatThrowsStopsDecodingButKeepsForwarding()
    {
        await using var server = new StubServer(_ => "pong"u8.ToArray());
        var sink = new RecordingSink();
        var diagnostics = new List<string>();

        var options = RedisOptions(server, sink, o => o.Log = message => { lock (diagnostics) diagnostics.Add(message); });
        options.DecoderFactory = _ => new ThrowingDecoder();

        await using var tap = new TcpTapCore(options);
        await tap.StartAsync();

        var (client, stream) = await ConnectAsync(tap);
        using (client)
        {
            await stream.WriteAsync("ping"u8.ToArray());
            var received = await ReadAtLeastAsync(stream, 4);
            Assert.Equal("pong", Encoding.UTF8.GetString(received));

            // The connection still works after the decoder gave up.
            await stream.WriteAsync("ping"u8.ToArray());
            Assert.Equal("pong", Encoding.UTF8.GetString(await ReadAtLeastAsync(stream, 4)));
        }

        Assert.True(await Wait.UntilAsync(() => tap.DecodeErrors == 1));
        Assert.Empty(sink.Logs);
        lock (diagnostics)
            Assert.Contains(diagnostics, d => d.Contains("gave up") && d.Contains("forwarding continues"));
        Assert.Equal(1, tap.DecodingDisabledConnections);
        Assert.Equal(0, tap.DecoderResets);
        var entry = Assert.Single(tap.Diagnostics());
        Assert.Contains("decoding disabled on 1 connection(s)", entry.Message);
        Assert.Contains("InvalidOperationException: decoder is broken", entry.Message);
    }

    [Fact]
    public async Task ATwelveMebibyteValueThroughTheTapIsForwardedIntactAndRecordedAsAPreview()
    {
        const int size = 12 * 1024 * 1024;
        var command = Encoding.UTF8.GetBytes($"*3\r\n$3\r\nSET\r\n$3\r\nbig\r\n${size}\r\n")
            .Concat(Enumerable.Repeat((byte)'b', size))
            .Concat("\r\n"u8.ToArray())
            .ToArray();

        var total = 0;
        await using var server = new StubServer(chunk =>
        {
            total += chunk.Length;
            return total == command.Length ? "+OK\r\n"u8.ToArray() : null;
        });
        var sink = new RecordingSink();
        var degradations = new List<CaptureDegradation>();

        await using var tap = new RedisTap(RedisOptions(server, sink, o => o.OnCaptureDegraded = d => { lock (degradations) degradations.Add(d); }));
        await tap.StartAsync();

        var (client, stream) = await ConnectAsync(tap);
        using (client)
        {
            await stream.WriteAsync(command);
            var reply = await ReadAtLeastAsync(stream, 5, 15000);
            Assert.Equal("+OK\r\n", Encoding.UTF8.GetString(reply));
        }

        Assert.True(await Wait.UntilAsync(() => server.Received.Length == command.Length, 15000));
        Assert.Equal(command, server.Received);

        Assert.True(await Wait.UntilAsync(() => sink.Responses.Count == 1));
        var request = Assert.Single(sink.Requests);
        Assert.Equal("Set", request.Method.Value?.ToString());
        Assert.Equal("redis://db0/big", request.Uri.ToString());
        Assert.StartsWith(new string('b', 65408), request.Content);
        Assert.EndsWith(" …[bulk string truncated: 12,582,912 bytes on the wire, 65,408 kept]", request.Content);

        Assert.Equal(1, tap.InteractionsCaptured);
        Assert.Equal(1, tap.OversizePayloadsSkipped);
        Assert.Equal(size - 65408, tap.BytesSkipped);
        Assert.Equal(0, tap.DecodingDisabledConnections);
        Assert.Equal(0, tap.DecoderResets);
        Assert.Equal(0, tap.DecodeErrors);
        Assert.Equal(0, tap.SegmentsDropped);
        var entry = Assert.Single(tap.Diagnostics());
        Assert.Contains("1 oversize payload(s) streamed past (largest 12,582,912 B", entry.Message);
        lock (degradations)
        {
            var degradation = Assert.Single(degradations);
            Assert.Equal(CaptureDegradationKind.OversizePayloadSkipped, degradation.Kind);
            Assert.Equal("svc→redis", degradation.Tap);
        }
    }

    [Fact]
    public async Task BytesFlowingWithoutAnyInteractionShowUpAsAPossibleStall()
    {
        await using var server = new StubServer(_ => null);
        var options = RedisOptions(server, new RecordingSink(), o => o.DecodingStallBytes = 4096);
        options.DecoderFactory = _ => new NullDecoder();

        await using var tap = new TcpTapCore(options);
        await tap.StartAsync();

        Assert.Empty(tap.Diagnostics());

        var (client, stream) = await ConnectAsync(tap);
        using (client)
        {
            await stream.WriteAsync(new byte[8192]);
            Assert.True(await Wait.UntilAsync(() => server.Received.Length == 8192));
        }

        Assert.True(tap.BytesSinceLastInteraction >= 8192);
        Assert.Null(tap.LastInteractionAt);
        var entry = Assert.Single(tap.Diagnostics());
        Assert.Contains("decoding may have stalled", entry.Message);
        Assert.Contains("none recorded yet", entry.Message);
    }

    [Fact]
    public async Task DroppedSegmentsAreReportedOncePerConnectionPerMinute()
    {
        var release = new ManualResetEventSlim(false);
        await using var server = new StubServer(_ => null);
        var degradations = new List<CaptureDegradation>();
        var options = RedisOptions(server, new RecordingSink(), o =>
        {
            o.ChannelCapacity = 2;
            o.ReadBufferBytes = 1024;
            o.OnCaptureDegraded = d => { lock (degradations) degradations.Add(d); };
        });
        options.DecoderFactory = _ => new BlockingDecoder(release);

        await using var tap = new TcpTapCore(options);
        await tap.StartAsync();

        var (client, stream) = await ConnectAsync(tap);
        try
        {
            for (var i = 0; i < 50; i++)
                await stream.WriteAsync(new byte[1024]);
            Assert.True(await Wait.UntilAsync(() => tap.SegmentsDropped > 1, 5000));
        }
        finally
        {
            release.Set();
            client.Dispose();
        }

        lock (degradations)
            Assert.Single(degradations.Where(d => d.Kind == CaptureDegradationKind.SegmentsDropped));
        Assert.Contains(tap.Diagnostics(), e => e.Message.Contains("segment(s) dropped"));
    }

    [Fact]
    public async Task ASlowDecoderDropsSegmentsRatherThanSlowingTheWire()
    {
        var release = new ManualResetEventSlim(false);
        await using var server = new StubServer(_ => null);
        var options = RedisOptions(server, new RecordingSink(), o =>
        {
            o.ChannelCapacity = 2;
            o.ReadBufferBytes = 1024;
        });
        options.DecoderFactory = _ => new BlockingDecoder(release);

        await using var tap = new TcpTapCore(options);
        await tap.StartAsync();

        var (client, stream) = await ConnectAsync(tap);
        try
        {
            var payload = new byte[1024];
            var watch = Stopwatch.StartNew();
            for (var i = 0; i < 400; i++)
                await stream.WriteAsync(payload);

            // Forwarding completed even though the decoder never consumed a single segment.
            Assert.True(await Wait.UntilAsync(() => server.Received.Length == 400 * 1024, 15000),
                $"only {server.Received.Length} of {400 * 1024} bytes were forwarded");
            watch.Stop();

            Assert.True(await Wait.UntilAsync(() => tap.SegmentsDropped > 0, 2000), "the bounded queue never dropped");
            Assert.Equal(tap.SegmentsDropped, tap.SegmentsDroppedClientToServer);
        }
        finally
        {
            release.Set();
            client.Dispose();
        }
    }

    // ---- attribution, spans and sinks -------------------------------------------------------------

    [Fact]
    public async Task ActivitiesAreEmittedWhenSomethingIsListening()
    {
        var activities = new List<Activity>();
        using var listener = new ActivityListener
        {
            ShouldListenTo = source => source.Name == TcpTapCore.ActivitySourceName,
            Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllDataAndRecorded,
            ActivityStopped = activity => { lock (activities) activities.Add(activity); },
        };
        ActivitySource.AddActivityListener(listener);

        await using var server = new StubServer(_ => Encoding.UTF8.GetBytes(Resp.Bulk("v")));
        var sink = new RecordingSink();
        await using var tap = new RedisTap(RedisOptions(server, sink, o => o.EmitActivities = true));
        await tap.StartAsync();

        var (client, stream) = await ConnectAsync(tap);
        using (client)
        {
            await stream.WriteAsync(Encoding.UTF8.GetBytes(Resp.Command("GET", "k")));
            await ReadAtLeastAsync(stream, Resp.Bulk("v").Length);
        }

        Assert.True(await Wait.UntilAsync(() => sink.Responses.Count == 1));
        lock (activities)
        {
            var activity = Assert.Single(activities);
            Assert.Equal("redis Get (Hit)", activity.DisplayName);
            Assert.Equal("Redis", activity.GetTagItem("db.system"));
            Assert.Equal("svc", activity.GetTagItem("kronikol.caller"));
        }

        Assert.Equal(activities[0].TraceId.ToString(), sink.Responses[0].ActivityTraceId);
    }

    [Fact]
    public async Task TheNdjsonSinkWritesReplayableRecords()
    {
        var path = Path.Combine(Path.GetTempPath(), $"kronikol-tcptap-{Guid.NewGuid():N}.ndjson");
        try
        {
            await using var server = new StubServer(_ => Encoding.UTF8.GetBytes(Resp.Bulk("v")));
            using var writer = new NdjsonInteractionWriter(path);

            await using var tap = new RedisTap(RedisOptions(server, writer, o => o.FallbackTestId = "bucket"));
            await tap.StartAsync();

            var (client, stream) = await ConnectAsync(tap);
            using (client)
            {
                await stream.WriteAsync(Encoding.UTF8.GetBytes(Resp.Command("GET", "k")));
                await ReadAtLeastAsync(stream, Resp.Bulk("v").Length);
            }

            Assert.True(await Wait.UntilAsync(() => writer.LinesWritten == 2));
            writer.Flush();

            var records = NdjsonInteractionReader.ReadFile(path).ToList();
            Assert.Equal(2, records.Count);
            Assert.All(records, r => Assert.Equal("bucket", r.TestId));
            Assert.Equal("redis://db0/k", records[0].Uri);
            Assert.Equal("Get (Hit)", records[1].Method);
        }
        finally
        {
            if (File.Exists(path)) File.Delete(path);
        }
    }

    // ---- options and DI ---------------------------------------------------------------------------

    [Fact]
    public void ValidationRejectsAnIncompleteConfiguration()
    {
        Assert.Throws<ArgumentException>(() => new RedisTapOptions { ForwardPort = 6379, ServiceName = "redis" }.Validate());
        Assert.Throws<ArgumentOutOfRangeException>(() => new RedisTapOptions { CallerName = "a", ForwardPort = 0 }.Validate());
        Assert.Throws<ArgumentException>(() => new TcpTapOptions { CallerName = "a", ServiceName = "b", ForwardPort = 1 }.Validate());
    }

    [Fact]
    public void TheRedisDefaultsAreTheRedisTopology()
    {
        var options = new RedisTapOptions();
        Assert.Equal("redis", options.ServiceName);
        Assert.Equal(6379, options.ForwardPort);
        Assert.Equal("localhost", options.ListenHost);
        Assert.Equal("Redis", options.DependencyCategory);
        Assert.Equal(TapVerbosity.Detailed, options.Verbosity);
        Assert.Equal(65536, options.BodyCapBytes);
        Assert.True(options.CaptureReplies);
        Assert.Contains("AUTH", options.ExcludedCommands);
    }

    [Fact]
    public void TheMongoDefaultsAreTheMongoTopology()
    {
        var options = new MongoTapOptions();
        Assert.Equal("mongo", options.ServiceName);
        Assert.Equal(27017, options.ForwardPort);
        Assert.Equal("MongoDB", options.DependencyCategory);
        Assert.False(options.TrackGetMore);
        Assert.Equal(10, options.MaxResponseDocuments);
        Assert.Contains("saslStart", options.ExcludedCommands);
    }

    [Fact]
    public async Task TheDiRegistrationStartsAndStopsTheTapsWithTheHost()
    {
        var services = new ServiceCollection();
        services.AddRedisTapTestTracking(o =>
        {
            o.ListenPort = 0;
            o.ForwardPort = 6379;
            o.CallerName = "svc";
        });
        services.AddMongoTapTestTracking(o =>
        {
            o.ListenPort = 0;
            o.ForwardPort = 27017;
            o.CallerName = "svc";
        });

        await using var provider = services.BuildServiceProvider();
        var taps = provider.GetServices<TcpTapCore>().ToList();
        Assert.Equal(2, taps.Count);
        Assert.NotNull(provider.GetService<RedisTap>());
        Assert.NotNull(provider.GetService<MongoTap>());

        var hosted = provider.GetServices<IHostedService>().OfType<TcpTapHostedService>().ToList();
        Assert.Equal(2, hosted.Count);

        foreach (var service in hosted)
            await service.StartAsync(CancellationToken.None);
        Assert.All(hosted, service => Assert.True(service.Tap.BoundPort > 0));

        foreach (var service in hosted)
            await service.StopAsync(CancellationToken.None);
        Assert.All(hosted, service => Assert.False(service.Tap.IsListening));
    }

    [Fact]
    public void CopyToCarriesEveryProtocolOption()
    {
        var source = new MongoTapOptions
        {
            ListenPort = 111,
            ForwardPort = 222,
            CallerName = "caller",
            TrackGetMore = true,
            MaxResponseDocuments = 3,
            LogFilterText = false,
            Verbosity = TapVerbosity.Raw,
        };
        source.ExcludedCommands.Clear();
        source.ExcludedCommands.Add("only");

        var target = new MongoTapOptions();
        source.CopyTo(target);

        Assert.Equal(111, target.ListenPort);
        Assert.Equal(222, target.ForwardPort);
        Assert.Equal("caller", target.CallerName);
        Assert.True(target.TrackGetMore);
        Assert.Equal(3, target.MaxResponseDocuments);
        Assert.False(target.LogFilterText);
        Assert.Equal(TapVerbosity.Raw, target.Verbosity);
        Assert.Equal(["only"], target.ExcludedCommands.ToArray());
    }

    private sealed class NullDecoder : IProtocolDecoder
    {
        public void OnClientToServer(ReadOnlySpan<byte> data, DateTimeOffset timestamp) { }

        public void OnServerToClient(ReadOnlySpan<byte> data, DateTimeOffset timestamp) { }

        public void OnConnectionClosed() { }

        public void Dispose() { }
    }

    private sealed class ThrowingDecoder : IProtocolDecoder
    {
        public void OnClientToServer(ReadOnlySpan<byte> data, DateTimeOffset timestamp) =>
            throw new InvalidOperationException("decoder is broken");

        public void OnServerToClient(ReadOnlySpan<byte> data, DateTimeOffset timestamp) =>
            throw new InvalidOperationException("decoder is broken");

        public void OnConnectionClosed() { }

        public void Dispose() { }
    }

    private sealed class BlockingDecoder(ManualResetEventSlim release) : IProtocolDecoder
    {
        public void OnClientToServer(ReadOnlySpan<byte> data, DateTimeOffset timestamp) => release.Wait();

        public void OnServerToClient(ReadOnlySpan<byte> data, DateTimeOffset timestamp) => release.Wait();

        public void OnConnectionClosed() { }

        public void Dispose() { }
    }
}
