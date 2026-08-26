using System.Globalization;
using System.Net;
using Kronikol.Tracking;

namespace Kronikol.Extensions.TcpTap.Protocols;

/// <summary>
/// Decodes one tapped Redis connection: RESP2/RESP3 commands one way, replies the other, matched FIFO
/// (Redis answers a pipelined connection strictly in order).
/// </summary>
/// <remarks>
/// <para>Labels come from the same <see cref="RedisOperationClassifier"/> source file the in-process
/// <c>Kronikol.Extensions.Redis</c> extension uses, so a command tapped on the wire renders with a
/// byte-identical <c>Method</c> and <c>Uri</c>.</para>
/// <para>Both directions go through a <see cref="RespStreamParser"/>, so a value of any size is decoded without
/// ever being buffered whole: a bulk payload over <see cref="RedisTapOptions.MaxBulkBytes"/> is streamed past,
/// keeping a preview and the length on the wire, and the interaction is still recorded — a <c>GET</c> of a 10 MB
/// value is still a <c>Get (Hit)</c>. Memory per connection is bounded by the caps, not by the data.</para>
/// <para>Nothing from the handshake is ever recorded: <c>AUTH</c> and <c>HELLO</c> are hard-excluded (they
/// carry credentials), and the rest of the chatter a client generates on connect
/// (<c>CLIENT SETNAME</c>, <c>CONFIG GET</c>, <c>INFO</c>, <c>ECHO</c>, <c>PING</c>, <c>COMMAND</c>,
/// <c>CLUSTER</c>) is excluded by default. Excluded commands are still tracked in the pending queue, because
/// their replies still occupy a slot in the FIFO.</para>
/// <para>When the decoder loses its place (the held-bytes cap, a pending-queue overflow, a protocol error on a
/// connection that had been decoding fine) it throws a recoverable <see cref="TapProtocolException"/>; the tap
/// calls <see cref="Reset"/> and the decoder <em>resynchronises</em>: client bytes are discarded until a segment
/// starts with a command (<c>*&lt;n&gt;\r\n$</c> or an inline command letter), server bytes until a command is
/// pending again, and the first interaction recorded afterwards is stamped <c>[resynchronised — pairing uncertain]</c>.</para>
/// </remarks>
public sealed class RedisProtocolDecoder : IProtocolDecoder
{
    /// <summary>The note prefix on both arrows of the first interaction recorded after a decoder reset.</summary>
    public const string ResyncNotePrefix = "[resynchronised — pairing uncertain] ";

    /// <summary>The pseudo-header name stamped on the first interaction recorded after a decoder reset.</summary>
    public const string CaptureHeader = "x-kronikol-capture";

    /// <summary>The <see cref="CaptureHeader"/> value stamped on the first interaction recorded after a decoder reset.</summary>
    public const string CaptureResynced = "resynced";

    /// <summary>Resets with no value decoded in between before the decoder gives up resynchronising and lets the tap disable it.</summary>
    public const int MaxConsecutiveResyncs = 8;

    /// <summary>Characters of a <see cref="RespValue.TruncationMarker"/> the preview leaves room for under <see cref="TcpTapOptions.BodyCapBytes"/>, so the record-time cap never cuts the marker off.</summary>
    internal const int TruncationMarkerReserve = 128;

    private static readonly HashSet<string> AlwaysExcluded = new(StringComparer.OrdinalIgnoreCase)
    {
        "AUTH", "HELLO", "RESET",
    };

    /// <summary>Reply array heads that are deliveries, never answers to a command.</summary>
    private static readonly HashSet<string> PushHeads = new(StringComparer.OrdinalIgnoreCase)
    {
        "message", "pmessage", "smessage", "invalidate",
    };

    private static readonly HashSet<string> MultiKeyCommands = new(StringComparer.OrdinalIgnoreCase)
    {
        "MGET", "DEL", "UNLINK", "EXISTS", "TOUCH", "WATCH", "SUNION", "SINTER", "SDIFF",
    };

