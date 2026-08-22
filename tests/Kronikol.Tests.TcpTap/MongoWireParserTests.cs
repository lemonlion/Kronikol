using Kronikol.Extensions.TcpTap.Protocols;
using MongoDB.Bson;

namespace Kronikol.Tests.TcpTap;

public class MongoWireParserTests
{
    [Fact]
    public void AShortBufferAsksForMoreBytes()
    {
        Assert.False(MongoWireParser.TryReadHeader(new byte[8], out _));
    }

    [Fact]
    public void AHeaderIsReadButFramingWaitsForTheWholeMessage()
    {
        var message = MongoWire.Msg(7, 0, new BsonDocument { { "find", "Trial" }, { "$db", "app" } });
        Assert.False(MongoWireParser.TryReadHeader(message.AsSpan(0, 20), out var header));
        Assert.Equal(message.Length, header.MessageLength);
        Assert.Equal(7, header.RequestId);
        Assert.Equal(MongoOpCodes.OpMsg, header.OpCode);

        Assert.True(MongoWireParser.TryReadHeader(message, out _));
    }

    [Fact]
    public void AnImplausibleLengthIsAProtocolError()
    {
        var bytes = new byte[16];
        BitConverter.GetBytes(int.MaxValue).CopyTo(bytes, 0);
        Assert.Throws<MongoWireProtocolException>(() => MongoWireParser.TryReadHeader(bytes, out _));
    }

    [Fact]
    public void AKindZeroBodyIsRead()
    {
        var body = new BsonDocument { { "find", "Trial" }, { "filter", new BsonDocument("status", "active") }, { "$db", "app" } };
        var parsed = MongoWireParser.ParseOpMsg(MongoWire.Msg(1, 0, body));

        Assert.Equal("find", parsed.Document.GetElement(0).Name);
        Assert.Equal("app", parsed.Document["$db"].AsString);
        Assert.False(parsed.MoreToCome);
        Assert.False(parsed.ChecksumPresent);
    }

    [Fact]
    public void KindOneDocumentSequencesAreFoldedIntoTheBody()
    {
        var body = new BsonDocument { { "insert", "Trial" }, { "$db", "app" } };
        var documents = new[]
        {
            new BsonDocument { { "_id", 1 }, { "name", "one" } },
            new BsonDocument { { "_id", 2 }, { "name", "two" } },
        };

        var parsed = MongoWireParser.ParseOpMsg(MongoWire.Msg(1, 0, body, 0, ("documents", documents)));

        Assert.Equal("insert", parsed.Document.GetElement(0).Name);
        var folded = parsed.Document["documents"].AsBsonArray;
        Assert.Equal(2, folded.Count);
        Assert.Equal("two", folded[1]["name"].AsString);
    }

    [Fact]
    public void SeveralKindOneSequencesAreAllFolded()
    {
        var body = new BsonDocument { { "update", "Trial" }, { "$db", "app" } };
        var parsed = MongoWireParser.ParseOpMsg(MongoWire.Msg(
            1, 0, body, 0,
            ("updates", [new BsonDocument { { "q", new BsonDocument("_id", 1) }, { "u", new BsonDocument("$set", new BsonDocument("x", 1)) } }]),
            ("extra", [new BsonDocument("a", 1)])));

        Assert.Single(parsed.Document["updates"].AsBsonArray);
        Assert.Single(parsed.Document["extra"].AsBsonArray);
    }

    [Fact]
    public void AChecksumIsSkippedRatherThanParsedAsASection()
    {
        var body = new BsonDocument { { "find", "Trial" }, { "$db", "app" } };
        var parsed = MongoWireParser.ParseOpMsg(MongoWire.Msg(1, 0, body, flags: 1));

        Assert.True(parsed.ChecksumPresent);
        Assert.Equal("find", parsed.Document.GetElement(0).Name);
    }

    [Fact]
    public void MoreToComeAndExhaustAllowedFlagsAreDecoded()
    {
        var body = new BsonDocument { { "insert", "Trial" }, { "$db", "app" } };
        var parsed = MongoWireParser.ParseOpMsg(MongoWire.Msg(1, 0, body, flags: 2 | (1u << 16)));

        Assert.True(parsed.MoreToCome);
        Assert.True(parsed.ExhaustAllowed);
    }

    [Fact]
    public void AnUnknownSectionKindIsAProtocolError()
    {
        var message = MongoWire.Msg(1, 0, new BsonDocument { { "find", "Trial" }, { "$db", "app" } });
        message[20] = 7; // the section-kind byte
        Assert.Throws<MongoWireProtocolException>(() => MongoWireParser.ParseOpMsg(message));
    }

    [Fact]
    public void ALegacyOpQueryHandshakeIsDecodedEnoughToBeRecognised()
    {
        var message = MongoWire.Query(1, "admin.$cmd", new BsonDocument { { "isMaster", 1 }, { "helloOk", true } });
        var (collection, query) = MongoWireParser.ParseOpQuery(message);

        Assert.Equal("admin.$cmd", collection);
        Assert.Equal("isMaster", query.GetElement(0).Name);
    }

    [Fact]
    public void AnOpCompressedHeaderIsReadWithoutDecompressing()
    {
        var message = MongoWire.Compressed(1, 0, MongoOpCodes.OpMsg, compressorId: 2, compressedBody: [1, 2, 3, 4]);
        var (originalOpCode, uncompressedSize, compressorId) = MongoWireParser.ParseOpCompressedHeader(message);

        Assert.Equal(MongoOpCodes.OpMsg, originalOpCode);
        Assert.Equal(4, uncompressedSize);
        Assert.Equal(2, compressorId);
    }
}
