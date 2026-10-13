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

    /// <summary>
    /// The old parity theory gave every command a top-level filter, a shape no write carries, and so compared no
    /// write's notes; these are the driver's real commands and replies.
    /// </summary>
    private static IReadOnlyList<WireShape> Shapes => MongoWireShapes.All;

    public static TheoryData<string, int, TapVerbosity> EveryShapeAtEveryVerbosity()
    {
        var data = new TheoryData<string, int, TapVerbosity>();
        for (var i = 0; i < Shapes.Count; i++)
            foreach (var verbosity in new[] { TapVerbosity.Detailed, TapVerbosity.Summarised, TapVerbosity.Raw })
                data.Add($"{Shapes[i].Section}: {Shapes[i].CommandName}", i, verbosity);
        return data;
    }

    /// <summary>Runs the in-process subscriber over a shape and returns its request and response.</summary>
    private static (RequestResponseLog Request, RequestResponseLog Response) InProcessMongo(WireShape shape, MongoDbTrackingVerbosity verbosity)
    {
        var testId = Guid.NewGuid().ToString();
        var subscriber = new MongoDbTrackingSubscriber(new MongoDbTrackingOptions
        {
            ServiceName = "mongo",
            CallerName = "svc",
            Verbosity = verbosity,
            TrackGetMore = true,
            AttributeByDocumentOwner = false,
            AutoCorrelateWrites = false,
            CurrentTestInfoFetcher = () => ("Parity", testId),
        });

        var database = new DatabaseNamespace(shape.Database);
        subscriber.OnCommandStarted(new CommandStartedEvent(shape.CommandName, shape.Command, database, 1L, 1, MakeConnectionId()));
        if (shape.Failed)
        {
            // What the driver raises on a failed command: its message names the command; the server's words are in Result.
            var failure = new MongoCommandException(MakeConnectionId(), $"{shape.CommandName} command failed", shape.Command, shape.Reply);
            subscriber.OnCommandFailed(new CommandFailedEvent(shape.CommandName, database, failure, 1L, 1, MakeConnectionId(), TimeSpan.FromMilliseconds(1)));
        }
        else
        {
            subscriber.OnCommandSucceeded(new CommandSucceededEvent(shape.CommandName, shape.Reply, database, 1L, 1, MakeConnectionId(), TimeSpan.FromMilliseconds(1)));
        }

        var logs = RequestResponseLogger.RequestAndResponseLogs.Where(l => l.TestId == testId).ToArray();
        Assert.Equal(2, logs.Length);
        return (logs[0], logs[1]);
    }

    [Theory]
    [MemberData(nameof(EveryShapeAtEveryVerbosity))]
    public void EveryRealCommandAndReplyRendersAlikeInBothCaptures(string shape, int index, TapVerbosity verbosity)
    {
        _ = shape;
        var extensionVerbosity = verbosity switch
        {
            TapVerbosity.Summarised => MongoDbTrackingVerbosity.Summarised,
            TapVerbosity.Raw => MongoDbTrackingVerbosity.Raw,
            _ => MongoDbTrackingVerbosity.Detailed,
        };
        var expected = InProcessMongo(Shapes[index], extensionVerbosity);

        var actual = MongoWireShapes.Tap(Shapes[index], verbosity);

        Assert.Equal(Text(expected.Request.Method), Text(actual.Request.Method));
        Assert.Equal(Text(expected.Response.Method), Text(actual.Response.Method));
        Assert.Equal(expected.Request.Uri.ToString(), actual.Request.Uri.ToString());
        Assert.Equal(expected.Request.Content, actual.Request.Content);
        Assert.Equal(expected.Response.Content, actual.Response.Content);
        Assert.Equal(expected.Response.StatusCode!.Value, actual.Response.StatusCode!.Value);
        Assert.Equal(expected.Response.Error, actual.Response.Error);
    }

    [Fact]
    public void TheShapesHoldEveryKindOfReplyTheRulesTellApart()
    {
        // The theory above proves only what its data holds: each kind of reply the note and failure rules treat apart.
        Assert.Contains(Shapes, s => s.Failed);
        Assert.Contains(Shapes, s => s.Reply.Contains("writeErrors"));
        Assert.Contains(Shapes, s => s.Reply.Contains("upserted"));
        Assert.Contains(Shapes, s => s.Reply.Contains("lastErrorObject") && s.Reply["value"].IsBsonNull);
        Assert.Contains(Shapes, s => s.Reply.Contains("values"));
        Assert.Contains(Shapes, s => s.CommandName == "getMore");
        Assert.Contains(Shapes, s => s.CommandName == "bulkWrite" && s.Reply.Contains("cursor") && s.Reply.Contains("nInserted"));
        // No captured reply has an empty batch: each capture has a fact of its own for one (An_empty_batch_is_an_empty_array).
        Assert.Contains(Shapes, s => HoldsAGuid(s.Reply));
        Assert.Contains(Shapes, s => s.Command.Contains("documents") && s.Command["documents"].AsBsonArray.Count > 1);
        Assert.Contains(Shapes, s => s.CommandName == "commitTransaction");
    }

    private static bool HoldsAGuid(BsonValue value) => value switch
    {
        BsonBinaryData binary => binary.SubType == BsonBinarySubType.UuidStandard,
        BsonDocument document => document.Values.Any(HoldsAGuid),
        BsonArray array => array.Any(HoldsAGuid),
        _ => false,
    };

    [Fact]
    public void AMongoFailureIsA500WithTheServersWordsInBothCaptures()
    {
        var failed = Shapes.First(s => s.Failed);

        var expected = InProcessMongo(failed, MongoDbTrackingVerbosity.Detailed);
        var actual = MongoWireShapes.Tap(failed, TapVerbosity.Detailed);

        Assert.Equal(HttpStatusCode.InternalServerError, actual.Response.StatusCode!.Value);
        Assert.Equal(expected.Response.StatusCode!.Value, actual.Response.StatusCode!.Value);
        Assert.Equal("{ \"errmsg\" : \"unknown operator: $bogus\", \"code\" : 2, \"codeName\" : \"BadValue\" }", expected.Response.Content);
        Assert.Equal(expected.Response.Content, actual.Response.Content);
        Assert.Equal("unknown operator: $bogus", expected.Response.Error);
        Assert.Equal(expected.Response.Error, actual.Response.Error);
    }
}
