using System.Net;
using Kronikol.Extensions.MongoDB;
using Kronikol.Extensions.Redis;
using Kronikol.Extensions.TcpTap;
using Kronikol.Tracking;
using MongoDB.Bson;
using MongoDB.Driver;
using MongoDB.Driver.Core.Clusters;
using MongoDB.Driver.Core.Connections;
using MongoDB.Driver.Core.Events;
using MongoDB.Driver.Core.Servers;

namespace Kronikol.Tests.TcpTap;

/// <summary>
/// The point of the wire taps: a call captured off the socket must render exactly like the same call captured
/// in-process. These tests drive the in-process extension and the tap decoder with the same command and assert
/// the arrow labels and URIs are identical, character for character.
/// </summary>
public class InProcessParityTests
{
    private static string Text(OneOf<HttpMethod, string> method) => method.Value?.ToString() ?? "";

    // ---- Redis ------------------------------------------------------------------------------------

    /// <summary>Runs the in-process Redis tracker and returns its (request label, response label, uri).</summary>
    private static (string RequestMethod, string ResponseMethod, string Uri) InProcessRedis(
        string command, string? key, int database, bool hasResult, RedisTrackingVerbosity verbosity, string endpoint)
    {
        var testId = Guid.NewGuid().ToString();
        var tracker = new RedisTracker(
            new RedisTrackingDatabaseOptions
            {
                ServiceName = "redis",
                CallerName = "svc",
                Verbosity = verbosity,
                CurrentTestInfoFetcher = () => ("Parity", testId),
            },
            endpoint);

        var (requestId, traceId) = tracker.LogRedisRequest(command, key, database, null);
        tracker.LogRedisResponse(command, key, database, hasResult, requestId, traceId, null);

        var logs = RequestResponseLogger.RequestAndResponseLogs.Where(l => l.TestId == testId).ToArray();
        Assert.Equal(2, logs.Length);
        return (Text(logs[0].Method), Text(logs[1].Method), logs[0].Uri.ToString());
    }

    [Theory]
    // command,           key,          reply wire,                       hasResult
    [InlineData("GET", "user:42", "$5\r\nvalue\r\n", true)]
    [InlineData("GET", "user:42", "$-1\r\n", false)]
    [InlineData("GETDEL", "k", "$1\r\nv\r\n", true)]
    [InlineData("SET", "k", "+OK\r\n", true)]
    [InlineData("SETNX", "k", ":1\r\n", true)]
    [InlineData("DEL", "k", ":1\r\n", true)]
    [InlineData("EXISTS", "k", ":1\r\n", true)]
    [InlineData("EXPIRE", "k", ":1\r\n", true)]
    [InlineData("INCR", "k", ":2\r\n", true)]
    [InlineData("DECR", "k", ":0\r\n", true)]
    [InlineData("HGET", "h", "$1\r\nv\r\n", true)]
    [InlineData("HGET", "h", "$-1\r\n", false)]
    [InlineData("HGETALL", "h", "*2\r\n$1\r\nf\r\n$1\r\nv\r\n", true)]
    [InlineData("HSET", "h", ":1\r\n", true)]
    [InlineData("HDEL", "h", ":1\r\n", true)]
    [InlineData("LPUSH", "l", ":1\r\n", true)]
    [InlineData("LRANGE", "l", "*1\r\n$1\r\na\r\n", true)]
    [InlineData("SADD", "s", ":1\r\n", true)]
    [InlineData("SMEMBERS", "s", "*1\r\n$1\r\na\r\n", true)]
    [InlineData("PUBLISH", "chan", ":1\r\n", true)]
    [InlineData("XADD", "stream", "$3\r\n1-1\r\n", true)]
    public void RedisLabelsAndUrisAreIdenticalToTheInProcessExtension(string command, string key, string reply, bool hasResult)
    {
        var expected = InProcessRedis(command, key, 0, hasResult, RedisTrackingVerbosity.Detailed, "localhost:6379");

        using var harness = DecoderHarness.Redis(o => o.ExcludedCommands.Clear());
        harness.ClientToServer(Resp.Command(command, key));
        harness.ServerToClient(reply);

        var request = Assert.Single(harness.Sink.Requests);
        var response = Assert.Single(harness.Sink.Responses);

        Assert.Equal(expected.RequestMethod, Text(request.Method));
        Assert.Equal(expected.ResponseMethod, Text(response.Method));
        Assert.Equal(expected.Uri, request.Uri.ToString());
        Assert.Equal(expected.Uri, response.Uri.ToString());
    }