    private static readonly HashSet<string> KeyValuePairCommands = new(StringComparer.OrdinalIgnoreCase)
    {
        "MSET", "MSETNX",
    };

    private const int MaxPendingCommands = 4096;

    private readonly TcpTapConnectionContext _context;
    private readonly RedisTapOptions _options;
    private readonly RespStreamParser _commands;
    private readonly RespStreamParser _replies;
    private readonly Action<RespValue> _onCommandValue;
    private readonly Action<RespValue> _onReplyValue;
    private readonly Queue<PendingCommand> _pending = new();
    private long _oldestUnansweredTicks;
    private readonly RedisTrackingVerbosity _verbosity;
    private readonly string _endpoint;
    private int _database;
    private DateTimeOffset _timestamp;
    private bool _resyncingCommands;
    private bool _resyncingReplies;
    private bool _stampNext;
    private int _resyncsWithoutRecord;
    private bool _closed;

    /// <summary>Creates a decoder for one connection.</summary>
    public RedisProtocolDecoder(TcpTapConnectionContext context, RedisTapOptions options)
    {
        _context = context ?? throw new ArgumentNullException(nameof(context));
        _options = options ?? throw new ArgumentNullException(nameof(options));
        var maxBulk = options.EffectiveMaxBulkBytes;
        var preview = PreviewBytes(options.BodyCapBytes, maxBulk);
        _commands = new RespStreamParser(maxBulk, preview, options.MaxBufferedBytes, acceptInlineCommands: true, OnOversizePayload);
        _replies = new RespStreamParser(maxBulk, preview, options.MaxBufferedBytes, acceptInlineCommands: false, OnOversizePayload);
        _onCommandValue = OnCommandValue;
        _onReplyValue = OnReplyValue;
        _verbosity = TapVerbosityMap.ToRedis(options.Verbosity);
        _endpoint = $"{options.ForwardHost}:{options.ForwardPort}";
        _database = options.DefaultDatabase;
    }

    /// <summary>The database index this connection is currently on (tracked from <c>SELECT</c>).</summary>
    public int Database => _database;

    /// <summary>Commands seen but not yet answered.</summary>
    public int PendingCommands => _pending.Count;

    /// <inheritdoc />
    public DateTimeOffset? OldestUnansweredSince
    {
        get
        {
            var ticks = Interlocked.Read(ref _oldestUnansweredTicks);
            return ticks == 0 ? null : new DateTimeOffset(ticks, TimeSpan.Zero);
        }
    }

    /// <summary>Publishes the head of the FIFO for the reaper (only the decode task mutates the queue).</summary>
    private void PublishOldestUnanswered() =>
        Interlocked.Exchange(ref _oldestUnansweredTicks, _pending.Count == 0 ? 0 : _pending.Peek().Timestamp.UtcTicks);

    /// <summary>Bulk payloads streamed past on this connection (both directions).</summary>
    public int OversizePayloadsSkipped => _commands.OversizePayloadsSkipped + _replies.OversizePayloadsSkipped;

    /// <summary>Bytes of oversize payloads not kept on this connection (both directions).</summary>
    public long BytesSkipped => _commands.BytesSkipped + _replies.BytesSkipped;

    /// <summary>Whether the decoder is waiting for the next command boundary after a reset.</summary>
    public bool IsResynchronising => _resyncingCommands || _resyncingReplies;

    /// <summary>
    /// How many leading bytes of a streamed-past payload are kept: the record-time cap less room for the truncation
    /// marker (so the cap never cuts the marker off), or the bulk cap itself when content is unlimited.
    /// </summary>
    internal static int PreviewBytes(int? bodyCapBytes, int maxBulkBytes)
    {
        if (bodyCapBytes is not { } cap)
            return maxBulkBytes;
        var preview = cap >= 4 * TruncationMarkerReserve ? cap - TruncationMarkerReserve : cap;
        return Math.Min(preview, maxBulkBytes);
    }

