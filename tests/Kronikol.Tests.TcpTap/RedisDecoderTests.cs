using System.Net;
using Kronikol.Constants;
using Kronikol.Extensions.TcpTap;
using Kronikol.Extensions.TcpTap.Protocols;
using Kronikol.Tracking;

namespace Kronikol.Tests.TcpTap;

public class RedisDecoderTests
{
    private static string Method(RequestResponseLog log) => log.Method.Value?.ToString() ?? "";

    private static string Status(RequestResponseLog log) => log.StatusCode?.Value?.ToString() ?? "";

    [Fact]
    public void AGetThatFindsTheKeyIsAHit()
    {
        using var harness = DecoderHarness.Redis();
        harness.ClientToServer(Resp.Command("GET", "user:42"));
        harness.ServerToClient(Resp.Bulk("{\"name\":\"Ada\"}"));

        var request = Assert.Single(harness.Sink.Requests);
        var response = Assert.Single(harness.Sink.Responses);

        Assert.Equal("Get", Method(request));
        Assert.Equal("Get (Hit)", Method(response));
        Assert.Equal("redis://db0/user:42", request.Uri.ToString());
        Assert.Equal("redis://db0/user:42", response.Uri.ToString());
        Assert.Null(request.Content);
        Assert.Equal("{\"name\":\"Ada\"}", response.Content);
        Assert.Equal("OK", Status(response));
        Assert.Equal(DependencyCategories.Redis, request.DependencyCategory);
        Assert.Equal("redis", request.ServiceName);
        Assert.Equal("svc", request.CallerName);
    }

    [Fact]
    public void AGetThatFindsNothingIsAMiss()
    {
        using var harness = DecoderHarness.Redis();
        harness.ClientToServer(Resp.Command("GET", "user:42"));
        harness.ServerToClient(Resp.Bulk(null));

        Assert.Equal("Get (Miss)", Method(Assert.Single(harness.Sink.Responses)));
        Assert.Null(Assert.Single(harness.Sink.Responses).Content);
    }

    [Fact]
    public void ASetRecordsTheValueOnTheRequest()
    {
        using var harness = DecoderHarness.Redis();
        harness.ClientToServer(Resp.Command("SET", "k", "v"));
        harness.ServerToClient(Resp.Simple("OK"));

        var request = Assert.Single(harness.Sink.Requests);
        Assert.Equal("Set", Method(request));
        Assert.Equal("v", request.Content);
        Assert.Equal("Set", Method(Assert.Single(harness.Sink.Responses)));
    }

    [Fact]
    public void SetexTakesTheValueAfterTheExpiry()
    {
        using var harness = DecoderHarness.Redis();
        harness.ClientToServer(Resp.Command("SETEX", "k", "60", "v"));
        harness.ServerToClient(Resp.Simple("OK"));

        Assert.Equal("v", Assert.Single(harness.Sink.Requests).Content);
    }

    [Fact]
    public void AHashSetRecordsFieldEqualsValue()
    {
        using var harness = DecoderHarness.Redis();
        harness.ClientToServer(Resp.Command("HSET", "h", "field", "value"));
        harness.ServerToClient(Resp.Integer(1));

        var request = Assert.Single(harness.Sink.Requests);
        Assert.Equal("HashSet", Method(request));
        Assert.Equal("field=value", request.Content);
    }

    [Fact]
    public void HashGetAllHasNoHitOrMiss()
    {
        using var harness = DecoderHarness.Redis();
        harness.ClientToServer(Resp.Command("HGETALL", "h"));
        harness.ServerToClient(Resp.Array(Resp.Bulk("f"), Resp.Bulk("v")));

        Assert.Equal("HashGetAll", Method(Assert.Single(harness.Sink.Responses)));
    }

    [Fact]
    public void MultiKeyCommandsJoinEveryKeyIntoTheUriJustLikeTheInProcessExtension()
    {
        using var harness = DecoderHarness.Redis();
        harness.ClientToServer(Resp.Command("MGET", "a", "b", "c"));
        harness.ServerToClient(Resp.Array(Resp.Bulk("1"), Resp.Bulk(null), Resp.Bulk("3")));

        var response = Assert.Single(harness.Sink.Responses);
        Assert.Equal("redis://db0/a,b,c", response.Uri.ToString());
        Assert.Equal("Get (Hit)", Method(response));
    }

