using System.Net;
using global::MongoDB.Bson;
using Kronikol.Tracking;

namespace Kronikol.Extensions.TcpTap.Protocols;

/// <summary>
/// Decodes one tapped MongoDB connection: OP_MSG commands one way, OP_MSG replies the other, matched by the
/// header's <c>responseTo</c>.
/// </summary>
/// <remarks>
/// <para>Labels come from the same <see cref="MongoDbOperationClassifier"/> source file the in-process
/// <c>Kronikol.Extensions.MongoDB</c> extension uses, and reply notes from the same
/// <see cref="MongoDbResponseSummary"/>, so a command tapped on the wire renders with a byte-identical
/// <c>Method</c>, <c>Uri</c> and reply note.</para>
/// <para>What is deliberately <em>not</em> decoded: the legacy OP_QUERY/OP_REPLY handshake (recognised and
/// stepped over, recorded never) and OP_COMPRESSED (passed straight through, reported once). Replies with
/// <c>moreToCome</c> that answer no pending request — the streaming <c>hello</c> on a monitoring connection —
/// are skipped.</para>
/// <para>The tap never sees the connection string: it knows only <c>$db</c> and the collection, so the URI it
/// records (<c>mongodb:///db/coll</c>) can never contain a password.</para>
/// </remarks>
public sealed class MongoProtocolDecoder : IProtocolDecoder
{
    private static readonly HashSet<string> AlwaysExcluded = new(StringComparer.OrdinalIgnoreCase)
    {
        "saslStart", "saslContinue", "saslSupportedMechs", "authenticate", "getnonce",
        "copydbsaslstart", "copydbgetnonce", "createUser", "updateUser",
    };

    private readonly TcpTapConnectionContext _context;
    private readonly MongoTapOptions _options;
    private readonly WireBuffer _commands;
    private readonly WireBuffer _replies;
    private readonly Dictionary<int, PendingCommand> _pending = [];
    private readonly MongoDbTrackingVerbosity _verbosity;
    private const int MaxPendingCommands = 4096;

    private bool _compressionReported;
    private bool _legacyHandshakeReported;

    /// <summary>Creates a decoder for one connection.</summary>
    public MongoProtocolDecoder(TcpTapConnectionContext context, MongoTapOptions options)
    {
        _context = context ?? throw new ArgumentNullException(nameof(context));
        _options = options ?? throw new ArgumentNullException(nameof(options));
        _commands = new WireBuffer(options.MaxBufferedBytes);
        _replies = new WireBuffer(options.MaxBufferedBytes);
        _verbosity = TapVerbosityMap.ToMongo(options.Verbosity);
    }

    /// <summary>Commands seen but not yet answered.</summary>
    public int PendingCommands => _pending.Count;

    /// <summary>Whether this connection negotiated compression, so its commands cannot be decoded.</summary>
    public bool CompressionSeen => _compressionReported;

    /// <summary>Whether this connection opened with the legacy OP_QUERY handshake.</summary>
    public bool LegacyHandshakeSeen => _legacyHandshakeReported;

    /// <inheritdoc />
    public void OnClientToServer(ReadOnlySpan<byte> data, DateTimeOffset timestamp)
    {
        _commands.Append(data);
        while (true)
        {
            var span = _commands.Span;
            if (!MongoWireParser.TryReadHeader(span, out var header))
                return;
            var message = span[..header.MessageLength];
            OnCommandMessage(header, message, timestamp);
            _commands.Consume(header.MessageLength);
        }
    }

    /// <inheritdoc />
    public void OnServerToClient(ReadOnlySpan<byte> data, DateTimeOffset timestamp)
    {
        _replies.Append(data);
        while (true)
        {
            var span = _replies.Span;
            if (!MongoWireParser.TryReadHeader(span, out var header))
                return;
            var message = span[..header.MessageLength];
            OnReplyMessage(header, message, timestamp);
            _replies.Consume(header.MessageLength);
        }
    }