    /// <inheritdoc />
    public void OnClientToServer(ReadOnlySpan<byte> data, DateTimeOffset timestamp)
    {
        if (_resyncingCommands)
        {
            if (!StartsAtCommandBoundary(data, acceptInline: _commands.InlineCommands > 0))
                return; // still inside whatever the client was sending when we lost our place
            _resyncingCommands = false;
        }

        _timestamp = timestamp;
        try
        {
            _commands.Feed(data, _onCommandValue);
        }
        catch (RespProtocolException ex) when (HasDecodedBefore)
        {
            throw Desynchronised(ex);
        }
    }

    /// <inheritdoc />
    public void OnServerToClient(ReadOnlySpan<byte> data, DateTimeOffset timestamp)
    {
        if (_resyncingReplies)
        {
            if (_pending.Count == 0)
                return; // replies to commands we never saw — out-of-band until the client speaks again
            _resyncingReplies = false;
        }

        _timestamp = timestamp;
        try
        {
            _replies.Feed(data, _onReplyValue);
        }
        catch (RespProtocolException ex) when (HasDecodedBefore)
        {
            throw Desynchronised(ex);
        }
    }

    /// <inheritdoc />
    public void OnConnectionClosed()
    {
        if (_closed)
            return;
        _closed = true;

        var unanswered = _pending.Count;
        var partialCommand = !_commands.IsAtValueBoundary;
        var partialReply = !_replies.IsAtValueBoundary;
        if (unanswered > 0 || partialCommand || partialReply)
        {
            var detail = $"{unanswered} unanswered command(s)";
            if (partialCommand)
                detail += $", a partial command ({_commands.HeldBytes} B held{(_commands.IsStreamingPast ? ", streaming past an oversize payload" : "")})";
            if (partialReply)
                detail += $", a partial reply ({_replies.HeldBytes} B held{(_replies.IsStreamingPast ? ", streaming past an oversize payload" : "")})";
            _context.ReportClosedMidMessage(detail);
        }

        Clear();
    }

    /// <inheritdoc />
    public void Dispose()
    {
        _closed = true;
        Clear();
    }

    /// <summary>
    /// Drops both parsers' partial state and the pending queue, then resynchronises: client bytes are discarded
    /// until a segment starts with a command, server bytes until a command is pending again, and the first
    /// interaction recorded afterwards is stamped <see cref="ResyncNotePrefix"/> / <c>x-kronikol-capture: resynced</c>.
    /// Called by the tap after a recoverable protocol error (see <see cref="TcpTapOptions.ResyncAfterOverflow"/>).
    /// </summary>
    public void Reset()
    {
        _commands.Reset();
        _replies.Reset();
        _pending.Clear();
        PublishOldestUnanswered();
        _resyncingCommands = true;
        _resyncingReplies = true;
        _stampNext = true;
    }

    bool IProtocolDecoder.TryReset()
    {
        if (++_resyncsWithoutRecord > MaxConsecutiveResyncs)
        {
            _context.Log($"{MaxConsecutiveResyncs} resets without recording a single interaction in between — giving up on this connection.");
            return false;
        }

        Reset();
        return true;
    }

    private bool HasDecodedBefore => _commands.ValuesCompleted + _replies.ValuesCompleted > 0;

    private RespProtocolException Desynchronised(RespProtocolException inner) =>
        new($"{inner.Message} — after {_commands.ValuesCompleted + _replies.ValuesCompleted} values on this connection, so the stream is desynchronised rather than not RESP")
        {
            Recoverable = true,
        };

