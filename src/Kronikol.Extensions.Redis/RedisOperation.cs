// Shared source: also compiled into Kronikol.Extensions.TcpTap (and, for the Mongo files,
// Kronikol.Extensions.MongoDB.V2) as a linked file. The tap compiles it into its own namespace so a
// host can reference both packages; the in-process extension keeps this namespace unchanged.
#if KRONIKOL_TCPTAP_SHARED
namespace Kronikol.Extensions.TcpTap.Protocols;
#else
namespace Kronikol.Extensions.Redis;
#endif

/// <summary>
/// Classified Redis operation types.
/// </summary>
public enum RedisOperation
{
    Get,
    Set,
    Delete,
    KeyExists,
    Expire,
    HashGet,
    HashSet,
    HashDelete,
    HashGetAll,
    ListPush,
    ListRange,
    SetAdd,
    SetMembers,
    Increment,
    Decrement,
    Publish,
    Other
}
