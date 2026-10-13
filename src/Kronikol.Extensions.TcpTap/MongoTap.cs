namespace Kronikol.Extensions.TcpTap;

/// <summary>
/// A <see cref="TcpTap"/> that speaks the MongoDB wire protocol: point a service's Mongo client at it instead
/// of mongod, and every command renders as a <c>service → mongo</c> arrow — <c>Find ← Trial</c>,
/// <c>Insert → Trial</c>, <c>Aggregate ← x (match, group)</c> — with <c>mongodb:///{db}/{collection}</c> as the
/// URI, the filter as the request note and what the reply returned as the response note (its documents, or its own
/// fields without the server's bookkeeping, in relaxed extended JSON), exactly as the in-process
/// <c>Kronikol.Extensions.MongoDB</c> extension would render it. A reply with <c>ok: 0</c>, <c>writeErrors</c> or a
/// <c>writeConcernError</c> is drawn as a failure (500) and carries the server's message as its error.
/// </summary>
/// <remarks>
/// Authentication (<c>saslStart</c>/<c>saslContinue</c>) and topology chatter (<c>hello</c>, <c>isMaster</c>,
/// <c>ping</c>, <c>buildInfo</c>, <c>getParameter</c>, <c>endSessions</c>) are dropped in the decoder. The tap
/// only ever knows <c>$db</c> and the collection, so the URI it records can never carry the connection
/// string's password.
/// </remarks>
public sealed class MongoTap : TcpTap
{
    /// <summary>Creates a MongoDB tap for the given options.</summary>
    public MongoTap(MongoTapOptions options) : base(options) => Options = options;

    /// <summary>The options this tap runs with.</summary>
    public new MongoTapOptions Options { get; }
}