    /// <summary>
    /// A segment that starts with <c>*&lt;digits&gt;\r\n$</c> — a RESP command — or, when <paramref name="acceptInline"/>
    /// (the connection has sent inline commands before), with a letter. Payload fragments left over after a desync are
    /// JSON, binary or text, which is why a letter alone is not trusted on a connection that speaks RESP arrays.
    /// </summary>
    internal static bool StartsAtCommandBoundary(ReadOnlySpan<byte> data, bool acceptInline)
    {
        if (data.Length == 0)
            return false;
        var first = data[0];
        if (first is >= (byte)'A' and <= (byte)'Z' or >= (byte)'a' and <= (byte)'z')
            return acceptInline;
        if (first != (byte)'*')
            return false;
        var i = 1;
        while (i < data.Length && data[i] is >= (byte)'0' and <= (byte)'9')
            i++;
        return i > 1 && i + 2 < data.Length && data[i] == (byte)'\r' && data[i + 1] == (byte)'\n' && data[i + 2] == (byte)'$';
    }

    private void Clear()
    {
        _pending.Clear();
        PublishOldestUnanswered();
        _commands.Reset();
        _replies.Reset();
    }

    private void OnOversizePayload(long declaredLength, int keptBytes) =>
        _context.CountOversizePayload(declaredLength, keptBytes);

    private void OnCommandValue(RespValue value)
    {
        var arguments = value.Type switch
        {
            RespType.Array => (value.Items ?? []).Select(i => i.AsText() ?? string.Empty).ToArray(),
            RespType.Null => [],
            RespType.BulkString => [value.AsText() ?? string.Empty],
            _ => throw new RespProtocolException($"A client command must be an array of bulk strings, got {value.Type}."),
        };
        if (arguments.Length > 0)
            OnCommand(arguments, _timestamp);
    }

    private void OnReplyValue(RespValue value) => OnReply(value, _timestamp);

