// Shared source: also compiled into Kronikol.Extensions.TcpTap (and, for the Mongo files,
// Kronikol.Extensions.MongoDB.V2) as a linked file. The tap compiles it into its own namespace so a
// host can reference both packages; the in-process extension keeps this namespace unchanged.
#if KRONIKOL_TCPTAP_SHARED
namespace Kronikol.Extensions.TcpTap.Protocols;
#else
namespace Kronikol.Extensions.MongoDB;
#endif

/// <summary>
/// Controls how much detail the MongoDB tracking extension includes in diagram entries.
/// </summary>
public enum MongoDbTrackingVerbosity
{
    /// <summary>
    /// The whole command as the request note and the whole reply as the response note, both written as MongoDB
    /// relaxed extended JSON on one line; the label also carries the filter, in the same JSON.
    /// </summary>
    Raw,
    /// <summary>
    /// Classified labels with relevant context (e.g. operation name, target resource). The request note is the
    /// command without its name and without the driver's own fields (the session, the transaction, the read and write
    /// concerns, <c>$db</c>): a find's filter, sort and limit, an insert's documents, an update's or a delete's
    /// statements, a findAndModify's query and update, an aggregate's pipeline; an insert's documents and the statements
    /// of an update, a delete or a client bulkWrite are capped by <c>MaxRequestDocuments</c>. The response note is what
    /// the reply returned: a cursor's documents as a JSON array, or the reply without the server's bookkeeping as a JSON
    /// object (a write's counts and upserted ids, a findAndModify's
    /// value, a distinct's values, write errors, a failed command's errmsg, code and codeName), in relaxed extended
    /// JSON on one line.
    /// </summary>
    Detailed,
    /// <summary>
    /// Minimal labels (the operation's name) and no request note. The reply note is kept while <c>LogResponseContent</c>
    /// is on (the default), as at <see cref="Detailed"/>; off, a reply has no note and a failed command's note is the
    /// server's message alone. A command no rule classifies is not recorded.
    /// </summary>
    Summarised
}