    [Theory]
    [InlineData(TapVerbosity.Detailed, RedisTrackingVerbosity.Detailed)]
    [InlineData(TapVerbosity.Summarised, RedisTrackingVerbosity.Summarised)]
    [InlineData(TapVerbosity.Raw, RedisTrackingVerbosity.Raw)]
    public void RedisVerbosityLevelsLineUpOneForOne(TapVerbosity tapVerbosity, RedisTrackingVerbosity extensionVerbosity)
    {
        var expected = InProcessRedis("GET", "k", 3, hasResult: true, extensionVerbosity, "cache.internal:6380");

        using var harness = DecoderHarness.Redis(o =>
        {
            o.Verbosity = tapVerbosity;
            o.DefaultDatabase = 3;
            o.ForwardHost = "cache.internal";
            o.ForwardPort = 6380;
        });
        harness.ClientToServer(Resp.Command("GET", "k"));
        harness.ServerToClient(Resp.Bulk("v"));

        var request = Assert.Single(harness.Sink.Requests);
        var response = Assert.Single(harness.Sink.Responses);

        Assert.Equal(expected.RequestMethod, Text(request.Method));
        Assert.Equal(expected.ResponseMethod, Text(response.Method));
        Assert.Equal(expected.Uri, request.Uri.ToString());
    }

    [Fact]
    public void AnOversizeRedisValueProducesTheSameLabelsAsInProcessAndANoteThatDiffersOnlyByTheMarker()
    {
        // In-process, a 1 MiB GET reply is simply a hit; on the wire the payload is streamed past — the labels and
        // URI must still be identical, and the note is the value's preview plus the truncation marker.
        var expected = InProcessRedis("GET", "cache:big", 0, hasResult: true, RedisTrackingVerbosity.Detailed, "localhost:6379");

        const int size = 1024 * 1024;
        using var harness = DecoderHarness.Redis();
        harness.ClientToServer(Resp.Command("GET", "cache:big"));
        harness.ServerToClient(System.Text.Encoding.UTF8.GetBytes($"${size}\r\n").Concat(Enumerable.Repeat((byte)'p', size)).Concat("\r\n"u8.ToArray()).ToArray());

        var request = Assert.Single(harness.Sink.Requests);
        var response = Assert.Single(harness.Sink.Responses);
        Assert.Equal(expected.RequestMethod, Text(request.Method));
        Assert.Equal(expected.ResponseMethod, Text(response.Method));
        Assert.Equal(expected.Uri, request.Uri.ToString());
        Assert.Equal(new string('p', 65408) + " …[bulk string truncated: 1,048,576 bytes on the wire, 65,408 kept]", response.Content);
    }

    [Fact]
    public void RedisMultiKeyUrisMatchTheInProcessRedisKeyArrayJoin()
    {
        // The in-process extension joins a RedisKey[] with commas; the wire tap must produce the same URI for
        // the MGET those keys turn into.
        var expected = InProcessRedis("MGET", "a,b,c", 0, hasResult: true, RedisTrackingVerbosity.Detailed, "localhost:6379");

        using var harness = DecoderHarness.Redis();
        harness.ClientToServer(Resp.Command("MGET", "a", "b", "c"));
        harness.ServerToClient(Resp.Array(Resp.Bulk("1"), Resp.Bulk("2"), Resp.Bulk("3")));

        Assert.Equal(expected.Uri, Assert.Single(harness.Sink.Requests).Uri.ToString());
        Assert.Equal(expected.ResponseMethod, Text(Assert.Single(harness.Sink.Responses).Method));
    }

    [Fact]
    public void TheRedisStatusCodeIsTheSameLiteralTheInProcessExtensionUses()
    {
        var testId = Guid.NewGuid().ToString();
        var tracker = new RedisTracker(new RedisTrackingDatabaseOptions
        {
            ServiceName = "redis",
            CallerName = "svc",
            CurrentTestInfoFetcher = () => ("Parity", testId),
        });
        var (requestId, traceId) = tracker.LogRedisRequest("GET", "k", 0, null);
        tracker.LogRedisResponse("GET", "k", 0, true, requestId, traceId, "v");
        var expected = RequestResponseLogger.RequestAndResponseLogs
            .Single(l => l.TestId == testId && l.Type == RequestResponseType.Response).StatusCode!.Value;

        using var harness = DecoderHarness.Redis();
        harness.ClientToServer(Resp.Command("GET", "k"));
        harness.ServerToClient(Resp.Bulk("v"));

        Assert.Equal(expected, Assert.Single(harness.Sink.Responses).StatusCode!.Value);
    }

