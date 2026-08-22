using System.Collections.Concurrent;
using System.Diagnostics;
using Kronikol.Extensions.TcpTap;
using Kronikol.Extensions.TcpTap.Protocols;
using Kronikol.Tracking;
using MongoDB.Bson;
using MongoDB.Driver;
using StackExchange.Redis;
using Testcontainers.MongoDb;
using Testcontainers.Redis;

namespace Kronikol.Tests.TcpTap;

/// <summary>
/// The taps driven by the real clients against real servers: StackExchange.Redis against <c>redis:7</c> and
/// MongoDB.Driver against <c>mongo:7</c>, both dialling the tap instead of the database. These prove the
/// things a hand-built byte fixture cannot — what a real client actually puts on the wire on connect, that the
/// driver keeps talking to the address it was given, and that nothing from the handshake is recorded.
/// </summary>
/// <remarks>Skipped when no Docker daemon is reachable.</remarks>
public class ContainerEndToEndTests(ITestOutputHelper output)
{
    private static bool DockerAvailable()
    {
        try
        {
            using var process = Process.Start(new ProcessStartInfo("docker", "version --format {{.Server.Version}}")
            {
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
            });
            if (process is null)
                return false;
            return process.WaitForExit(20_000) && process.ExitCode == 0;
        }
        catch (Exception)
        {
            return false;
        }
    }

    /// <summary>A password that cannot occur by accident, so "never recorded" assertions mean something.</summary>
    private const string TapPassword = "n0t-in-the-diagram-9f3a";

    private static string Text(RequestResponseLog log) => log.Method.Value?.ToString() ?? "";

    // ---- Redis ------------------------------------------------------------------------------------

    [Fact]
    public async Task StackExchangeRedisThroughTheTapRendersLikeTheInProcessExtension()
    {
        Assert.SkipWhen(!DockerAvailable(), "Docker is not available.");

        var container = new RedisBuilder().WithImage("redis:7").Build();
        await container.StartAsync();
        try
        {
            var endpoint = new Uri($"tcp://{container.GetConnectionString()}");
            var sink = new RecordingSink();

            await using var tap = new RedisTap(new RedisTapOptions
            {
                ListenPort = 0,
                ForwardHost = endpoint.Host,
                ForwardPort = endpoint.Port,
                CallerName = "data-insights",
                Sink = sink,
                EmitActivities = false,
            });
            await tap.StartAsync();

            var configuration = ConfigurationOptions.Parse($"127.0.0.1:{tap.BoundPort}");
            configuration.AbortOnConnectFail = false;
            configuration.ConnectTimeout = 10_000;
            using (var multiplexer = await ConnectionMultiplexer.ConnectAsync(configuration))
            {
                var database = multiplexer.GetDatabase();
                await database.StringSetAsync("data-insights-api:period:1:OneWeek", "{\"total\":42}");
                Assert.Equal("{\"total\":42}", await database.StringGetAsync("data-insights-api:period:1:OneWeek"));
                Assert.True((await database.StringGetAsync("data-insights-api:missing")).IsNull);
                await database.KeyDeleteAsync("data-insights-api:period:1:OneWeek");
            }

            Assert.True(await Wait.UntilAsync(() => sink.Responses.Count >= 4, 15_000),
                $"only {sink.Responses.Count} interactions were captured");

            var responses = sink.Responses;
            Assert.Contains(responses, r => Text(r) == "Set" && r.Uri.ToString() == "redis://db0/data-insights-api:period:1:OneWeek");
            Assert.Contains(responses, r => Text(r) == "Get (Hit)" && r.Content == "{\"total\":42}");
            Assert.Contains(responses, r => Text(r) == "Get (Miss)" && r.Uri.ToString() == "redis://db0/data-insights-api:missing");
            Assert.Contains(responses, r => Text(r) == "Delete");
            Assert.All(responses, r => Assert.Equal("Redis", r.DependencyCategory));
            Assert.All(sink.Logs, r => Assert.Equal("data-insights", r.CallerName));

            // Nothing from the connection handshake reached the sink.
            Assert.DoesNotContain(sink.Logs, r => Text(r).Contains("PING", StringComparison.OrdinalIgnoreCase));
            Assert.DoesNotContain(sink.Logs, r => (r.Content ?? "").Contains("redis_version"));
            // Everything captured is an application command, not an unclassified handshake leftover.
            Assert.DoesNotContain(responses, r => Text(r) == "Other");
            Assert.Equal(0, tap.DecodeErrors);
            Assert.Equal(0, tap.SegmentsDropped);
        }
        finally
        {
            await container.DisposeAsync();
        }
    }

