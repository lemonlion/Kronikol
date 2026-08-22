using System.Buffers.Binary;
using System.Text;
using global::MongoDB.Bson;
using global::MongoDB.Bson.Serialization;

namespace Kronikol.Extensions.TcpTap.Protocols;

/// <summary>Raised when the bytes on a tapped MongoDB connection are not a valid wire message.</summary>
public sealed class MongoWireProtocolException(string message) : TapProtocolException(message);

/// <summary>The MongoDB wire-protocol op codes this tap recognises.</summary>
public static class MongoOpCodes
{
    /// <summary>Legacy reply to <see cref="OpQuery"/>.</summary>
    public const int OpReply = 1;

    /// <summary>Legacy query — the connection handshake still uses it.</summary>
    public const int OpQuery = 2004;

    /// <summary>A compressed envelope around another message.</summary>
    public const int OpCompressed = 2012;

    /// <summary>The modern command message.</summary>
    public const int OpMsg = 2013;
}

/// <summary>The 16-byte header every MongoDB wire message starts with.</summary>
/// <param name="MessageLength">Total message length in bytes, header included.</param>
/// <param name="RequestId">The sender's id for this message.</param>
/// <param name="ResponseTo">The <paramref name="RequestId"/> this message answers (0 when it answers nothing).</param>
/// <param name="OpCode">One of <see cref="MongoOpCodes"/>.</param>
public readonly record struct MongoMessageHeader(int MessageLength, int RequestId, int ResponseTo, int OpCode);

/// <summary>An OP_MSG message decoded into its flags and one merged command (or reply) document.</summary>
/// <param name="Flags">The raw flag bits.</param>
/// <param name="Document">Section kind 0 merged with every kind-1 document sequence, so <c>documents</c>/<c>updates</c>/<c>deletes</c> appear as arrays.</param>
public readonly record struct MongoOpMsg(uint Flags, BsonDocument Document)
{
    /// <summary>Bit 0 — a CRC-32C checksum follows the sections.</summary>
    public bool ChecksumPresent => (Flags & 1) != 0;

    /// <summary>Bit 1 — the sender will not wait for (or will keep sending after) this message.</summary>
    public bool MoreToCome => (Flags & 2) != 0;

    /// <summary>Bit 16 — the client is willing to receive an exhaust cursor.</summary>
    public bool ExhaustAllowed => (Flags & (1u << 16)) != 0;
}

/// <summary>
/// Framing and section parsing for the MongoDB wire protocol: enough to recognise a message, pull the command
/// document out of an OP_MSG, and step safely over everything else.
/// </summary>
/// <remarks>
/// The tap never re-encodes anything — this only reads a copy of the bytes that were already forwarded.
/// OP_COMPRESSED is deliberately not decompressed: it is passed through and reported once, so a client that
/// negotiates <c>compressors=</c> degrades to "no capture" rather than to a broken connection.
/// </remarks>
public static class MongoWireParser
{
    /// <summary>The largest message the parser will frame (48 MiB — well past MongoDB's 16 MiB document limit).</summary>
    public const int MaxMessageLength = 48 * 1024 * 1024;

    /// <summary>Reads the header if a whole message is present.</summary>
    /// <returns>False when more bytes are needed.</returns>
    /// <exception cref="MongoWireProtocolException">The framing is impossible (bad length).</exception>
    public static bool TryReadHeader(ReadOnlySpan<byte> buffer, out MongoMessageHeader header) =>
        TryPeekHeader(buffer, out header) && buffer.Length >= header.MessageLength;

    /// <summary>Reads the header as soon as its 16 bytes are present, whether or not the rest of the message is.</summary>
    /// <returns>False when fewer than 16 bytes are available.</returns>
    /// <exception cref="MongoWireProtocolException">The framing is impossible (bad length).</exception>
    public static bool TryPeekHeader(ReadOnlySpan<byte> buffer, out MongoMessageHeader header)
    {
        header = default;
        if (buffer.Length < 16)
            return false;

        var length = BinaryPrimitives.ReadInt32LittleEndian(buffer);
        if (length is < 16 or > MaxMessageLength)
            throw new MongoWireProtocolException($"Implausible MongoDB message length {length}; the stream is not framed as expected.");

        header = new MongoMessageHeader(
            length,
            BinaryPrimitives.ReadInt32LittleEndian(buffer[4..]),
            BinaryPrimitives.ReadInt32LittleEndian(buffer[8..]),
            BinaryPrimitives.ReadInt32LittleEndian(buffer[12..]));
        return true;
    }

    /// <summary>Whether <paramref name="buffer"/> starts with something that looks like a message header: a plausible length and a known op code. Used to find a message boundary after a desync.</summary>
    public static bool LooksLikeHeader(ReadOnlySpan<byte> buffer)
    {
        if (buffer.Length < 16)
            return false;
        var length = BinaryPrimitives.ReadInt32LittleEndian(buffer);
        if (length is < 16 or > MaxMessageLength)
            return false;
        var opCode = BinaryPrimitives.ReadInt32LittleEndian(buffer[12..]);
        return opCode is MongoOpCodes.OpMsg or MongoOpCodes.OpQuery or MongoOpCodes.OpReply or MongoOpCodes.OpCompressed;
    }

