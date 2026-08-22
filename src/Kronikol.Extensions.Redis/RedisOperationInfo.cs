// Shared source: also compiled into Kronikol.Extensions.TcpTap (and, for the Mongo files,
// Kronikol.Extensions.MongoDB.V2) as a linked file. The tap compiles it into its own namespace so a
// host can reference both packages; the in-process extension keeps this namespace unchanged.
#if KRONIKOL_TCPTAP_SHARED
namespace Kronikol.Extensions.TcpTap.Protocols;
#else
namespace Kronikol.Extensions.Redis;
#endif

/// <summary>
/// The result of classifying a Redis operation, containing the operation type and metadata.
/// </summary>
public record RedisOperationInfo(
    RedisOperation Operation,
    RedisCacheResult CacheResult,
    string? Key,
    int DatabaseNumber);