    [Fact]
    public void ADeleteOfSeveralKeysJoinsThemToo()
    {
        using var harness = DecoderHarness.Redis();
        harness.ClientToServer(Resp.Command("DEL", "a", "b"));
        harness.ServerToClient(Resp.Integer(2));

        var response = Assert.Single(harness.Sink.Responses);
        Assert.Equal("Delete", Method(response));
        Assert.Equal("redis://db0/a,b", response.Uri.ToString());
    }

    [Fact]
    public void SelectMovesTheDatabaseIndexForEverythingAfterIt()
    {
        using var harness = DecoderHarness.Redis();
        harness.ClientToServer(Resp.Command("SELECT", "3"));
        harness.ServerToClient(Resp.Simple("OK"));
        harness.ClientToServer(Resp.Command("GET", "k"));
        harness.ServerToClient(Resp.Bulk("v"));

        // SELECT itself is handshake chatter and is never recorded.
        var response = Assert.Single(harness.Sink.Responses);
        Assert.Equal("redis://db3/k", response.Uri.ToString());
    }

    [Fact]
    public void AFailedSelectLeavesTheDatabaseIndexAlone()
    {
        using var harness = DecoderHarness.Redis();
        harness.ClientToServer(Resp.Command("SELECT", "9"));
        harness.ServerToClient(Resp.Error("ERR DB index is out of range"));
        harness.ClientToServer(Resp.Command("GET", "k"));
        harness.ServerToClient(Resp.Bulk("v"));

        Assert.Equal("redis://db0/k", Assert.Single(harness.Sink.Responses).Uri.ToString());
    }

    [Fact]
    public void TheDefaultDatabaseOptionSetsTheStartingIndex()
    {
        using var harness = DecoderHarness.Redis(o => o.DefaultDatabase = 2);
        harness.ClientToServer(Resp.Command("GET", "k"));
        harness.ServerToClient(Resp.Bulk("v"));

        Assert.Equal("redis://db2/k", Assert.Single(harness.Sink.Responses).Uri.ToString());
    }

    [Fact]
    public void AnErrorReplyBecomesA500WithTheMessage()
    {
        using var harness = DecoderHarness.Redis();
        harness.ClientToServer(Resp.Command("GET", "k"));
        harness.ServerToClient(Resp.Error("WRONGTYPE Operation against a key holding the wrong kind of value"));

        var response = Assert.Single(harness.Sink.Responses);
        Assert.Equal(HttpStatusCode.InternalServerError, response.StatusCode!.Value);
        Assert.StartsWith("WRONGTYPE", response.Content);
        Assert.Equal("Get (Miss)", Method(response));
    }

    // ---- handshake and security ------------------------------------------------------------------

    [Theory]
    [InlineData("PING")]
    [InlineData("INFO")]
    [InlineData("COMMAND")]
    [InlineData("CLUSTER")]
    public void HandshakeChatterIsNeverRecorded(string verb)
    {
        using var harness = DecoderHarness.Redis();
        harness.ClientToServer(Resp.Command(verb));
        harness.ServerToClient(Resp.Simple("PONG"));

        Assert.Empty(harness.Sink.Logs);
    }

    [Fact]
    public void AuthIsNeverRecordedEvenWhenTheExclusionListIsEmptied()
    {
        using var harness = DecoderHarness.Redis(o => o.ExcludedCommands.Clear());
        harness.ClientToServer(Resp.Command("AUTH", "default", "hunter2"));
        harness.ServerToClient(Resp.Simple("OK"));

        Assert.Empty(harness.Sink.Logs);
        Assert.DoesNotContain(harness.Sink.Logs, l => (l.Content ?? "").Contains("hunter2"));
    }

    [Fact]
    public void HelloIsNeverRecordedEvenWhenTheExclusionListIsEmptied()
    {
        using var harness = DecoderHarness.Redis(o => o.ExcludedCommands.Clear());
        harness.ClientToServer(Resp.Command("HELLO", "3", "AUTH", "default", "hunter2"));
        harness.ServerToClient("%1\r\n$6\r\nserver\r\n$5\r\nredis\r\n");

        Assert.Empty(harness.Sink.Logs);
    }

