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
    /// <c>copydbsaslstart</c>, <c>createUser</c>, <c>updateUser</c>), and a handshake (<c>hello</c>, <c>isMaster</c>)
    /// that starts an authentication (<c>speculativeAuthenticate</c>) or names its user (<c>saslSupportedMechs</c>), are
    /// hard-excluded by <see cref="MongoProtocolDecoder"/> whatever this set contains.
    /// </remarks>
    public HashSet<string> ExcludedCommands { get; } = new(StringComparer.OrdinalIgnoreCase)
    {
        "hello", "isMaster", "ismaster", "saslStart", "saslContinue", "saslSupportedMechs",
        "ping", "buildInfo", "getParameter", "getLastError", "killCursors", "endSessions",
        "logout", "authenticate", "getnonce", "whatsmyuri", "connectionStatus",
    };

    /// <summary>Whether cursor continuations (<c>getMore</c>) are recorded. Default false — they add noise, as in the in-process extension.</summary>
    public bool TrackGetMore { get; set; }

    /// <summary>Whether the request note is recorded at <see cref="TapVerbosity.Detailed"/>: the command without its name and the driver's own fields, as relaxed extended JSON (an insert's documents included). Default true.</summary>
    public bool LogFilterText { get; set; } = true;

    /// <summary>
    /// Whether the response note holds the documents the reply returned: a cursor's batch (<c>firstBatch</c>, or a
    /// getMore's <c>nextBatch</c>), a findAndModify's <c>value</c> and a distinct's <c>values</c>. Off, the note keeps
    /// the rest of the reply (a write's counts, upserted ids, write errors, and a client bulkWrite's failed operations),
    /// and at <see cref="TapVerbosity.Summarised"/> a reply has no note and a failed command's note is the server's
    /// message alone. Default true.
    /// </summary>
    public bool LogResponseContent { get; set; } = true;

    /// <summary>
    /// The most documents a response note keeps from a cursor's batch; a note that left some out ends with
    /// <c>... (N more documents not shown)</c>. A distinct's <c>values</c>, and a write's <c>writeErrors</c> and
    /// <c>upserted</c>, keep as many entries, and a note that left some of those out ends with <c>... (N more)</c>.
    /// Default 10.
    /// </summary>
    public int MaxResponseDocuments { get; set; } = 10;

    /// <summary>
    /// The most documents or statements a request note keeps (an insert's <c>documents</c>, an update's
    /// <c>updates</c>, a delete's <c>deletes</c>, a client bulkWrite's <c>ops</c>); a note that left some out ends with
    /// <c>... (N more documents not shown)</c>, or <c>... (N more)</c> for statements. Default 10.
    /// </summary>
    public int MaxRequestDocuments { get; set; } = 10;

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
        mongo.MaxRequestDocuments = MaxRequestDocuments;
        mongo.DocumentRedaction = DocumentRedaction;
    }
}