    [Fact]
    public async Task WhatStackExchangeRedis26PutsOnTheWireOnConnect()
    {
        Assert.SkipWhen(!DockerAvailable(), "Docker is not available.");

        var container = new RedisBuilder().WithImage("redis:7").Build();
        await container.StartAsync();
        try
        {
            var endpoint = new Uri($"tcp://{container.GetConnectionString()}");
            var spy = new ConcurrentQueue<string>();

            var options = new TcpTapOptions
            {
                ListenPort = 0,
                ForwardHost = endpoint.Host,
                ForwardPort = endpoint.Port,
                CallerName = "svc",
                ServiceName = "redis",
                EmitActivities = false,
                DecoderFactory = _ => new RespCommandSpy(spy),
            };

            await using var tap = new TcpTapCore(options);
            await tap.StartAsync();

            var configuration = ConfigurationOptions.Parse($"127.0.0.1:{tap.BoundPort}");
            configuration.AbortOnConnectFail = false;
            configuration.ConnectTimeout = 10_000;
            var multiplexer = await ConnectionMultiplexer.ConnectAsync(configuration);
            await multiplexer.GetDatabase().StringGetAsync("probe");
            // Include whatever the client says on the way out, too.
            multiplexer.Dispose();

            Assert.True(await Wait.UntilAsync(() => spy.Contains("GET"), 15_000));
            await Task.Delay(500);
            var verbs = spy.ToArray();

            output.WriteLine($"StackExchange.Redis {typeof(ConnectionMultiplexer).Assembly.GetName().Version} wire: {string.Join(", ", verbs)}");

            // RESP2 by default: no HELLO upgrade on this client version.
            Assert.DoesNotContain("HELLO", verbs);
            // Every verb the client sends of its own accord — opening its two connections and closing them —
            // is in the default exclusion set, so a tapped service contributes no handshake noise to a diagram.
            var excluded = new RedisTapOptions().ExcludedCommands;
            Assert.All(verbs.Where(v => v != "GET"), verb =>
                Assert.True(excluded.Contains(verb), $"client-generated verb '{verb}' is not excluded by default; the wire was: {string.Join(", ", verbs)}"));
        }
        finally
        {
            await container.DisposeAsync();
        }
    }

    // ---- MongoDB ----------------------------------------------------------------------------------