    // ---- MongoDB ----------------------------------------------------------------------------------

    private static ConnectionId MakeConnectionId() => new(new ServerId(new ClusterId(), new DnsEndPoint("localhost", 27017)));

    /// <summary>Runs the in-process Mongo subscriber and returns its (label, uri, request note, reply note).</summary>
    private static (string Method, string Uri, string? RequestContent, string? ResponseContent) InProcessMongo(
        string commandName, BsonDocument command, BsonDocument reply, string database, MongoDbTrackingVerbosity verbosity)
    {
        var testId = Guid.NewGuid().ToString();
        var subscriber = new MongoDbTrackingSubscriber(new MongoDbTrackingOptions
        {
            ServiceName = "mongo",
            CallerName = "svc",
            Verbosity = verbosity,
            CurrentTestInfoFetcher = () => ("Parity", testId),
        });

        subscriber.OnCommandStarted(new CommandStartedEvent(
            commandName, command, new DatabaseNamespace(database), 1L, 1, MakeConnectionId()));
        subscriber.OnCommandSucceeded(new CommandSucceededEvent(
            commandName, reply, new DatabaseNamespace(database), 1L, 1, MakeConnectionId(), TimeSpan.FromMilliseconds(3)));

        var logs = RequestResponseLogger.RequestAndResponseLogs.Where(l => l.TestId == testId).ToArray();
        Assert.Equal(2, logs.Length);
        return (Text(logs[0].Method), logs[0].Uri.ToString(), logs[0].Content, logs[1].Content);
    }

    public static TheoryData<string, string> MongoCommands() => new()
    {
        { "find", "Trial" },
        { "insert", "Trial" },
        { "update", "Trial" },
        { "delete", "Trial" },
        { "count", "Trial" },
        { "distinct", "Trial" },
        { "findAndModify", "Trial" },
        { "createIndexes", "Trial" },
        { "listCollections", "Trial" },
    };

    [Theory]
    [MemberData(nameof(MongoCommands))]
    public void MongoLabelsAndUrisAreIdenticalToTheInProcessExtension(string commandName, string collection)
    {
        var command = new BsonDocument
        {
            { commandName, collection },
            { "filter", new BsonDocument("status", "active") },
        };
        var reply = new BsonDocument { { "n", 1 }, { "ok", 1.0 } };

        var expected = InProcessMongo(commandName, command, reply, "app", MongoDbTrackingVerbosity.Detailed);

        var wireCommand = command.DeepClone().AsBsonDocument;
        wireCommand["$db"] = "app";

        using var harness = DecoderHarness.Mongo();
        harness.ClientToServer(MongoWire.Msg(1, 0, wireCommand));
        harness.ServerToClient(MongoWire.Msg(2, 1, reply));

        var request = Assert.Single(harness.Sink.Requests);
        var response = Assert.Single(harness.Sink.Responses);

        Assert.Equal(expected.Method, Text(request.Method));
        Assert.Equal(expected.Method, Text(response.Method));
        Assert.Equal(expected.Uri, request.Uri.ToString());
        Assert.Equal(expected.RequestContent, request.Content);
        Assert.Equal(expected.ResponseContent, response.Content);
    }

    [Fact]
    public void MongoInsertCountsAndReplyNotesMatch()
    {
        var documents = new BsonArray { new BsonDocument("_id", 1), new BsonDocument("_id", 2) };
        var command = new BsonDocument { { "insert", "Trial" }, { "documents", documents } };
        var reply = new BsonDocument { { "n", 2 }, { "ok", 1.0 } };

        var expected = InProcessMongo("insert", command, reply, "app", MongoDbTrackingVerbosity.Detailed);

        // On the wire the driver puts the documents in a kind-1 section, not inline.
        var body = new BsonDocument { { "insert", "Trial" }, { "$db", "app" } };
        using var harness = DecoderHarness.Mongo();
        harness.ClientToServer(MongoWire.Msg(1, 0, body, 0,
            ("documents", [new BsonDocument("_id", 1), new BsonDocument("_id", 2)])));
        harness.ServerToClient(MongoWire.Msg(2, 1, reply));

        Assert.Equal(expected.Method, Text(Assert.Single(harness.Sink.Requests).Method));
        Assert.Equal(expected.ResponseContent, Assert.Single(harness.Sink.Responses).Content);
    }