    private void OnCommand(string[] arguments, DateTimeOffset timestamp)
    {
        var verb = arguments[0].ToUpperInvariant();

        int? selectTarget = null;
        if (verb == "SELECT" && arguments.Length > 1 &&
            int.TryParse(arguments[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out var target))
            selectTarget = target;

        var record = !AlwaysExcluded.Contains(verb) && !_options.ExcludedCommands.Contains(verb);
        if (record && !_options.CapturePubSub && IsPubSub(verb))
            record = false;

        var key = record ? ExtractKey(verb, arguments) : null;
        if (record && key is not null && _options.ExcludedKeyPrefixes.Any(prefix => key.StartsWith(prefix, StringComparison.Ordinal)))
        {
            record = false;
            key = null;
        }

        // Excluded commands still take a slot: their reply is in the same FIFO.
        _pending.Enqueue(record
            ? new PendingCommand(verb, key, ExtractRequestContent(verb, arguments), _database, timestamp, true, selectTarget)
            : new PendingCommand(verb, null, null, _database, timestamp, false, selectTarget));
        PublishOldestUnanswered();

        if (_pending.Count > MaxPendingCommands)
        {
            // A connection whose replies we never saw (dropped segments, a desynchronised reply stream): the
            // queue is worthless. Recoverable — the tap resets us and we re-arm at the next command boundary.
            throw new TapProtocolException(
                $"More than {MaxPendingCommands} unanswered commands — replies are not being decoded; dropping the pending queue.",
                recoverable: true);
        }
    }

    private void OnReply(RespValue reply, DateTimeOffset timestamp)
    {
        // RESP3 pushes and RESP2 pub/sub deliveries are unsolicited — they answer nothing.
        if (reply.Type == RespType.Push)
            return;
        if (reply.Type == RespType.Array && reply.Items is { Count: > 0 } items &&
            items[0].AsText() is { } head && PushHeads.Contains(head))
            return;

        if (_pending.Count == 0)
            return; // out-of-band traffic on a connection we joined mid-stream

        var pending = _pending.Dequeue();
        PublishOldestUnanswered();

        if (pending.SelectTarget is { } database && !reply.IsError)
            _database = database;

        if (!pending.Record)
            return;

        Record(pending, reply, timestamp);
    }

    private void Record(PendingCommand pending, RespValue reply, DateTimeOffset timestamp)
    {
        var key = pending.Key is null ? null : Redact(_options.KeyRedaction, pending.Key);
        var operation = RedisOperationClassifier.Classify(pending.Verb, reply.HasResult(), key, pending.Database);

        // Same early-out as the in-process tracker: unclassified commands vanish at Summarised.
        if (_verbosity == RedisTrackingVerbosity.Summarised && operation.Operation == RedisOperation.Other)
            return;

        var requestOperation = operation with { CacheResult = RedisCacheResult.None };
        var requestLabel = RedisOperationClassifier.GetDiagramLabel(requestOperation, _verbosity) ?? requestOperation.Operation.ToString();
        var responseLabel = RedisOperationClassifier.GetDiagramLabel(operation, _verbosity) ?? operation.Operation.ToString();

        OneOf<HttpMethod, string> requestMethod = _verbosity == RedisTrackingVerbosity.Raw ? pending.Verb : requestLabel;
        OneOf<HttpMethod, string> responseMethod = _verbosity == RedisTrackingVerbosity.Raw ? pending.Verb : responseLabel;

        var uri = BuildUri(key, pending.Database);

        var requestContent = _verbosity == RedisTrackingVerbosity.Summarised || pending.RequestContent is null
            ? null
            : Redact(_options.ValueRedaction, pending.RequestContent);

        string? responseContent;
        OneOf<HttpStatusCode, string>? status;
        if (reply.IsError)
        {
            responseContent = reply.AsText();
            status = HttpStatusCode.InternalServerError;
        }
        else
        {
            responseContent = _verbosity == RedisTrackingVerbosity.Summarised ? null : reply.Render();
            status = "OK";
        }

        if (responseContent is not null)
            responseContent = Redact(_options.ValueRedaction, responseContent);

        (string Key, string? Value)[]? headers = null;
        _resyncsWithoutRecord = 0;
        if (_stampNext)
        {
            // The first interaction after a reset: a reply still in flight may belong to a command we discarded.
            _stampNext = false;
            requestContent = requestContent is null ? ResyncNotePrefix.TrimEnd() : ResyncNotePrefix + requestContent;
            responseContent = responseContent is null ? ResyncNotePrefix.TrimEnd() : ResyncNotePrefix + responseContent;
            headers = [(CaptureHeader, CaptureResynced)];
        }

        _context.Record(new TapInteraction(
            responseMethod, requestMethod, uri, requestContent, responseContent, status, pending.Timestamp, timestamp)
        {
            Headers = headers,
        });
    }

    private Uri BuildUri(string? key, int database)
    {
        var text = _verbosity switch
        {
            RedisTrackingVerbosity.Raw => key is not null ? $"redis://{_endpoint}/{database}/{key}" : $"redis://{_endpoint}/{database}",
            RedisTrackingVerbosity.Detailed => key is not null ? $"redis://db{database}/{key}" : $"redis://db{database}/",
            _ => $"redis://db{database}/",
        };

        if (Uri.TryCreate(text, UriKind.Absolute, out var uri))
            return uri;

        // A key with characters no URI allows (spaces, control bytes) must not take the connection down.
        _context.CountDecodeError();
        var escaped = key is null ? null : Uri.EscapeDataString(key);
        var fallback = _verbosity switch
        {
            RedisTrackingVerbosity.Raw => escaped is not null ? $"redis://{_endpoint}/{database}/{escaped}" : $"redis://{_endpoint}/{database}",
            RedisTrackingVerbosity.Detailed => escaped is not null ? $"redis://db{database}/{escaped}" : $"redis://db{database}/",
            _ => $"redis://db{database}/",
        };
        return Uri.TryCreate(fallback, UriKind.Absolute, out var safe) ? safe : new Uri($"redis://db{database}/");
    }

    private static string Redact(Func<string, string>? hook, string value)
    {
        if (hook is null)
            return value;
        try
        {
            return hook(value) ?? value;
        }
        catch (Exception)
        {
            return "[REDACTION FAILED]";
        }
    }

    private static bool IsPubSub(string verb) => verb is "PUBLISH" or "SPUBLISH" or "SUBSCRIBE" or "PSUBSCRIBE"
        or "SSUBSCRIBE" or "UNSUBSCRIBE" or "PUNSUBSCRIBE" or "SUNSUBSCRIBE";

    /// <summary>
    /// The key (or keys) a command targets. Multi-key commands join every key with <c>,</c> — exactly what the
    /// in-process extension does for a <c>RedisKey[]</c> argument, so <c>MGET a b</c> and
    /// <c>KeyDelete([a, b])</c> produce the same <c>redis://db0/a,b</c>.
    /// </summary>
    internal static string? ExtractKey(string verb, string[] arguments)
    {
        if (arguments.Length < 2)
            return null;

        if (MultiKeyCommands.Contains(verb))
            return string.Join(",", arguments.Skip(1));

        if (KeyValuePairCommands.Contains(verb))
        {
            var keys = new List<string>();
            for (var i = 1; i < arguments.Length; i += 2)
                keys.Add(arguments[i]);
            return keys.Count > 0 ? string.Join(",", keys) : null;
        }

        // Everything else addresses one thing by its first argument — a key, a channel, a script or a cursor.
        return arguments[1];
    }

    /// <summary>
    /// Note text for the request arrow, matching the in-process extension: the value for a string write, the
    /// <c>field=value</c> pair for a hash write, the message for a publish, and nothing for a read.
    /// </summary>
    internal static string? ExtractRequestContent(string verb, string[] arguments)
    {
        switch (verb)
        {
            case "SET" or "SETNX" or "GETSET" or "APPEND" or "SETRANGE" when arguments.Length >= 3:
                return arguments[2];
            case "SETEX" or "PSETEX" when arguments.Length >= 4:
                return arguments[3];
            case "HSET" or "HMSET" or "HSETNX" when arguments.Length >= 4:
            {
                var pairs = new List<string>();
                for (var i = 2; i + 1 < arguments.Length; i += 2)
                    pairs.Add($"{arguments[i]}={arguments[i + 1]}");
                return pairs.Count > 0 ? string.Join(", ", pairs) : null;
            }
            case "PUBLISH" or "SPUBLISH" when arguments.Length >= 3:
                return arguments[2];
            case "MSET" or "MSETNX" when arguments.Length >= 3:
            {
                var pairs = new List<string>();
                for (var i = 1; i + 1 < arguments.Length; i += 2)
                    pairs.Add($"{arguments[i]}={arguments[i + 1]}");
                return pairs.Count > 0 ? string.Join(", ", pairs) : null;
            }
            case "LPUSH" or "RPUSH" or "LPUSHX" or "RPUSHX" or "SADD" when arguments.Length >= 3:
                return string.Join(", ", arguments.Skip(2));
            default:
                return null;
        }
    }

    private readonly record struct PendingCommand(
        string Verb,
        string? Key,
        string? RequestContent,
        int Database,
        DateTimeOffset Timestamp,
        bool Record,
        int? SelectTarget);
}

/// <summary>Maps the tap's <see cref="TapVerbosity"/> onto the extension verbosity enums the classifiers take.</summary>
internal static class TapVerbosityMap
{
    public static RedisTrackingVerbosity ToRedis(TapVerbosity verbosity) => verbosity switch
    {
        TapVerbosity.Raw => RedisTrackingVerbosity.Raw,
        TapVerbosity.Summarised => RedisTrackingVerbosity.Summarised,
        _ => RedisTrackingVerbosity.Detailed,
    };

    public static MongoDbTrackingVerbosity ToMongo(TapVerbosity verbosity) => verbosity switch
    {
        TapVerbosity.Raw => MongoDbTrackingVerbosity.Raw,
        TapVerbosity.Summarised => MongoDbTrackingVerbosity.Summarised,
        _ => MongoDbTrackingVerbosity.Detailed,
    };
}