    /// <inheritdoc />
    public void OnConnectionClosed()
    {
        _pending.Clear();
        _commands.Clear();
        _replies.Clear();
    }

    /// <inheritdoc />
    public void Dispose() => OnConnectionClosed();

    private void OnCommandMessage(MongoMessageHeader header, ReadOnlySpan<byte> message, DateTimeOffset timestamp)
    {
        switch (header.OpCode)
        {
            case MongoOpCodes.OpMsg:
                break;

            case MongoOpCodes.OpQuery:
                ReportLegacyHandshake(message);
                return;

            case MongoOpCodes.OpCompressed:
                ReportCompression(message);
                return;

            default:
                // OP_GET_MORE / OP_KILL_CURSORS and anything else legacy: framed, stepped over, recorded never.
                return;
        }

        var parsed = MongoWireParser.ParseOpMsg(message);
        var command = parsed.Document;
        if (command.ElementCount == 0)
            return;

        var commandName = command.GetElement(0).Name;
        var databaseName = command.TryGetValue("$db", out var db) && db.IsString ? db.AsString : null;

        if (!ShouldRecord(commandName))
            return;

        var info = MongoDbOperationClassifier.Classify(commandName, databaseName, command);
        if (_verbosity == MongoDbTrackingVerbosity.Summarised && info.Operation == MongoDbOperation.Other)
            return;

        var pending = new PendingCommand(info, BuildUri(info), RequestContent(info, command), timestamp);

        if (parsed.MoreToCome)
        {
            // Fire-and-forget write (w:0): there will be no reply, so the arrow closes immediately.
            RecordPair(pending, null, timestamp);
            return;
        }

        _pending[header.RequestId] = pending;
        if (_pending.Count > MaxPendingCommands)
        {
            // A connection whose replies we never saw (dropped segments, a mid-stream join): forget the
            // backlog rather than grow without bound. Forwarding is untouched either way.
            _pending.Clear();
            _context.CountDecodeError();
            _context.Log($"more than {MaxPendingCommands} unanswered commands — dropping the pending map; some arrows will be missing.");
        }
    }

    private void OnReplyMessage(MongoMessageHeader header, ReadOnlySpan<byte> message, DateTimeOffset timestamp)
    {
        switch (header.OpCode)
        {
            case MongoOpCodes.OpMsg:
                break;

            case MongoOpCodes.OpCompressed:
                ReportCompression(message);
                return;

            case MongoOpCodes.OpReply:
                // Legacy handshake answer — nothing to record.
                return;

            default:
                return;
        }

        if (!_pending.Remove(header.ResponseTo, out var pending))
            return; // an excluded command's reply, or a streaming `hello` with moreToCome on a monitor connection

        var parsed = MongoWireParser.ParseOpMsg(message);
        RecordPair(pending, parsed.Document, timestamp);
    }

    private void RecordPair(PendingCommand pending, BsonDocument? reply, DateTimeOffset timestamp)
    {
        var label = MongoDbOperationClassifier.GetDiagramLabel(pending.Info, _verbosity);

        var failed = reply is not null &&
                     ((reply.TryGetValue("ok", out var ok) && ToDouble(ok) == 0d) || reply.Contains("errmsg"));

        string? responseContent;
        OneOf<HttpStatusCode, string>? status;
        if (failed)
        {
            responseContent = reply!.TryGetValue("errmsg", out var errmsg) ? errmsg.ToString() : "command failed";
            status = HttpStatusCode.InternalServerError;
        }
        else
        {
            responseContent = ResponseContent(reply);
            status = HttpStatusCode.OK;
        }

        _context.Record(new TapInteraction(
            label, label, pending.Uri,
            Redact(pending.RequestContent), Redact(responseContent),
            status, pending.Timestamp, timestamp));
    }