    [Fact]
    public void MongoCursorBatchNotesMatchCharacterForCharacter()
    {
        var command = new BsonDocument { { "find", "Trial" }, { "filter", new BsonDocument("a", 1) } };
        var reply = new BsonDocument
        {
            {
                "cursor", new BsonDocument
                {
                    { "firstBatch", new BsonArray { new BsonDocument { { "_id", 1 }, { "name", "one" } }, new BsonDocument { { "_id", 2 }, { "name", "two" } } } },
                    { "id", 0L },
                    { "ns", "app.Trial" },
                }
            },
            { "ok", 1.0 },
        };

        var expected = InProcessMongo("find", command, reply, "app", MongoDbTrackingVerbosity.Detailed);

        var wireCommand = command.DeepClone().AsBsonDocument;
        wireCommand["$db"] = "app";

        using var harness = DecoderHarness.Mongo();
        harness.ClientToServer(MongoWire.Msg(1, 0, wireCommand));
        harness.ServerToClient(MongoWire.Msg(2, 1, reply));

        Assert.Equal(expected.ResponseContent, Assert.Single(harness.Sink.Responses).Content);
    }

    [Theory]
    [InlineData(TapVerbosity.Detailed, MongoDbTrackingVerbosity.Detailed)]
    [InlineData(TapVerbosity.Summarised, MongoDbTrackingVerbosity.Summarised)]
    [InlineData(TapVerbosity.Raw, MongoDbTrackingVerbosity.Raw)]
    public void MongoVerbosityLevelsLineUpOneForOne(TapVerbosity tapVerbosity, MongoDbTrackingVerbosity extensionVerbosity)
    {
        var command = new BsonDocument { { "find", "Trial" }, { "filter", new BsonDocument("a", 1) }, { "$db", "app" } };
        var reply = new BsonDocument { { "n", 1 }, { "ok", 1.0 } };

        var expected = InProcessMongo("find", command, reply, "app", extensionVerbosity);

        using var harness = DecoderHarness.Mongo(o => o.Verbosity = tapVerbosity);
        harness.ClientToServer(MongoWire.Msg(1, 0, command));
        harness.ServerToClient(MongoWire.Msg(2, 1, reply));

        var request = Assert.Single(harness.Sink.Requests);
        Assert.Equal(expected.Method, Text(request.Method));
        Assert.Equal(expected.Uri, request.Uri.ToString());
        Assert.Equal(expected.RequestContent, request.Content);
    }

    [Fact]
    public void AMongoFailureIsA500InBothCaptures()
    {
        var testId = Guid.NewGuid().ToString();
        var subscriber = new MongoDbTrackingSubscriber(new MongoDbTrackingOptions
        {
            ServiceName = "mongo",
            CallerName = "svc",
            CurrentTestInfoFetcher = () => ("Parity", testId),
        });
        subscriber.OnCommandStarted(new CommandStartedEvent(
            "find", new BsonDocument("find", "Trial"), new DatabaseNamespace("app"), 1L, 1, MakeConnectionId()));
        subscriber.OnCommandFailed(new CommandFailedEvent(
            "find", new DatabaseNamespace("app"), new Exception("not authorized on app"), 1L, 1, MakeConnectionId(), TimeSpan.Zero));

        var expected = RequestResponseLogger.RequestAndResponseLogs
            .Single(l => l.TestId == testId && l.Type == RequestResponseType.Response);

        using var harness = DecoderHarness.Mongo();
        harness.ClientToServer(MongoWire.Msg(1, 0, new BsonDocument { { "find", "Trial" }, { "$db", "app" } }));
        harness.ServerToClient(MongoWire.Msg(2, 1, new BsonDocument { { "ok", 0.0 }, { "errmsg", "not authorized on app" } }));

        var actual = Assert.Single(harness.Sink.Responses);
        Assert.Equal(expected.StatusCode!.Value, actual.StatusCode!.Value);
        Assert.Equal(Text(expected.Method), Text(actual.Method));
        Assert.Equal(expected.Content, actual.Content);
    }
}
