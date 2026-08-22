// Shared source: also compiled into Kronikol.Extensions.TcpTap (and, for the Mongo files,
// Kronikol.Extensions.MongoDB.V2) as a linked file. The tap compiles it into its own namespace so a
// host can reference both packages; the in-process extension keeps this namespace unchanged.
#if KRONIKOL_TCPTAP_SHARED
namespace Kronikol.Extensions.TcpTap.Protocols;
#else
namespace Kronikol.Extensions.Redis;
#endif

/// <summary>
/// Controls how much detail the Redis tracking extension includes in diagram entries.
/// </summary>
public enum RedisTrackingVerbosity
{
    /// <summary>Full detail including raw content, headers, and connection information.</summary>
    Raw,
    /// <summary>Classified labels with relevant context (e.g. operation name, target resource).</summary>
    Detailed,
    /// <summary>Minimal labels only — content and connection details are omitted.</summary>
    Summarised
}
