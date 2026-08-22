// Shared source: also compiled into Kronikol.Extensions.TcpTap (and, for the Mongo files,
// Kronikol.Extensions.MongoDB.V2) as a linked file. The tap compiles it into its own namespace so a
// host can reference both packages; the in-process extension keeps this namespace unchanged.
#if KRONIKOL_TCPTAP_SHARED
namespace Kronikol.Extensions.TcpTap.Protocols;
#else
namespace Kronikol.Extensions.MongoDB;
#endif

/// <summary>
/// Classified MongoDB operation types.
/// </summary>
public enum MongoDbOperation
{
    Find,
    Insert,
    Update,
    Delete,
    Aggregate,
    Count,
    FindAndModify,
    Distinct,
    BulkWrite,
    CreateIndex,
    DropIndex,
    CreateCollection,
    DropCollection,
    ListCollections,
    ListDatabases,
    GetMore,
    Watch,
    MapReduce,
    CommitTransaction,
    AbortTransaction,
    DropDatabase,
    RenameCollection,
    ListIndexes,
    ServerStatus,
    DbStats,
    CollStats,
    Other
}