    [Fact]
    public void AnExcludedCommandStillConsumesItsReplySoTheFifoStaysAligned()
    {
        using var harness = DecoderHarness.Redis();

        // The StackExchange.Redis 2.6 connect sequence, then one real command, pipelined.
        harness.ClientToServer(
            Resp.Command("ECHO", "probe") +
            Resp.Command("CLIENT", "SETNAME", "app") +
            Resp.Command("CONFIG", "GET", "timeout") +
            Resp.Command("GET", "real:key"));
        harness.ServerToClient(
            Resp.Bulk("probe") +
            Resp.Simple("OK") +
            Resp.Array(Resp.Bulk("timeout"), Resp.Bulk("0")) +
            Resp.Bulk("real:value"));

        var response = Assert.Single(harness.Sink.Responses);
        Assert.Equal("Get (Hit)", Method(response));
        Assert.Equal("redis://db0/real:key", response.Uri.ToString());
        Assert.Equal("real:value", response.Content);
    }

    [Fact]
    public void ClientLibraryBookkeepingKeysAreNeverRecorded()
    {
        using var harness = DecoderHarness.Redis();
        // StackExchange.Redis probes its tie-breaker key on every connection it opens.
        harness.ClientToServer(Resp.Command("GET", "__Booksleeve_TieBreak"));
        harness.ServerToClient(Resp.Bulk(null));
        harness.ClientToServer(Resp.Command("GET", "app:key"));
        harness.ServerToClient(Resp.Bulk("v"));

        var response = Assert.Single(harness.Sink.Responses);
        Assert.Equal("redis://db0/app:key", response.Uri.ToString());
    }

    [Fact]
    public void TheKeyPrefixExclusionsCanBeReplaced()
    {
        using var harness = DecoderHarness.Redis(o =>
        {
            o.ExcludedKeyPrefixes.Clear();
            o.ExcludedKeyPrefixes.Add("internal:");
        });
        harness.ClientToServer(Resp.Command("GET", "__Booksleeve_TieBreak"));
        harness.ServerToClient(Resp.Bulk(null));
        harness.ClientToServer(Resp.Command("GET", "internal:thing"));
        harness.ServerToClient(Resp.Bulk("v"));

        Assert.Equal("redis://db0/__Booksleeve_TieBreak", Assert.Single(harness.Sink.Responses).Uri.ToString());
    }

    // ---- pipelining and segmentation --------------------------------------------------------------

    [Fact]
    public void PipelinedCommandsAreMatchedToTheirRepliesInOrder()
    {
        using var harness = DecoderHarness.Redis();
        harness.ClientToServer(Resp.Command("GET", "a") + Resp.Command("GET", "b") + Resp.Command("SET", "c", "3"));
        harness.ServerToClient(Resp.Bulk("1") + Resp.Bulk(null) + Resp.Simple("OK"));

        var responses = harness.Sink.Responses;
        Assert.Equal(3, responses.Count);
        Assert.Equal("Get (Hit)", Method(responses[0]));
        Assert.Equal("redis://db0/a", responses[0].Uri.ToString());
        Assert.Equal("Get (Miss)", Method(responses[1]));
        Assert.Equal("redis://db0/b", responses[1].Uri.ToString());
        Assert.Equal("Set", Method(responses[2]));
    }

    [Fact]
    public void MessagesSplitAcrossSegmentsAreReassembled()
    {
        using var harness = DecoderHarness.Redis();
        harness.ClientToServer("*3\r\n$3\r\nSE");
        harness.ClientToServer("T\r\n$1\r\nk\r\n$5\r\nva");
        harness.ClientToServer("lue\r\n");
        harness.ServerToClientByteByByte(Resp.Simple("OK"));

        var request = Assert.Single(harness.Sink.Requests);
        Assert.Equal("Set", Method(request));
        Assert.Equal("value", request.Content);
        Assert.Equal("redis://db0/k", request.Uri.ToString());
    }

    [Fact]
    public void TheTimestampsSpanTheCommandAndItsReply()
    {
        using var harness = DecoderHarness.Redis();
        var start = new DateTimeOffset(2026, 8, 22, 10, 0, 0, TimeSpan.Zero);
        harness.ClientToServer(Resp.Command("GET", "k"), start);
        harness.ServerToClient(Resp.Bulk("v"), start.AddMilliseconds(7));

        Assert.Equal(start, Assert.Single(harness.Sink.Requests).Timestamp);
        Assert.Equal(start.AddMilliseconds(7), Assert.Single(harness.Sink.Responses).Timestamp);
    }