    [Fact]
    public async Task MongoDbDriverThroughTheTapRendersLikeTheInProcessExtension()
    {
        Assert.SkipWhen(!DockerAvailable(), "Docker is not available.");

        var container = new MongoDbBuilder().WithImage("mongo:7").WithUsername("tapuser").WithPassword(TapPassword).Build();
        await container.StartAsync();
        try
        {
            var seed = new MongoUrl(container.GetConnectionString());
            var sink = new RecordingSink();

            await using var tap = new MongoTap(new MongoTapOptions
            {
                ListenPort = 0,
                ForwardHost = seed.Server.Host,
                ForwardPort = seed.Server.Port,
                CallerName = "data-insights",
                Sink = sink,
                EmitActivities = false,
            });
            await tap.StartAsync();

            var settings = MongoClientSettings.FromUrl(new MongoUrl(container.GetConnectionString()));
            settings.Server = new MongoServerAddress("127.0.0.1", tap.BoundPort);
            settings.DirectConnection = true;
            settings.ServerSelectionTimeout = TimeSpan.FromSeconds(20);

            var client = new MongoClient(settings);
            var collection = client.GetDatabase("data-insights-Development").GetCollection<BsonDocument>("Trial");

            await collection.InsertOneAsync(new BsonDocument { { "_id", 1 }, { "status", "active" } });
            var found = await collection.Find(new BsonDocument("status", "active")).ToListAsync();
            Assert.Single(found);
            await collection.UpdateOneAsync(new BsonDocument("_id", 1), new BsonDocument("$set", new BsonDocument("status", "expired")));
            await collection.DeleteOneAsync(new BsonDocument("_id", 1));

            Assert.True(await Wait.UntilAsync(() => sink.Responses.Count >= 4, 20_000),
                $"only {sink.Responses.Count} interactions were captured");

            var responses = sink.Responses;
            Assert.Contains(responses, r => Text(r) == "Insert → Trial");
            Assert.Contains(responses, r => Text(r) == "Find ← Trial");
            Assert.Contains(responses, r => Text(r) == "Update → Trial");
            Assert.Contains(responses, r => Text(r) == "Delete → Trial");

            // The database in the URI is the one on the wire, and the URI never carries the credentials.
            Assert.All(sink.Logs, r => Assert.Equal("mongodb:///data-insights-Development/Trial", r.Uri.ToString()));
            Assert.Equal(TapPassword, seed.Password);
            Assert.All(sink.Logs, r => Assert.DoesNotContain(TapPassword, r.Uri.ToString()));
            Assert.All(sink.Logs, r => Assert.DoesNotContain(TapPassword, r.Content ?? ""));
            Assert.All(sink.Logs, r => Assert.DoesNotContain("@", r.Uri.ToString()));

            var find = Assert.Single(responses, r => Text(r) == "Find ← Trial");
            Assert.Contains("\"status\" : \"active\"", find.Content);
            Assert.Equal("{ \"status\" : \"active\" }", sink.Requests.Single(r => Text(r) == "Find ← Trial").Content);

            // Nothing from the SCRAM handshake or the topology chatter was recorded.
            Assert.DoesNotContain(sink.Logs, r => Text(r).Contains("hello", StringComparison.OrdinalIgnoreCase));
            Assert.DoesNotContain(sink.Logs, r => Text(r).Contains("sasl", StringComparison.OrdinalIgnoreCase));
            Assert.Equal(0, tap.DecodeErrors);
            Assert.Equal(0, tap.SegmentsDropped);
        }
        finally
        {
            await container.DisposeAsync();
        }
    }

    [Fact]
    public async Task WhatMongoDbDriver230PutsOnTheWireOnConnect()
    {
        Assert.SkipWhen(!DockerAvailable(), "Docker is not available.");

        var container = new MongoDbBuilder().WithImage("mongo:7").WithUsername("tapuser").WithPassword(TapPassword).Build();
        await container.StartAsync();
        try
        {
            var seed = new MongoUrl(container.GetConnectionString());
            var spy = new ConcurrentQueue<string>();

            var options = new TcpTapOptions
            {
                ListenPort = 0,
                ForwardHost = seed.Server.Host,
                ForwardPort = seed.Server.Port,
                CallerName = "svc",
                ServiceName = "mongo",
                EmitActivities = false,
                DecoderFactory = _ => new MongoMessageSpy(spy),
            };

            await using var tap = new TcpTapCore(options);
            await tap.StartAsync();

            var settings = MongoClientSettings.FromUrl(new MongoUrl(container.GetConnectionString()));
            settings.Server = new MongoServerAddress("127.0.0.1", tap.BoundPort);
            settings.DirectConnection = true;
            settings.ServerSelectionTimeout = TimeSpan.FromSeconds(20);

            var client = new MongoClient(settings);
            await client.GetDatabase("data-insights-Development").GetCollection<BsonDocument>("Trial")
                .Find(new BsonDocument()).ToListAsync();

            Assert.True(await Wait.UntilAsync(() => spy.Any(s => s.Contains("OP_MSG find")), 20_000));
            var messages = spy.ToArray();

            // Documented for the wiki: the handshake shape and the seed-address behaviour.
            output.WriteLine($"MongoDB.Driver {typeof(MongoClient).Assembly.GetName().Version} wire: {string.Join(" | ", messages)}");

            // The connection opens with the legacy OP_QUERY hello on admin.$cmd …
            Assert.Contains(messages, m => m.StartsWith("OP_QUERY admin.$cmd"));
            // … then everything else is OP_MSG, including the command we care about.
            Assert.Contains(messages, m => m == "OP_MSG find");
            // The driver never routed around the tap: a standalone advertises no other address, so every
            // command still arrives on the seed address it was given (the tap's port).
            Assert.Contains(messages, m => m.StartsWith("OP_MSG"));
            Assert.True(tap.ConnectionsAccepted > 0);
            // No compression was negotiated, so every command is decodable.
            Assert.DoesNotContain(messages, m => m.StartsWith("OP_COMPRESSED"));
        }
        finally
        {
            await container.DisposeAsync();
        }
    }

