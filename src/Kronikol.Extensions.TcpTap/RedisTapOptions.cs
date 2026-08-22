using Kronikol.Constants;
using Kronikol.Extensions.TcpTap.Protocols;

namespace Kronikol.Extensions.TcpTap;

/// <summary>
/// Options for a <see cref="RedisTap"/> — a <see cref="TcpTap"/> that decodes RESP2/RESP3 and records the
/// commands it sees the way <c>Kronikol.Extensions.Redis</c> records them in-process.
/// </summary>
public sealed class RedisTapOptions : TcpTapOptions
{
    /// <summary>Creates the options with the Redis defaults (service name <c>redis</c>, Redis dependency category, RESP decoder).</summary>
    public RedisTapOptions()
    {
        ServiceName = "redis";
        ForwardPort = 6379;
        DependencyCategory = DependencyCategories.Redis;
        DecoderFactory = context => new RedisProtocolDecoder(context, (RedisTapOptions)context.Options);
    }

    /// <summary>
    /// Command verbs never recorded (case-insensitive). Defaults to the connection handshake and keep-alive
    /// chatter every client generates: <c>PING, CLIENT, CONFIG, INFO, ECHO, HELLO, SELECT, AUTH, COMMAND,
    /// CLUSTER</c>, plus <c>SENTINEL</c> and the subscription-management verbs, which StackExchange.Redis
    /// sends while opening its connections. <c>SELECT</c> is still <em>tracked</em> so the database index in
    /// the URI stays right; <c>PUBLISH</c> is deliberately <em>not</em> excluded, because publishing is
    /// something the application did.
    /// </summary>
    /// <remarks>
    /// This is the capture-time security boundary, not a render filter: <see cref="RedisProtocolDecoder"/>
    /// additionally hard-excludes <c>AUTH</c> and <c>HELLO</c> (which carry credentials) whatever this contains.
    /// </remarks>
    public HashSet<string> ExcludedCommands { get; } = new(StringComparer.OrdinalIgnoreCase)
    {
        "PING", "CLIENT", "CONFIG", "INFO", "ECHO", "HELLO", "SELECT", "AUTH", "COMMAND", "CLUSTER", "SENTINEL",
        "SUBSCRIBE", "UNSUBSCRIBE", "PSUBSCRIBE", "PUNSUBSCRIBE", "SSUBSCRIBE", "SUNSUBSCRIBE", "QUIT",
    };

    /// <summary>
    /// Keys whose name starts with one of these (case-sensitive) are never recorded. Defaults to the client
    /// libraries' own bookkeeping keys — StackExchange.Redis probes <c>__Booksleeve_TieBreak</c> on every
    /// connection, which is a real <c>GET</c> but not something the application did.
    /// </summary>
    public HashSet<string> ExcludedKeyPrefixes { get; } = ["__Booksleeve_", "__redis__"];

    /// <summary>The database index a fresh connection starts on, before any <c>SELECT</c>. Default 0.</summary>
    public int DefaultDatabase { get; set; }

    /// <summary>
    /// Whether <c>PUBLISH</c> is recorded — publishing is something the application did, so it is on by
    /// default. Subscription management is in <see cref="ExcludedCommands"/> and delivered messages are
    /// unsolicited, so neither is recorded either way. Default true.
    /// </summary>
    public bool CapturePubSub { get; set; } = true;

    /// <summary>
    /// A bulk payload (a value being <c>SET</c>, a <c>GET</c> reply, any element of an aggregate) longer than this is
    /// never buffered: the decoder streams it past, keeping the first bytes as a preview and the length on the wire,
    /// and the interaction is still recorded — a <c>GET</c> of a 10 MB value is still a <c>Get (Hit)</c>, its note the
    /// preview followed by <c> …[bulk string truncated: N bytes on the wire, K kept]</c>. Null (the default) means
    /// <see cref="TcpTapOptions.BodyCapBytes"/>, or <see cref="TcpTapOptions.MaxBufferedBytes"/> when that is unlimited —
    /// nothing beyond the record-time cap is kept anyway, so buffering it buys nothing. Never more than
    /// <see cref="TcpTapOptions.MaxBufferedBytes"/>.
    /// </summary>
    public int? MaxBulkBytes { get; set; }

    /// <summary>The bulk-payload cap in force: <see cref="MaxBulkBytes"/>, else <see cref="TcpTapOptions.BodyCapBytes"/>, else <see cref="TcpTapOptions.MaxBufferedBytes"/>; never above <see cref="TcpTapOptions.MaxBufferedBytes"/>.</summary>
    public int EffectiveMaxBulkBytes => Math.Min(MaxBulkBytes ?? BodyCapBytes ?? MaxBufferedBytes, MaxBufferedBytes);

    /// <inheritdoc />
    public override void Validate()
    {
        base.Validate();
        if (MaxBulkBytes is < 0)
            throw new ArgumentOutOfRangeException(nameof(MaxBulkBytes), "MaxBulkBytes must be zero or positive, or null for the default.");
    }

    /// <inheritdoc />
    public override void CopyTo(TcpTapOptions target)
    {
        base.CopyTo(target);
        if (target is not RedisTapOptions redis)
            return;
        redis.ExcludedCommands.Clear();
        redis.ExcludedCommands.UnionWith(ExcludedCommands);
        redis.ExcludedKeyPrefixes.Clear();
        redis.ExcludedKeyPrefixes.UnionWith(ExcludedKeyPrefixes);
        redis.DefaultDatabase = DefaultDatabase;
        redis.CapturePubSub = CapturePubSub;
        redis.MaxBulkBytes = MaxBulkBytes;
    }
}