    /// <summary>
    /// Decodes an OP_MSG message (header included) into its flags and one merged document. Kind-1 document
    /// sequences are folded into the kind-0 body under their identifier, so an insert's <c>documents</c>
    /// sequence classifies exactly like an inline <c>documents</c> array.
    /// </summary>
    public static MongoOpMsg ParseOpMsg(ReadOnlySpan<byte> message)
    {
        if (message.Length < 21)
            throw new MongoWireProtocolException($"OP_MSG is {message.Length} bytes, too short for flags and one section.");

        var flags = BinaryPrimitives.ReadUInt32LittleEndian(message[16..]);
        var end = message.Length - ((flags & 1) != 0 ? 4 : 0);
        var offset = 20;

        BsonDocument? body = null;
        var sequences = new Dictionary<string, BsonArray>(StringComparer.Ordinal);

        while (offset < end)
        {
            var kind = message[offset];
            offset++;
            switch (kind)
            {
                case 0:
                {
                    var document = ReadDocument(message, ref offset);
                    body ??= document;
                    break;
                }

                case 1:
                {
                    if (offset + 4 > end)
                        throw new MongoWireProtocolException("Truncated OP_MSG document sequence.");
                    var size = BinaryPrimitives.ReadInt32LittleEndian(message[offset..]);
                    if (size < 5 || offset + size > end)
                        throw new MongoWireProtocolException($"Implausible OP_MSG document-sequence size {size}.");
                    var sectionEnd = offset + size;
                    var cursor = offset + 4;
                    var identifier = ReadCString(message, ref cursor);
                    var documents = new BsonArray();
                    while (cursor < sectionEnd)
                        documents.Add(ReadDocument(message, ref cursor));
                    sequences[identifier] = documents;
                    offset = sectionEnd;
                    break;
                }

                default:
                    throw new MongoWireProtocolException($"Unknown OP_MSG section kind {kind}.");
            }
        }

        body ??= [];
        foreach (var (identifier, documents) in sequences)
            body[identifier] = documents;

        return new MongoOpMsg(flags, body);
    }

    /// <summary>
    /// Decodes the query document of a legacy OP_QUERY (the handshake). The tap records nothing from it — this
    /// exists so the message can be recognised and stepped over, and so the handshake shape can be reported.
    /// </summary>
    public static (string CollectionName, BsonDocument Query) ParseOpQuery(ReadOnlySpan<byte> message)
    {
        if (message.Length < 16 + 4 + 1 + 8)
            throw new MongoWireProtocolException($"OP_QUERY is {message.Length} bytes, too short.");

        var offset = 20; // header + flags
        var collection = ReadCString(message, ref offset);
        offset += 8; // numberToSkip + numberToReturn
        var query = ReadDocument(message, ref offset);
        return (collection, query);
    }

    /// <summary>Decodes the header fields of an OP_COMPRESSED envelope without decompressing it.</summary>
    public static (int OriginalOpCode, int UncompressedSize, byte CompressorId) ParseOpCompressedHeader(ReadOnlySpan<byte> message)
    {
        if (message.Length < 25)
            throw new MongoWireProtocolException($"OP_COMPRESSED is {message.Length} bytes, too short.");
        return (
            BinaryPrimitives.ReadInt32LittleEndian(message[16..]),
            BinaryPrimitives.ReadInt32LittleEndian(message[20..]),
            message[24]);
    }

    /// <summary>Reads one BSON document at <paramref name="offset"/> and advances past it.</summary>
    public static BsonDocument ReadDocument(ReadOnlySpan<byte> buffer, ref int offset)
    {
        if (offset + 4 > buffer.Length)
            throw new MongoWireProtocolException("Truncated BSON document header.");
        var length = BinaryPrimitives.ReadInt32LittleEndian(buffer[offset..]);
        if (length < 5 || offset + length > buffer.Length)
            throw new MongoWireProtocolException($"Implausible BSON document length {length}.");

        var bytes = buffer.Slice(offset, length).ToArray();
        offset += length;
        try
        {
            return BsonSerializer.Deserialize<BsonDocument>(bytes);
        }
        catch (Exception ex) when (ex is not MongoWireProtocolException)
        {
            throw new MongoWireProtocolException($"BSON document could not be read: {ex.GetType().Name}: {ex.Message}");
        }
    }

    /// <summary>Reads a NUL-terminated UTF-8 string and advances past the terminator.</summary>
    public static string ReadCString(ReadOnlySpan<byte> buffer, ref int offset)
    {
        var start = offset;
        while (offset < buffer.Length && buffer[offset] != 0)
            offset++;
        if (offset >= buffer.Length)
            throw new MongoWireProtocolException("Unterminated cstring.");
        var text = Encoding.UTF8.GetString(buffer[start..offset]);
        offset++;
        return text;
    }
}
