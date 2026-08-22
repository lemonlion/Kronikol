namespace Kronikol.Extensions.TcpTap;

/// <summary>
/// A <see cref="TcpTap"/> that speaks the MongoDB wire protocol: point a service's Mongo client at it instead
/// of mongod, and every command renders as a <c>service → mongo</c> arrow — <c>Find ← Trial</c>,
/// <c>Insert → Trial</c>, <c>Aggregate ← x (match, group)</c> — with <c>mongodb:///{db}/{collection}</c> as the
/// URI, the filter as the request note and the reply documents as the response note, exactly as the in-process
/// <c>Kronikol.Extensions.MongoDB</c> extension would render it.
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
