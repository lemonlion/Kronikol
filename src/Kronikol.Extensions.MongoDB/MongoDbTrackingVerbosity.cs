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
    /// The whole command as the request note and the whole reply as the response note, the reply written as MongoDB
    /// relaxed extended JSON on one line; the label also carries the filter.
    /// </summary>
    Raw,
    /// <summary>
    /// Classified labels with relevant context (e.g. operation name, target resource). The request note is the
    /// command's filter. The response note is what the reply returned: a cursor's documents as a JSON array, or the
    /// reply without the server's bookkeeping as a JSON object (a write's counts and upserted ids, a findAndModify's
    /// value, a distinct's values, write errors, a failed command's errmsg, code and codeName), in relaxed extended
    /// JSON on one line.
    /// </summary>
    Detailed,
    /// <summary>Minimal labels only — content and connection details are omitted.</summary>
    Summarised
}