    private bool ShouldRecord(string commandName)
    {
        if (AlwaysExcluded.Contains(commandName))
            return false;
        if (_options.ExcludedCommands.Contains(commandName))
            return false;
        if (!_options.TrackGetMore && commandName.Equals("getMore", StringComparison.OrdinalIgnoreCase))
            return false;
        return true;
    }

    private string? RequestContent(MongoDbOperationInfo info, BsonDocument command) => _verbosity switch
    {
        MongoDbTrackingVerbosity.Summarised => null,
        MongoDbTrackingVerbosity.Raw => Stringify(command),
        _ => _options.LogFilterText ? info.FilterText : null,
    };

    private string? ResponseContent(BsonDocument? reply)
    {
        if (reply is null)
            return null;

        try
        {
            return _verbosity switch
            {
                MongoDbTrackingVerbosity.Summarised when !_options.LogResponseContent => null,
                MongoDbTrackingVerbosity.Raw => Stringify(reply),
                _ => MongoDbResponseSummary.ExtractDetailed(reply, _options.LogResponseContent, _options.MaxResponseDocuments),
            };
        }
        catch (Exception ex)
        {
            _context.CountDecodeError();
            return $"<reply could not be rendered: {ex.GetType().Name}>";
        }
    }

    private static string? Stringify(BsonDocument document)
    {
        try
        {
            return document.ToString();
        }
        catch (Exception ex)
        {
            return $"<document could not be rendered: {ex.GetType().Name}>";
        }
    }

    private string? Redact(string? text)
    {
        if (text is null || _options.DocumentRedaction is null)
            return text;
        try
        {
            return _options.DocumentRedaction(text) ?? text;
        }
        catch (Exception)
        {
            return "[REDACTION FAILED]";
        }
    }

    private static double ToDouble(BsonValue value) => value.BsonType switch
    {
        BsonType.Double => value.AsDouble,
        BsonType.Int32 => value.AsInt32,
        BsonType.Int64 => value.AsInt64,
        BsonType.Boolean => value.AsBoolean ? 1d : 0d,
        _ => 1d,
    };

    private Uri BuildUri(MongoDbOperationInfo info)
    {
        var database = info.DatabaseName ?? "unknown";
        var collection = info.CollectionName;

        var text = _verbosity == MongoDbTrackingVerbosity.Summarised || collection is null
            ? $"mongodb:///{database}"
            : $"mongodb:///{database}/{collection}";

        if (Uri.TryCreate(text, UriKind.Absolute, out var uri))
            return uri;

        _context.CountDecodeError();
        return new Uri($"mongodb:///{Uri.EscapeDataString(database)}");
    }

    private void ReportCompression(ReadOnlySpan<byte> message)
    {
        if (_compressionReported)
            return;
        _compressionReported = true;
        try
        {
            var (originalOpCode, uncompressedSize, compressorId) = MongoWireParser.ParseOpCompressedHeader(message);
            _context.Log(
                $"OP_COMPRESSED (compressor {compressorId}, opcode {originalOpCode}, {uncompressedSize} bytes uncompressed) — " +
                "passed through, not decoded. Remove `compressors=` from the client's connection string to capture these commands.");
        }
        catch (TapProtocolException)
        {
            _context.Log("OP_COMPRESSED — passed through, not decoded.");
        }
    }

    private void ReportLegacyHandshake(ReadOnlySpan<byte> message)
    {
        if (_legacyHandshakeReported)
            return;
        _legacyHandshakeReported = true;
        try
        {
            var (collection, query) = MongoWireParser.ParseOpQuery(message);
            var commandName = query.ElementCount > 0 ? query.GetElement(0).Name : "?";
            _context.Log($"legacy OP_QUERY handshake on {collection} ({commandName}) — passed through, recorded never.");
        }
        catch (TapProtocolException)
        {
            _context.Log("legacy OP_QUERY handshake — passed through, recorded never.");
        }
    }

    private readonly record struct PendingCommand(
        MongoDbOperationInfo Info,
        Uri Uri,
        string? RequestContent,
        DateTimeOffset Timestamp);
}
