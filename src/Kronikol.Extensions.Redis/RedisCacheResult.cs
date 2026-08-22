// Shared source: also compiled into Kronikol.Extensions.TcpTap (and, for the Mongo files,
// Kronikol.Extensions.MongoDB.V2) as a linked file. The tap compiles it into its own namespace so a
// host can reference both packages; the in-process extension keeps this namespace unchanged.
#if KRONIKOL_TCPTAP_SHARED
namespace Kronikol.Extensions.TcpTap.Protocols;
#else
namespace Kronikol.Extensions.Redis;
#endif

/// <summary>
/// The cache outcome of a Redis operation.
/// </summary>
public enum RedisCacheResult
{
    /// <summary>The key existed and a value was returned.</summary>
    Hit,

    /// <summary>The key did not exist.</summary>
    Miss,

    /// <summary>Cache result is not applicable for this operation.</summary>
    None
}