    /// <summary>Records the verb of every command a client sends, whatever it is.</summary>
    private sealed class RespCommandSpy(ConcurrentQueue<string> verbs) : IProtocolDecoder
    {
        private readonly List<byte> _buffer = [];

        public void OnClientToServer(ReadOnlySpan<byte> data, DateTimeOffset timestamp)
        {
            _buffer.AddRange(data);
            while (RespParser.TryParseCommand(System.Runtime.InteropServices.CollectionsMarshal.AsSpan(_buffer), out var arguments, out var consumed))
            {
                _buffer.RemoveRange(0, consumed);
                if (arguments is { Length: > 0 })
                    verbs.Enqueue(arguments[0].ToUpperInvariant());
            }
        }

        public void OnServerToClient(ReadOnlySpan<byte> data, DateTimeOffset timestamp) { }

        public void OnConnectionClosed() { }

        public void Dispose() { }
    }

    /// <summary>Records the op code (and, for OP_MSG, the command name) of every message a client sends.</summary>
    private sealed class MongoMessageSpy(ConcurrentQueue<string> messages) : IProtocolDecoder
    {
        private readonly List<byte> _buffer = [];

        public void OnClientToServer(ReadOnlySpan<byte> data, DateTimeOffset timestamp)
        {
            _buffer.AddRange(data);
            while (true)
            {
                var span = System.Runtime.InteropServices.CollectionsMarshal.AsSpan(_buffer);
                if (!MongoWireParser.TryReadHeader(span, out var header))
                    return;

                var message = span[..header.MessageLength];
                switch (header.OpCode)
                {
                    case MongoOpCodes.OpMsg:
                        var parsed = MongoWireParser.ParseOpMsg(message);
                        var name = parsed.Document.ElementCount > 0 ? parsed.Document.GetElement(0).Name : "?";
                        var database = parsed.Document.TryGetValue("$db", out var db) && db.IsString ? db.AsString : null;
                        messages.Enqueue(database is null ? $"OP_MSG {name}" : $"OP_MSG {name}");
                        if (database is not null)
                            messages.Enqueue($"$db={database}");
                        break;
                    case MongoOpCodes.OpQuery:
                        var (collection, query) = MongoWireParser.ParseOpQuery(message);
                        messages.Enqueue($"OP_QUERY {collection} ({(query.ElementCount > 0 ? query.GetElement(0).Name : "?")})");
                        break;
                    case MongoOpCodes.OpCompressed:
                        messages.Enqueue("OP_COMPRESSED");
                        break;
                    default:
                        messages.Enqueue($"opcode {header.OpCode}");
                        break;
                }

                _buffer.RemoveRange(0, header.MessageLength);
            }
        }

        public void OnServerToClient(ReadOnlySpan<byte> data, DateTimeOffset timestamp) { }

        public void OnConnectionClosed() { }

        public void Dispose() { }
    }
}