    // ---- pub/sub ----------------------------------------------------------------------------------

    [Fact]
    public void ADeliveredPubSubMessageIsNotTreatedAsAReply()
    {
        using var harness = DecoderHarness.Redis();
        harness.ClientToServer(Resp.Command("GET", "k"));
        // A subscription connection can interleave a delivery before the answer arrives.
        harness.ServerToClient(Resp.Array(Resp.Bulk("message"), Resp.Bulk("channel"), Resp.Bulk("payload")));
        harness.ServerToClient(Resp.Bulk("v"));

        var response = Assert.Single(harness.Sink.Responses);
        Assert.Equal("Get (Hit)", Method(response));
        Assert.Equal("v", response.Content);
    }

    [Fact]
    public void AResp3PushIsNotTreatedAsAReply()
    {
        using var harness = DecoderHarness.Redis();
        harness.ClientToServer(Resp.Command("GET", "k"));
        harness.ServerToClient(">3\r\n$7\r\nmessage\r\n$2\r\nch\r\n$2\r\nhi\r\n");
        harness.ServerToClient(Resp.Bulk("v"));

        Assert.Equal("Get (Hit)", Method(Assert.Single(harness.Sink.Responses)));
    }

    [Fact]
    public void PublishIsRecordedWithItsMessage()
    {
        using var harness = DecoderHarness.Redis();
        harness.ClientToServer(Resp.Command("PUBLISH", "news", "hello"));
        harness.ServerToClient(Resp.Integer(2));

        var request = Assert.Single(harness.Sink.Requests);
        Assert.Equal("Publish", Method(request));
        Assert.Equal("hello", request.Content);
        Assert.Equal("redis://db0/news", request.Uri.ToString());
    }

    // ---- verbosity and redaction ------------------------------------------------------------------

    [Fact]
    public void RawVerbosityUsesTheCommandNameAndTheRealEndpoint()
    {
        using var harness = DecoderHarness.Redis(o =>
        {
            o.Verbosity = TapVerbosity.Raw;
            o.ForwardHost = "cache.internal";
            o.ForwardPort = 6380;
        });
        harness.ClientToServer(Resp.Command("GET", "k"));
        harness.ServerToClient(Resp.Bulk("v"));

        var response = Assert.Single(harness.Sink.Responses);
        Assert.Equal("GET", Method(response));
        Assert.Equal("redis://cache.internal:6380/0/k", response.Uri.ToString());
    }

    [Fact]
    public void SummarisedVerbosityDropsTheKeyAndTheContent()
    {
        using var harness = DecoderHarness.Redis(o => o.Verbosity = TapVerbosity.Summarised);
        harness.ClientToServer(Resp.Command("SET", "k", "secret"));
        harness.ServerToClient(Resp.Simple("OK"));

        var request = Assert.Single(harness.Sink.Requests);
        Assert.Equal("redis://db0/", request.Uri.ToString());
        Assert.Null(request.Content);
        Assert.Null(Assert.Single(harness.Sink.Responses).Content);
    }

    [Fact]
    public void SummarisedVerbosityDropsUnclassifiedCommandsEntirely()
    {
        using var harness = DecoderHarness.Redis(o => o.Verbosity = TapVerbosity.Summarised);
        harness.ClientToServer(Resp.Command("XADD", "stream", "*", "f", "v"));
        harness.ServerToClient(Resp.Bulk("1-1"));

        Assert.Empty(harness.Sink.Logs);
    }

    [Fact]
    public void KeyAndValueRedactionRunBeforeAnythingReachesTheSink()
    {
        using var harness = DecoderHarness.Redis(o =>
        {
            o.KeyRedaction = key => key.Replace("42", "[ID]");
            o.ValueRedaction = value => value.Contains("token") ? "[REDACTED]" : value;
        });
        harness.ClientToServer(Resp.Command("SET", "user:42", "token=abc"));
        harness.ServerToClient(Resp.Simple("OK"));

        var request = Assert.Single(harness.Sink.Requests);
        Assert.Equal("redis://db0/user:[ID]", request.Uri.ToString());
        Assert.Equal("[REDACTED]", request.Content);
    }

