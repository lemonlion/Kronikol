using System.Net;
using Kronikol.Constants;
using Kronikol.Extensions.TcpTap;
using Kronikol.Extensions.TcpTap.Protocols;
using Kronikol.Tracking;
using MongoDB.Bson;

namespace Kronikol.Tests.TcpTap;

public class MongoDecoderTests
{
    private static string Method(RequestResponseLog log) => log.Method.Value?.ToString() ?? "";

    private static BsonDocument Find(string collection, BsonDocument? filter = null, string database = "app") =>
        new()
        {
            { "find", collection },
            { "filter", filter ?? [] },
            { "$db", database },
        };

    private static BsonDocument CursorReply(params BsonDocument[] documents) =>
        new()
        {
            { "cursor", new BsonDocument { { "firstBatch", new BsonArray(documents) }, { "id", 0L }, { "ns", "app.Trial" } } },
            { "ok", 1.0 },
        };

    [Fact]
    public void AFindIsLabelledAndAddressedLikeTheInProcessExtension()
    {
        using var harness = DecoderHarness.Mongo();
        harness.ClientToServer(MongoWire.Msg(1, 0, Find("Trial", new BsonDocument("status", "active"))));
        harness.ServerToClient(MongoWire.Msg(2, 1, CursorReply(new BsonDocument { { "_id", 1 }, { "status", "active" } })));

        var request = Assert.Single(harness.Sink.Requests);
        var response = Assert.Single(harness.Sink.Responses);

        Assert.Equal("Find ← Trial", Method(request));
        Assert.Equal("Find ← Trial", Method(response));
        Assert.Equal("mongodb:///app/Trial", request.Uri.ToString());
        Assert.Equal("{ \"status\" : \"active\" }", request.Content);
        Assert.Contains("\"status\" : \"active\"", response.Content);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode!.Value);
        Assert.Equal(DependencyCategories.MongoDB, request.DependencyCategory);
    }

    [Fact]
    public void TheDatabaseInTheUriComesFromTheCommandsDollarDb()
    {
        using var harness = DecoderHarness.Mongo();
        harness.ClientToServer(MongoWire.Msg(1, 0, Find("Trial", database: "data-insights-Development")));
        harness.ServerToClient(MongoWire.Msg(2, 1, CursorReply()));

        Assert.Equal("mongodb:///data-insights-Development/Trial", Assert.Single(harness.Sink.Requests).Uri.ToString());
    }

    [Fact]
    public void AnInsertCarriedInAKindOneSequenceIsCounted()
    {
        using var harness = DecoderHarness.Mongo();
        var body = new BsonDocument { { "insert", "Trial" }, { "$db", "app" } };
        harness.ClientToServer(MongoWire.Msg(1, 0, body, 0, ("documents", [new BsonDocument("_id", 1)])));
        harness.ServerToClient(MongoWire.Msg(2, 1, new BsonDocument { { "n", 1 }, { "ok", 1.0 } }));

        var request = Assert.Single(harness.Sink.Requests);
        Assert.Equal("Insert → Trial", Method(request));
        Assert.Equal("mongodb:///app/Trial", request.Uri.ToString());
        Assert.Equal("n=1", Assert.Single(harness.Sink.Responses).Content);
    }

    [Fact]
    public void SeveralInsertedDocumentsShowTheirCount()
    {
        using var harness = DecoderHarness.Mongo();
        var body = new BsonDocument { { "insert", "Trial" }, { "$db", "app" } };
        harness.ClientToServer(MongoWire.Msg(1, 0, body, 0,
            ("documents", [new BsonDocument("_id", 1), new BsonDocument("_id", 2), new BsonDocument("_id", 3)])));
        harness.ServerToClient(MongoWire.Msg(2, 1, new BsonDocument { { "n", 3 }, { "ok", 1.0 } }));

        Assert.Equal("Insert (×3) → Trial", Method(Assert.Single(harness.Sink.Requests)));
    }

    [Fact]
    public void AnUpdateReportsNAndNModified()
    {
        using var harness = DecoderHarness.Mongo();
        var body = new BsonDocument { { "update", "Trial" }, { "$db", "app" } };
        harness.ClientToServer(MongoWire.Msg(1, 0, body, 0,
            ("updates", [new BsonDocument { { "q", new BsonDocument("_id", 1) }, { "u", new BsonDocument("$set", new BsonDocument("x", 2)) } }])));
        harness.ServerToClient(MongoWire.Msg(2, 1, new BsonDocument { { "n", 1 }, { "nModified", 1 }, { "ok", 1.0 } }));

        Assert.Equal("Update → Trial", Method(Assert.Single(harness.Sink.Requests)));
        Assert.Equal("n=1, nModified=1", Assert.Single(harness.Sink.Responses).Content);
    }

    [Fact]
    public void AnAggregateListsItsPipelineStages()
    {
        using var harness = DecoderHarness.Mongo();
        var body = new BsonDocument
        {
            { "aggregate", "x" },
            { "pipeline", new BsonArray { new BsonDocument("$match", new BsonDocument("a", 1)), new BsonDocument("$group", new BsonDocument("_id", "$a")) } },
            { "$db", "app" },
        };
        harness.ClientToServer(MongoWire.Msg(1, 0, body));
        harness.ServerToClient(MongoWire.Msg(2, 1, CursorReply()));

        Assert.Equal("Aggregate ($match, $group) ← x", Method(Assert.Single(harness.Sink.Requests)));
    }

    [Fact]
    public void FindAndModifyGetsTheTwoWayArrow()
    {
        using var harness = DecoderHarness.Mongo();
        var body = new BsonDocument { { "findAndModify", "Trial" }, { "query", new BsonDocument("_id", 1) }, { "$db", "app" } };
        harness.ClientToServer(MongoWire.Msg(1, 0, body));
        harness.ServerToClient(MongoWire.Msg(2, 1, new BsonDocument { { "ok", 1.0 } }));

        Assert.Equal("FindAndModify ↔ Trial", Method(Assert.Single(harness.Sink.Requests)));
    }

    [Fact]
    public void ReplyDocumentsAreCappedByMaxResponseDocuments()
    {
        using var harness = DecoderHarness.Mongo(o => o.MaxResponseDocuments = 2);
        harness.ClientToServer(MongoWire.Msg(1, 0, Find("Trial")));
        harness.ServerToClient(MongoWire.Msg(2, 1, CursorReply(
            new BsonDocument("_id", 1), new BsonDocument("_id", 2), new BsonDocument("_id", 3), new BsonDocument("_id", 4))));

        Assert.Contains("(2 more documents not shown)", Assert.Single(harness.Sink.Responses).Content);
    }

    [Fact]
    public void AnEmptyBatchSaysSo()
    {
        using var harness = DecoderHarness.Mongo();
        harness.ClientToServer(MongoWire.Msg(1, 0, Find("Trial")));
        harness.ServerToClient(MongoWire.Msg(2, 1, CursorReply()));

        Assert.Equal("0 documents", Assert.Single(harness.Sink.Responses).Content);
    }

    [Fact]
    public void ACommandThatFailedBecomesA500WithItsErrmsg()
    {
        using var harness = DecoderHarness.Mongo();
        harness.ClientToServer(MongoWire.Msg(1, 0, Find("Trial")));
        harness.ServerToClient(MongoWire.Msg(2, 1, new BsonDocument { { "ok", 0.0 }, { "errmsg", "not authorized on app" }, { "code", 13 } }));

        var response = Assert.Single(harness.Sink.Responses);
        Assert.Equal(HttpStatusCode.InternalServerError, response.StatusCode!.Value);
        Assert.Equal("not authorized on app", response.Content);
    }

    // ---- matching ---------------------------------------------------------------------------------

    [Fact]
    public void RepliesAreMatchedByResponseToNotByOrder()
    {
        using var harness = DecoderHarness.Mongo();
        harness.ClientToServer(MongoWire.Msg(10, 0, Find("First")));
        harness.ClientToServer(MongoWire.Msg(11, 0, Find("Second")));
        // Answered out of order, as a server may.
        harness.ServerToClient(MongoWire.Msg(90, 11, CursorReply()));
        harness.ServerToClient(MongoWire.Msg(91, 10, CursorReply()));

        // A pair is written when its reply lands, so the sink order follows the replies; the request
        // half still carries the real command timestamp, which is what the ingest orders on.
        Assert.Equal("mongodb:///app/Second", harness.Sink.Requests[0].Uri.ToString());
        Assert.Equal("mongodb:///app/First", harness.Sink.Requests[1].Uri.ToString());
        Assert.Equal("Find ← Second", Method(harness.Sink.Responses[0]));
        Assert.Equal("Find ← First", Method(harness.Sink.Responses[1]));
    }

    [Fact]
    public void AMoreToComeReplyThatAnswersNothingIsSkipped()
    {
        using var harness = DecoderHarness.Mongo();
        // The streaming `hello` a monitoring connection receives, unsolicited.
        harness.ServerToClient(MongoWire.Msg(50, 0, new BsonDocument { { "isWritablePrimary", true }, { "ok", 1.0 } }, flags: 2));
        harness.ServerToClient(MongoWire.Msg(51, 0, new BsonDocument { { "isWritablePrimary", true }, { "ok", 1.0 } }, flags: 2));

        Assert.Empty(harness.Sink.Logs);
    }

    [Fact]
    public void AMoreToComeCommandIsRecordedWithoutWaitingForAReply()
    {
        using var harness = DecoderHarness.Mongo();
        var body = new BsonDocument { { "insert", "Trial" }, { "writeConcern", new BsonDocument("w", 0) }, { "$db", "app" } };
        harness.ClientToServer(MongoWire.Msg(1, 0, body, flags: 2, ("documents", [new BsonDocument("_id", 1)])));

        Assert.Equal("Insert → Trial", Method(Assert.Single(harness.Sink.Requests)));
        Assert.Equal(HttpStatusCode.OK, Assert.Single(harness.Sink.Responses).StatusCode!.Value);
    }

    [Fact]
    public void MessagesSplitAcrossSegmentsAreReassembled()
    {
        using var harness = DecoderHarness.Mongo();
        var command = MongoWire.Msg(1, 0, Find("Trial", new BsonDocument("a", 1)));
        harness.ClientToServer(command[..7]);
        harness.ClientToServer(command[7..20]);
        harness.ClientToServer(command[20..]);
        harness.ServerToClient(MongoWire.Msg(2, 1, CursorReply()));

        Assert.Equal("Find ← Trial", Method(Assert.Single(harness.Sink.Requests)));
    }

    [Fact]
    public void TwoMessagesInOneSegmentAreBothDecoded()
    {
        using var harness = DecoderHarness.Mongo();
        harness.ClientToServer([.. MongoWire.Msg(1, 0, Find("A")), .. MongoWire.Msg(2, 0, Find("B"))]);
        harness.ServerToClient([.. MongoWire.Msg(3, 1, CursorReply()), .. MongoWire.Msg(4, 2, CursorReply())]);

        Assert.Equal(2, harness.Sink.Responses.Count);
        Assert.Equal("Find ← A", Method(harness.Sink.Responses[0]));
        Assert.Equal("Find ← B", Method(harness.Sink.Responses[1]));
    }

    // ---- pass-through and security ----------------------------------------------------------------

    [Fact]
    public void TheLegacyOpQueryHandshakeIsPassedThroughAndRecordedNever()
    {
        var diagnostics = new List<string>();
        using var harness = DecoderHarness.Mongo(o => o.Log = diagnostics.Add);
        harness.ClientToServer(MongoWire.Query(1, "admin.$cmd", new BsonDocument { { "isMaster", 1 }, { "helloOk", true } }));
        harness.ServerToClient(MongoWire.Reply(2, 1, new BsonDocument { { "isWritablePrimary", true }, { "ok", 1.0 } }));

        Assert.Empty(harness.Sink.Logs);
        Assert.Contains(diagnostics, d => d.Contains("legacy OP_QUERY handshake"));
        Assert.True(((MongoProtocolDecoder)harness.Decoder).LegacyHandshakeSeen);
    }

    [Fact]
    public void AnOpQueryHandshakeIsFollowedNormallyByOpMsgCommands()
    {
        using var harness = DecoderHarness.Mongo();
        harness.ClientToServer(MongoWire.Query(1, "admin.$cmd", new BsonDocument("isMaster", 1)));
        harness.ServerToClient(MongoWire.Reply(2, 1, new BsonDocument { { "ok", 1.0 } }));
        harness.ClientToServer(MongoWire.Msg(3, 0, Find("Trial")));
        harness.ServerToClient(MongoWire.Msg(4, 3, CursorReply()));

        Assert.Equal("Find ← Trial", Method(Assert.Single(harness.Sink.Requests)));
    }

    [Fact]
    public void OpCompressedIsPassedThroughAndReportedOnce()
    {
        var diagnostics = new List<string>();
        using var harness = DecoderHarness.Mongo(o => o.Log = diagnostics.Add);
        harness.ClientToServer(MongoWire.Compressed(1, 0, MongoOpCodes.OpMsg, 2, [9, 9, 9, 9]));
        harness.ClientToServer(MongoWire.Compressed(2, 0, MongoOpCodes.OpMsg, 2, [8, 8, 8, 8]));

        Assert.Empty(harness.Sink.Logs);
        Assert.Single(diagnostics, d => d.Contains("OP_COMPRESSED"));
        Assert.Contains("compressors=", diagnostics.Single(d => d.Contains("OP_COMPRESSED")));
        Assert.True(((MongoProtocolDecoder)harness.Decoder).CompressionSeen);
    }

    [Theory]
    [InlineData("hello")]
    [InlineData("isMaster")]
    [InlineData("ping")]
    [InlineData("buildInfo")]
    [InlineData("getParameter")]
    [InlineData("endSessions")]
    public void TopologyChatterIsNeverRecorded(string command)
    {
        using var harness = DecoderHarness.Mongo();
        harness.ClientToServer(MongoWire.Msg(1, 0, new BsonDocument { { command, 1 }, { "$db", "admin" } }));
        harness.ServerToClient(MongoWire.Msg(2, 1, new BsonDocument { { "ok", 1.0 } }));

        Assert.Empty(harness.Sink.Logs);
    }

    [Theory]
    [InlineData("saslStart")]
    [InlineData("saslContinue")]
    [InlineData("authenticate")]
    [InlineData("createUser")]
    public void AuthenticationIsNeverRecordedEvenWhenTheExclusionListIsEmptied(string command)
    {
        using var harness = DecoderHarness.Mongo(o => o.ExcludedCommands.Clear());
        harness.ClientToServer(MongoWire.Msg(1, 0, new BsonDocument
        {
            { command, 1 },
            { "payload", new BsonBinaryData("n,,n=root,r=hunter2"u8.ToArray()) },
            { "$db", "admin" },
        }));
        harness.ServerToClient(MongoWire.Msg(2, 1, new BsonDocument { { "ok", 1.0 } }));

        Assert.Empty(harness.Sink.Logs);
    }

    [Fact]
    public void GetMoreIsOffByDefaultAndCanBeTurnedOn()
    {
        var command = new BsonDocument { { "getMore", 12L }, { "collection", "Trial" }, { "$db", "app" } };

        using (var off = DecoderHarness.Mongo())
        {
            off.ClientToServer(MongoWire.Msg(1, 0, command));
            off.ServerToClient(MongoWire.Msg(2, 1, new BsonDocument { { "ok", 1.0 } }));
            Assert.Empty(off.Sink.Logs);
        }

        using var on = DecoderHarness.Mongo(o => o.TrackGetMore = true);
        on.ClientToServer(MongoWire.Msg(1, 0, command));
        on.ServerToClient(MongoWire.Msg(2, 1, new BsonDocument { { "ok", 1.0 } }));
        Assert.Equal("GetMore", Method(Assert.Single(on.Sink.Requests)));
    }

    // ---- verbosity and redaction ------------------------------------------------------------------

    [Fact]
    public void RawVerbosityRecordsTheWholeCommandAndReply()
    {
        using var harness = DecoderHarness.Mongo(o => o.Verbosity = TapVerbosity.Raw);
        harness.ClientToServer(MongoWire.Msg(1, 0, Find("Trial", new BsonDocument("a", 1))));
        harness.ServerToClient(MongoWire.Msg(2, 1, CursorReply(new BsonDocument("_id", 5))));

        var request = Assert.Single(harness.Sink.Requests);
        Assert.Equal("Find app.Trial filter={ \"a\" : 1 }", Method(request));
        Assert.Contains("\"$db\" : \"app\"", request.Content);
        Assert.Contains("firstBatch", Assert.Single(harness.Sink.Responses).Content);
    }

    [Fact]
    public void SummarisedVerbosityDropsTheCollectionAndTheContent()
    {
        using var harness = DecoderHarness.Mongo(o => o.Verbosity = TapVerbosity.Summarised);
        harness.ClientToServer(MongoWire.Msg(1, 0, Find("Trial", new BsonDocument("secret", "x"))));
        harness.ServerToClient(MongoWire.Msg(2, 1, CursorReply(new BsonDocument("_id", 5))));

        var request = Assert.Single(harness.Sink.Requests);
        Assert.Equal("Find", Method(request));
        Assert.Equal("mongodb:///app", request.Uri.ToString());
        Assert.Null(request.Content);
    }

    [Fact]
    public void LogFilterTextFalseKeepsTheFilterOutOfTheNote()
    {
        using var harness = DecoderHarness.Mongo(o => o.LogFilterText = false);
        harness.ClientToServer(MongoWire.Msg(1, 0, Find("Trial", new BsonDocument("pii", "x"))));
        harness.ServerToClient(MongoWire.Msg(2, 1, CursorReply()));

        Assert.Null(Assert.Single(harness.Sink.Requests).Content);
    }

    [Fact]
    public void DocumentRedactionRunsBeforeAnythingReachesTheSink()
    {
        using var harness = DecoderHarness.Mongo(o => o.DocumentRedaction = text => text.Replace("ada@example.com", "[EMAIL]"));
        harness.ClientToServer(MongoWire.Msg(1, 0, Find("Trial", new BsonDocument("email", "ada@example.com"))));
        harness.ServerToClient(MongoWire.Msg(2, 1, CursorReply(new BsonDocument("email", "ada@example.com"))));

        Assert.DoesNotContain("ada@example.com", Assert.Single(harness.Sink.Requests).Content);
        Assert.DoesNotContain("ada@example.com", Assert.Single(harness.Sink.Responses).Content);
        Assert.Contains("[EMAIL]", Assert.Single(harness.Sink.Responses).Content);
    }

    [Fact]
    public void TheTimestampsSpanTheCommandAndItsReply()
    {
        using var harness = DecoderHarness.Mongo();
        var start = new DateTimeOffset(2026, 8, 22, 10, 0, 0, TimeSpan.Zero);
        harness.ClientToServer(MongoWire.Msg(1, 0, Find("Trial")), start);
        harness.ServerToClient(MongoWire.Msg(2, 1, CursorReply()), start.AddMilliseconds(12));

        Assert.Equal(start, Assert.Single(harness.Sink.Requests).Timestamp);
        Assert.Equal(start.AddMilliseconds(12), Assert.Single(harness.Sink.Responses).Timestamp);
    }

    [Fact]
    public void GarbageFramingStopsDecodingRatherThanCorruptingTheStream()
    {
        using var harness = DecoderHarness.Mongo();
        Assert.Throws<MongoWireProtocolException>(() => harness.ClientToServer(new byte[] { 0xff, 0xff, 0xff, 0x7f, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0 }));
    }
}
