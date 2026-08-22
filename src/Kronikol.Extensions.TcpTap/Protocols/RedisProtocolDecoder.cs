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
/// <para>Nothing from the handshake is ever recorded: <c>AUTH</c> and <c>HELLO</c> are hard-excluded (they
/// carry credentials), and the rest of the chatter a client generates on connect
/// (<c>CLIENT SETNAME</c>, <c>CONFIG GET</c>, <c>INFO</c>, <c>ECHO</c>, <c>PING</c>, <c>COMMAND</c>,
/// <c>CLUSTER</c>) is excluded by default. Excluded commands are still tracked in the pending queue, because
/// their replies still occupy a slot in the FIFO.</para>
/// </remarks>
public sealed class RedisProtocolDecoder : IProtocolDecoder
{
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
    private readonly WireBuffer _commands;
    private readonly WireBuffer _replies;
    private readonly Queue<PendingCommand> _pending = new();
    private readonly RedisTrackingVerbosity _verbosity;
    private readonly string _endpoint;
    private int _database;

    /// <summary>Creates a decoder for one connection.</summary>
    public RedisProtocolDecoder(TcpTapConnectionContext context, RedisTapOptions options)
    {
        _context = context ?? throw new ArgumentNullException(nameof(context));
        _options = options ?? throw new ArgumentNullException(nameof(options));
        _commands = new WireBuffer(options.MaxBufferedBytes);
        _replies = new WireBuffer(options.MaxBufferedBytes);
        _verbosity = TapVerbosityMap.ToRedis(options.Verbosity);
        _endpoint = $"{options.ForwardHost}:{options.ForwardPort}";
        _database = options.DefaultDatabase;
    }

    /// <summary>The database index this connection is currently on (tracked from <c>SELECT</c>).</summary>
    public int Database => _database;

    /// <summary>Commands seen but not yet answered.</summary>
    public int PendingCommands => _pending.Count;

    /// <inheritdoc />
    public void OnClientToServer(ReadOnlySpan<byte> data, DateTimeOffset timestamp)
    {
        _commands.Append(data);
        while (RespParser.TryParseCommand(_commands.Span, out var arguments, out var consumed))
        {
            _commands.Consume(consumed);
            if (arguments is { Length: > 0 })
                OnCommand(arguments, timestamp);
        }
    }

    /// <inheritdoc />
    public void OnServerToClient(ReadOnlySpan<byte> data, DateTimeOffset timestamp)
    {
        _replies.Append(data);
        while (RespParser.TryParse(_replies.Span, out var value, out var consumed))
        {
            _replies.Consume(consumed);
            if (value is not null)
                OnReply(value, timestamp);
        }
    }

    /// <inheritdoc />
    public void OnConnectionClosed()
    {
        // Commands still unanswered when the socket closed produced no arrow; drop them.
        _pending.Clear();
        _commands.Clear();
        _replies.Clear();
    }

    /// <inheritdoc />
    public void Dispose() => OnConnectionClosed();

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

        if (_pending.Count > MaxPendingCommands)
        {
            // A connection whose replies we never saw (dropped segments, a mid-stream join): forget the
            // backlog rather than grow without bound. Forwarding is untouched either way.
            _pending.Clear();
            _context.CountDecodeError();
            _context.Log($"more than {MaxPendingCommands} unanswered commands — dropping the pending queue; some arrows will be missing.");
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

        _context.Record(new TapInteraction(
            responseMethod, requestMethod, uri, requestContent, responseContent, status, pending.Timestamp, timestamp));
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
