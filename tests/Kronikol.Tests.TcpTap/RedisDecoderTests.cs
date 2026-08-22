using System.Net;
using System.Text;
using Kronikol.Constants;
using Kronikol.Extensions.TcpTap;
using Kronikol.Extensions.TcpTap.Protocols;
using Kronikol.Reports;
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
        // A payload over the cap is never buffered: the note is the preview plus the length on the wire, and the
        // record-time cap leaves that marker alone (the preview leaves room for it).
        using var harness = DecoderHarness.Redis(o => o.BodyCapBytes = 512);
        harness.ClientToServer(Resp.Command("GET", "k"));
        harness.ServerToClient(Resp.Bulk(new string('x', 1000)));

        var response = Assert.Single(harness.Sink.Responses);
        Assert.Equal("Get (Hit)", Method(response));
        Assert.Equal(new string('x', 384) + " …[bulk string truncated: 1,000 bytes on the wire, 384 kept]", response.Content);
        Assert.DoesNotContain("chars total", response.Content);
        Assert.True(response.Content!.Length <= 512);
    }

    [Fact]
    public void AValueBetweenTheRecordCapAndTheBulkCapIsTruncatedAtRecordTime()
    {
        using var harness = DecoderHarness.Redis(o =>
        {
            o.BodyCapBytes = 16;
            o.MaxBulkBytes = 1000;
        });
        harness.ClientToServer(Resp.Command("GET", "k"));
        harness.ServerToClient(Resp.Bulk(new string('x', 100)));

        var response = Assert.Single(harness.Sink.Responses);
        Assert.StartsWith(new string('x', 16), response.Content);
        Assert.Contains("truncated (100 chars total)", response.Content);
        Assert.Equal(0, harness.Tap.OversizePayloadsSkipped);
    }

    [Fact]
    public void MaxBulkBytesDefaultsToTheRecordCapThenToTheBufferCap()
    {
        Assert.Equal(65536, new RedisTapOptions().EffectiveMaxBulkBytes);
        Assert.Equal(1000, new RedisTapOptions { BodyCapBytes = 1000 }.EffectiveMaxBulkBytes);
        Assert.Equal(8 * 1024 * 1024, new RedisTapOptions { BodyCapBytes = null }.EffectiveMaxBulkBytes);
        Assert.Equal(4096, new RedisTapOptions { MaxBulkBytes = 4096 }.EffectiveMaxBulkBytes);
        Assert.Equal(1024, new RedisTapOptions { MaxBulkBytes = 4096, MaxBufferedBytes = 1024 }.EffectiveMaxBulkBytes);
        Assert.Throws<ArgumentOutOfRangeException>(() => new RedisTapOptions { CallerName = "a", MaxBulkBytes = -1 }.Validate());

        Assert.Equal(65408, RedisProtocolDecoder.PreviewBytes(65536, 65536));
        Assert.Equal(16, RedisProtocolDecoder.PreviewBytes(16, 16));
        Assert.Equal(1024, RedisProtocolDecoder.PreviewBytes(null, 1024));
        Assert.Equal(100, RedisProtocolDecoder.PreviewBytes(65536, 100));

        var source = new RedisTapOptions { MaxBulkBytes = 77, ResyncAfterOverflow = false, OnCaptureDegraded = _ => { }, DecodingStallBytes = 5 };
        var target = new RedisTapOptions();
        source.CopyTo(target);
        Assert.Equal(77, target.MaxBulkBytes);
        Assert.False(target.ResyncAfterOverflow);
        Assert.NotNull(target.OnCaptureDegraded);
        Assert.Equal(5, target.DecodingStallBytes);
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

    [Fact]
    public void AnInlineCommandIsDecodedLikeAnArrayCommand()
    {
        using var harness = DecoderHarness.Redis();
        harness.ClientToServer("GET k\r\n");
        harness.ServerToClient(Resp.Bulk("v"));

        var response = Assert.Single(harness.Sink.Responses);
        Assert.Equal("Get (Hit)", Method(response));
        Assert.Equal("redis://db0/k", response.Uri.ToString());
    }

    // ---- oversize payloads (values larger than the capture cap) -----------------------------------

    private static byte[] Bytes(string text) => Encoding.UTF8.GetBytes(text);

    private static byte[] Concat(params byte[][] parts)
    {
        var result = new byte[parts.Sum(p => p.Length)];
        var offset = 0;
        foreach (var part in parts)
        {
            part.CopyTo(result, offset);
            offset += part.Length;
        }

        return result;
    }

    /// <summary>A <c>SET key &lt;payload&gt;</c> command with a payload of <paramref name="size"/> bytes of <paramref name="fill"/>.</summary>
    private static byte[] BigSet(string key, int size, byte fill = (byte)'v')
    {
        var payload = new byte[size];
        Array.Fill(payload, fill);
        return Concat(Bytes($"*3\r\n$3\r\nSET\r\n${key.Length}\r\n{key}\r\n${size}\r\n"), payload, Bytes("\r\n"));
    }

    /// <summary>A bulk reply of <paramref name="size"/> bytes of <paramref name="fill"/>.</summary>
    private static byte[] BigBulk(int size, byte fill = (byte)'v')
    {
        var payload = new byte[size];
        Array.Fill(payload, fill);
        return Concat(Bytes($"${size}\r\n"), payload, Bytes("\r\n"));
    }

    [Fact]
    public void ATenMebibyteSetThenAGetOfItAreBothRecordedAndTheGetIsStillAHit()
    {
        const int size = 10 * 1024 * 1024;
        using var harness = DecoderHarness.Redis();

        harness.DeliverChunked(TapDirection.ClientToServer, BigSet("cache:big", size), 32 * 1024);
        harness.Deliver(TapDirection.ServerToClient, Resp.Simple("OK"));
        harness.Deliver(TapDirection.ClientToServer, Resp.Command("GET", "cache:big"));
        harness.DeliverChunked(TapDirection.ServerToClient, BigBulk(size), 32 * 1024);

        Assert.True(harness.Decoding);
        var requests = harness.Sink.Requests;
        var responses = harness.Sink.Responses;
        Assert.Equal(2, requests.Count);
        Assert.Equal(2, responses.Count);

        Assert.Equal("Set", Method(requests[0]));
        Assert.Equal("redis://db0/cache:big", requests[0].Uri.ToString());
        Assert.StartsWith(new string('v', 65408), requests[0].Content);
        Assert.EndsWith(" …[bulk string truncated: 10,485,760 bytes on the wire, 65,408 kept]", requests[0].Content);
        Assert.True(requests[0].Content!.Length <= 65536, "the record-time cap must not cut the marker off");

        Assert.Equal("Get", Method(requests[1]));
        Assert.Equal("Get (Hit)", Method(responses[1]));
        Assert.EndsWith(" …[bulk string truncated: 10,485,760 bytes on the wire, 65,408 kept]", responses[1].Content);
        Assert.DoesNotContain("chars total", responses[1].Content);

        Assert.Equal(2, harness.Tap.OversizePayloadsSkipped);
        Assert.Equal(2L * (size - 65408), harness.Tap.BytesSkipped);
        Assert.Equal(size, harness.Tap.LargestOversizePayload);
        Assert.Equal(0, harness.Tap.DecodingDisabledConnections);
        Assert.Equal(0, harness.Tap.DecoderResets);
        Assert.Equal(2, harness.Degradations.Count(d => d.Kind == CaptureDegradationKind.OversizePayloadSkipped));
        var diagnostic = Assert.Single(harness.Tap.Diagnostics());
        Assert.Contains("2 oversize payload(s) streamed past (largest 10,485,760 B", diagnostic.Message);
        Assert.Equal(DiagnosticKind.CaptureDegraded, diagnostic.Kind);
    }

    [Fact]
    public void TwoHundredPipelinedCommandsWithAnOversizeValueInTheMiddleAllPairInOrder()
    {
        using var harness = DecoderHarness.Redis(o => o.BodyCapBytes = 1024);
        var commands = new List<byte[]>();
        var replies = new List<byte[]>();
        for (var i = 0; i < 200; i++)
        {
            if (i == 100)
            {
                commands.Add(BigSet("k100", 300_000));
                replies.Add(Bytes(Resp.Simple("OK")));
            }
            else if (i == 150)
            {
                commands.Add(Bytes(Resp.Command("GET", "k150")));
                replies.Add(BigBulk(200_000, (byte)'r'));
            }
            else
            {
                commands.Add(Bytes(Resp.Command("GET", $"k{i}")));
                replies.Add(Bytes(Resp.Bulk($"v{i}")));
            }
        }

        harness.DeliverChunked(TapDirection.ClientToServer, Concat(commands.ToArray()), 4096);
        harness.DeliverChunked(TapDirection.ServerToClient, Concat(replies.ToArray()), 4096);

        var responses = harness.Sink.Responses;
        Assert.Equal(200, responses.Count);
        for (var i = 0; i < 200; i++)
            Assert.Equal($"redis://db0/k{i}", responses[i].Uri.ToString());
        Assert.Equal("Set", Method(responses[100]));
        Assert.Equal("Get (Hit)", Method(responses[150]));
        Assert.Contains("200,000 bytes on the wire", responses[150].Content);
        Assert.Equal("v199", responses[199].Content);
        Assert.Equal(2, harness.Tap.OversizePayloadsSkipped);
        Assert.Equal(0, harness.Tap.DecodeErrors);
    }

    [Fact]
    public void AnOversizeElementInsideAnAggregateReplyKeepsTheAggregateAHit()
    {
        using var harness = DecoderHarness.Redis(o => o.BodyCapBytes = 512);
        harness.ClientToServer(Resp.Command("MGET", "a", "b"));
        harness.ServerToClient(Concat(Bytes("*2\r\n"), BigBulk(5000, (byte)'a'), Bytes(Resp.Bulk(null))));

        var response = Assert.Single(harness.Sink.Responses);
        Assert.Equal("Get (Hit)", Method(response));
        Assert.StartsWith("[" + new string('a', 384) + " …[bulk string truncated: 5,000 bytes on the wire, 384 kept], (nil)]", response.Content);
    }

    // ---- desynchronisation: reset and resync instead of a dead tap --------------------------------

    [Fact]
    public void GarbageMidStreamResetsTheDecoderAndTheFirstInteractionAfterItIsStamped()
    {
        using var harness = DecoderHarness.Redis();
        harness.Deliver(TapDirection.ClientToServer, Resp.Command("GET", "a"));
        harness.Deliver(TapDirection.ServerToClient, Resp.Bulk("1"));

        // The reply stream desynchronises (payload bytes where a value should start).
        harness.Deliver(TapDirection.ServerToClient, "{\"not\":\"resp\"}\r\n");

        Assert.True(harness.Decoding);
        Assert.Equal(1, harness.Tap.DecoderResets);
        Assert.Equal(0, harness.Tap.DecodingDisabledConnections);
        Assert.Contains(harness.Degradations, d => d.Kind == CaptureDegradationKind.DecoderReset);
        Assert.Contains(harness.LogLines, l => l.Contains("decoder reset on connection 1"));
        Assert.True(((RedisProtocolDecoder)harness.Decoder).IsResynchronising);

        // Leftover bytes of the old stream are discarded until a segment starts with a command.
        harness.Deliver(TapDirection.ClientToServer, "lue\r\n");
        harness.Deliver(TapDirection.ServerToClient, "+OK\r\n");
        Assert.Single(harness.Sink.Responses);

        // The next command re-arms the decoder; its interaction is stamped, the one after it is not.
        harness.Deliver(TapDirection.ClientToServer, Resp.Command("GET", "b"));
        harness.Deliver(TapDirection.ServerToClient, Resp.Bulk("2"));
        harness.Deliver(TapDirection.ClientToServer, Resp.Command("SET", "c", "3"));
        harness.Deliver(TapDirection.ServerToClient, Resp.Simple("OK"));

        var requests = harness.Sink.Requests;
        var responses = harness.Sink.Responses;
        Assert.Equal(3, responses.Count);
        Assert.Equal("Get (Hit)", Method(responses[1]));
        Assert.Equal("redis://db0/b", responses[1].Uri.ToString());
        Assert.Equal("[resynchronised — pairing uncertain]", requests[1].Content);
        Assert.Equal("[resynchronised — pairing uncertain] 2", responses[1].Content);
        Assert.Contains(requests[1].Headers, h => h is ("x-kronikol-capture", "resynced"));
        Assert.Contains(responses[1].Headers, h => h is ("x-kronikol-capture", "resynced"));

        Assert.Equal("3", requests[2].Content);
        Assert.Equal("OK", responses[2].Content);
        Assert.Empty(requests[2].Headers);
        Assert.False(((RedisProtocolDecoder)harness.Decoder).IsResynchronising);

        var diagnostic = Assert.Single(harness.Tap.Diagnostics());
        Assert.Contains("decoder reset 1 time(s)", diagnostic.Message);
    }

    [Fact]
    public void NonRespBytesOnAFreshConnectionDisableDecodingAndAreCounted()
    {
        using var harness = DecoderHarness.Redis();
        harness.Deliver(TapDirection.ServerToClient, "this is not RESP\r\n");

        Assert.False(harness.Decoding);
        Assert.Equal(1, harness.Tap.DecodingDisabledConnections);
        Assert.Equal(0, harness.Tap.DecoderResets);
        var degradation = Assert.Single(harness.Degradations);
        Assert.Equal(CaptureDegradationKind.DecodingDisabled, degradation.Kind);
        Assert.Equal(1, degradation.ConnectionId);
        Assert.Contains("RespProtocolException", degradation.Detail);
        Assert.Contains(harness.LogLines, l => l.Contains("decoder gave up on connection 1"));
        var diagnostic = Assert.Single(harness.Tap.Diagnostics());
        Assert.Contains("decoding disabled on 1 connection(s)", diagnostic.Message);
        Assert.Contains("redis arrows on them after", diagnostic.Message);

        // A disabled connection drains silently.
        harness.Deliver(TapDirection.ClientToServer, Resp.Command("GET", "k"));
        harness.Deliver(TapDirection.ServerToClient, Resp.Bulk("v"));
        Assert.Empty(harness.Sink.Logs);
    }

    [Fact]
    public void AnUndecodedBytesOverflowResetsTheDecoderInsteadOfKillingTheConnection()
    {
        using var harness = DecoderHarness.Redis(o => o.MaxBufferedBytes = 4096);
        harness.Deliver(TapDirection.ClientToServer, Resp.Command("GET", "a"));
        harness.Deliver(TapDirection.ServerToClient, Resp.Bulk("1"));

        // A reply header whose count was really payload: elements pile up without ever completing a value.
        harness.Deliver(TapDirection.ServerToClient, "*100000\r\n");
        harness.Deliver(TapDirection.ServerToClient, string.Concat(Enumerable.Repeat(":1\r\n", 2000)));

        Assert.True(harness.Decoding);
        Assert.Equal(1, harness.Tap.DecoderResets);
        Assert.Contains(harness.Degradations, d => d.Kind == CaptureDegradationKind.DecoderReset && d.Detail.Contains("MaxBufferedBytes"));

        harness.Deliver(TapDirection.ClientToServer, Resp.Command("GET", "b"));
        harness.Deliver(TapDirection.ServerToClient, Resp.Bulk("2"));
        Assert.Equal(2, harness.Sink.Responses.Count);
    }

    [Fact]
    public void APendingQueueOverflowResetsTheDecoder()
    {
        using var harness = DecoderHarness.Redis();
        var burst = string.Concat(Enumerable.Range(0, 4097).Select(i => Resp.Command("GET", $"k{i}")));
        harness.Deliver(TapDirection.ClientToServer, burst);

        Assert.True(harness.Decoding);
        Assert.Equal(1, harness.Tap.DecoderResets);
        Assert.Equal(0, ((RedisProtocolDecoder)harness.Decoder).PendingCommands);
        Assert.Contains(harness.Degradations, d => d.Kind == CaptureDegradationKind.DecoderReset && d.Detail.Contains("unanswered commands"));
    }

    [Fact]
    public void ResyncAfterOverflowOffDisablesDecodingButStillCountsAndReports()
    {
        using var harness = DecoderHarness.Redis(o =>
        {
            o.ResyncAfterOverflow = false;
            o.MaxBufferedBytes = 4096;
        });
        harness.Deliver(TapDirection.ServerToClient, "*100000\r\n" + string.Concat(Enumerable.Repeat(":1\r\n", 2000)));

        Assert.False(harness.Decoding);
        Assert.Equal(0, harness.Tap.DecoderResets);
        Assert.Equal(1, harness.Tap.DecodingDisabledConnections);
        Assert.Equal(CaptureDegradationKind.DecodingDisabled, Assert.Single(harness.Degradations).Kind);
        Assert.NotEmpty(harness.Tap.Diagnostics());
    }

    [Fact]
    public void RepeatedResetsWithoutProgressEventuallyDisableTheConnection()
    {
        using var harness = DecoderHarness.Redis();
        harness.Deliver(TapDirection.ClientToServer, Resp.Command("GET", "a"));
        harness.Deliver(TapDirection.ServerToClient, Resp.Bulk("1"));

        for (var i = 0; i < RedisProtocolDecoder.MaxConsecutiveResyncs + 2 && harness.Decoding; i++)
        {
            harness.Deliver(TapDirection.ClientToServer, Resp.Command("GET", "b")); // re-arms the reply side
            harness.Deliver(TapDirection.ServerToClient, "}}garbage}}\r\n");
        }

        Assert.False(harness.Decoding);
        Assert.Equal(RedisProtocolDecoder.MaxConsecutiveResyncs, harness.Tap.DecoderResets);
        Assert.Equal(1, harness.Tap.DecodingDisabledConnections);
    }

    [Fact]
    public void AConnectionThatClosesMidOversizeValueIsReported()
    {
        using var harness = DecoderHarness.Redis();
        // The first 100 KB of a 10 MiB SET — and then the socket closes; the CRLF never arrives.
        harness.Deliver(TapDirection.ClientToServer, BigSet("k", 10 * 1024 * 1024)[..(100 * 1024)]);
        harness.Decoder.OnConnectionClosed();

        Assert.Empty(harness.Sink.Logs);
        Assert.Equal(1, harness.Tap.ConnectionsClosedMidMessage);
        var degradation = Assert.Single(harness.Degradations);
        Assert.Equal(CaptureDegradationKind.ConnectionClosedMidMessage, degradation.Kind);
        Assert.Contains("partial command", degradation.Detail);
        Assert.Contains("streaming past an oversize payload", degradation.Detail);
        Assert.Contains(harness.LogLines, l => l.Contains("closed mid-message"));
        var diagnostic = Assert.Single(harness.Tap.Diagnostics());
        Assert.Contains("1 connection(s) closed mid-message", diagnostic.Message);

        // Dispose after close must not report twice.
        harness.Decoder.Dispose();
        Assert.Single(harness.Degradations);
    }

    [Fact]
    public void AConnectionThatClosesWithACommandUnansweredIsReported()
    {
        using var harness = DecoderHarness.Redis();
        harness.Deliver(TapDirection.ClientToServer, Resp.Command("GET", "k"));
        harness.Decoder.OnConnectionClosed();

        Assert.Equal(1, harness.Tap.ConnectionsClosedMidMessage);
        Assert.Contains("1 unanswered command(s)", Assert.Single(harness.Degradations).Detail);
    }

    [Fact]
    public void ACleanlyClosedConnectionReportsNothing()
    {
        using var harness = DecoderHarness.Redis();
        harness.Deliver(TapDirection.ClientToServer, Resp.Command("GET", "k"));
        harness.Deliver(TapDirection.ServerToClient, Resp.Bulk("v"));
        harness.Decoder.OnConnectionClosed();

        Assert.Equal(0, harness.Tap.ConnectionsClosedMidMessage);
        Assert.Empty(harness.Degradations);
        Assert.Empty(harness.Tap.Diagnostics());
    }

    [Fact]
    public void StartsAtCommandBoundaryRecognisesOnlyCommandStarts()
    {
        Assert.True(RedisProtocolDecoder.StartsAtCommandBoundary("*2\r\n$3\r\nGET\r\n$1\r\nk\r\n"u8, acceptInline: false));
        Assert.True(RedisProtocolDecoder.StartsAtCommandBoundary("PING\r\n"u8, acceptInline: true));
        Assert.False(RedisProtocolDecoder.StartsAtCommandBoundary("PING\r\n"u8, acceptInline: false));
        Assert.False(RedisProtocolDecoder.StartsAtCommandBoundary("*\r\n$"u8, acceptInline: false));
        Assert.False(RedisProtocolDecoder.StartsAtCommandBoundary("*2\r\n+"u8, acceptInline: false));
        Assert.False(RedisProtocolDecoder.StartsAtCommandBoundary("lue\r\n"u8, acceptInline: false));
        Assert.False(RedisProtocolDecoder.StartsAtCommandBoundary("{\"json\":1}"u8, acceptInline: true));
        Assert.False(RedisProtocolDecoder.StartsAtCommandBoundary(""u8, acceptInline: true));
    }

    [Fact]
    public void AnInlineClientIsResyncedOnItsNextInlineCommand()
    {
        using var harness = DecoderHarness.Redis();
        harness.Deliver(TapDirection.ClientToServer, "GET a\r\n");
        harness.Deliver(TapDirection.ServerToClient, Resp.Bulk("1"));
        harness.Deliver(TapDirection.ServerToClient, "}}garbage}}\r\n");
        Assert.Equal(1, harness.Tap.DecoderResets);

        harness.Deliver(TapDirection.ClientToServer, "GET b\r\n");
        harness.Deliver(TapDirection.ServerToClient, Resp.Bulk("2"));

        Assert.Equal(2, harness.Sink.Responses.Count);
        Assert.Equal("redis://db0/b", harness.Sink.Responses[1].Uri.ToString());
    }
}
