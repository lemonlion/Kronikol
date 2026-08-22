using Kronikol.Constants;
using Kronikol.Extensions.TcpTap.Protocols;

namespace Kronikol.Extensions.TcpTap;

/// <summary>
/// Options for a <see cref="MongoTap"/> — a <see cref="TcpTap"/> that decodes the MongoDB wire protocol and
/// records the commands it sees the way <c>Kronikol.Extensions.MongoDB</c> records them in-process.
/// </summary>
public sealed class MongoTapOptions : TcpTapOptions
{
    /// <summary>Creates the options with the MongoDB defaults (service name <c>mongo</c>, MongoDB dependency category, OP_MSG decoder).</summary>
    public MongoTapOptions()
    {
        ServiceName = "mongo";
        ForwardPort = 27017;
        DependencyCategory = DependencyCategories.MongoDB;
        DecoderFactory = context => new MongoProtocolDecoder(context, (MongoTapOptions)context.Options);
    }

    /// <summary>
    /// Command names never recorded (case-insensitive). Defaults to authentication and the topology chatter
    /// every driver generates. Matches the in-process extension's <c>IgnoredCommands</c>, plus the SCRAM and
    /// legacy-auth commands, so a credential can never reach a sink.
    /// </summary>
    /// <remarks>
    /// The authentication family (<c>saslStart</c>, <c>saslContinue</c>, <c>authenticate</c>, <c>getnonce</c>,
    /// <c>copydbsaslstart</c>, <c>createUser</c>, <c>updateUser</c>) is hard-excluded by
    /// <see cref="MongoProtocolDecoder"/> whatever this set contains.
    /// </remarks>
    public HashSet<string> ExcludedCommands { get; } = new(StringComparer.OrdinalIgnoreCase)
    {
        "hello", "isMaster", "ismaster", "saslStart", "saslContinue", "saslSupportedMechs",
        "ping", "buildInfo", "getParameter", "getLastError", "killCursors", "endSessions",
        "logout", "authenticate", "getnonce", "whatsmyuri", "connectionStatus",
    };

    /// <summary>Whether cursor continuations (<c>getMore</c>) are recorded. Default false — they add noise, as in the in-process extension.</summary>
    public bool TrackGetMore { get; set; }

    /// <summary>Whether the command's <c>filter</c> is included as the request note at <see cref="TapVerbosity.Detailed"/>. Default true.</summary>
    public bool LogFilterText { get; set; } = true;

    /// <summary>Whether reply documents (from <c>cursor.firstBatch</c>) are included in the response note. Default true.</summary>
    public bool LogResponseContent { get; set; } = true;

    /// <summary>Maximum number of documents from <c>cursor.firstBatch</c> in the response note. Default 10.</summary>
    public int MaxResponseDocuments { get; set; } = 10;

    /// <summary>Optional hook applied to every command and reply document text before it is recorded (filters, documents, reply batches).</summary>
    public Func<string, string>? DocumentRedaction { get; set; }

    /// <inheritdoc />
    public override void CopyTo(TcpTapOptions target)
    {
        base.CopyTo(target);
        if (target is not MongoTapOptions mongo)
            return;
        mongo.ExcludedCommands.Clear();
        mongo.ExcludedCommands.UnionWith(ExcludedCommands);
        mongo.TrackGetMore = TrackGetMore;
        mongo.LogFilterText = LogFilterText;
        mongo.LogResponseContent = LogResponseContent;
        mongo.MaxResponseDocuments = MaxResponseDocuments;
        mongo.DocumentRedaction = DocumentRedaction;
    }
}