    [Fact]
    public void ContentIsCappedAtBodyCapBytes()
    {
        using var harness = DecoderHarness.Redis(o => o.BodyCapBytes = 16);
        harness.ClientToServer(Resp.Command("GET", "k"));
        harness.ServerToClient(Resp.Bulk(new string('x', 100)));

        var response = Assert.Single(harness.Sink.Responses);
        Assert.StartsWith(new string('x', 16), response.Content);
        Assert.Contains("truncated (100 chars total)", response.Content);
    }

    [Fact]
    public void CaptureRepliesFalseKeepsTheArrowButDropsTheReplyNote()
    {
        using var harness = DecoderHarness.Redis(o => o.CaptureReplies = false);
        harness.ClientToServer(Resp.Command("GET", "k"));
        harness.ServerToClient(Resp.Bulk("v"));

        Assert.Single(harness.Sink.Requests);
        var response = Assert.Single(harness.Sink.Responses);
        Assert.Null(response.Content);
        Assert.Equal("Get (Hit)", Method(response));
    }

    // ---- identity ---------------------------------------------------------------------------------

    [Fact]
    public void EverythingIsAttributedToTheFallbackIdentityByDefault()
    {
        using var harness = DecoderHarness.Redis(o =>
        {
            o.FallbackTestName = "Traffic outside any test";
            o.FallbackTestId = "session-bucket";
        });
        harness.ClientToServer(Resp.Command("GET", "k"));
        harness.ServerToClient(Resp.Bulk("v"));

        var request = Assert.Single(harness.Sink.Requests);
        Assert.Equal("Traffic outside any test", request.TestName);
        Assert.Equal("session-bucket", request.TestId);
    }

    [Fact]
    public void AnIdentityResolverCanAttributeToTheRequestInFlight()
    {
        (string Name, string Id)? current = ("Overview loads", "abc123");
        using var harness = DecoderHarness.Redis(o => o.IdentityResolver = () => current);
        harness.ClientToServer(Resp.Command("GET", "k"));
        harness.ServerToClient(Resp.Bulk("v"));

        current = null;
        harness.ClientToServer(Resp.Command("GET", "k2"));
        harness.ServerToClient(Resp.Bulk("v"));

        Assert.Equal("Overview loads", harness.Sink.Requests[0].TestName);
        Assert.Equal("abc123", harness.Sink.Requests[0].TestId);
        Assert.Equal(TestIdentityScope.UnknownTestName, harness.Sink.Requests[1].TestName);
    }

    [Fact]
    public void AThrowingIdentityResolverFallsBackInsteadOfBreakingTheDecoder()
    {
        using var harness = DecoderHarness.Redis(o => o.IdentityResolver = () => throw new InvalidOperationException("boom"));
        harness.ClientToServer(Resp.Command("GET", "k"));
        harness.ServerToClient(Resp.Bulk("v"));

        Assert.Equal(TestIdentityScope.UnknownTestId, Assert.Single(harness.Sink.Requests).TestId);
    }

    // ---- resilience -------------------------------------------------------------------------------

    [Fact]
    public void GarbageOnTheWireStopsDecodingRatherThanCorruptingTheStream()
    {
        using var harness = DecoderHarness.Redis();
        Assert.Throws<RespProtocolException>(() => harness.ServerToClient("this is not RESP\r\n"));
    }

    [Fact]
    public void AKeyThatCannotFormAUriStillProducesAnArrow()
    {
        using var harness = DecoderHarness.Redis();
        harness.ClientToServer(Resp.Command("GET", "a key with spaces"));
        harness.ServerToClient(Resp.Bulk("v"));

        var response = Assert.Single(harness.Sink.Responses);
        Assert.Equal("Get (Hit)", Method(response));
        Assert.StartsWith("redis://db0/", response.Uri.ToString());
    }

    [Fact]
    public void ARepliesFloodWithoutCommandsIsIgnored()
    {
        using var harness = DecoderHarness.Redis();
        harness.ServerToClient(Resp.Simple("OK") + Resp.Simple("OK"));
        Assert.Empty(harness.Sink.Logs);
    }
}
