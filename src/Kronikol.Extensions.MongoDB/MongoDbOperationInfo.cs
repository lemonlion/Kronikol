// Shared source: also compiled into Kronikol.Extensions.TcpTap (and, for the Mongo files,
// Kronikol.Extensions.MongoDB.V2) as a linked file. The tap compiles it into its own namespace so a
// host can reference both packages; the in-process extension keeps this namespace unchanged.
#if KRONIKOL_TCPTAP_SHARED
namespace Kronikol.Extensions.TcpTap.Protocols;
#else
namespace Kronikol.Extensions.MongoDB;
#endif

/// <summary>
/// The result of classifying a MongoDB operation, containing the operation type and metadata.
/// </summary>
public record MongoDbOperationInfo(
    MongoDbOperation Operation,
    string? DatabaseName,
    string? CollectionName,
    string? FilterText = null,
    int? DocumentCount = null,
    string? DocumentId = null,
    string? PipelineStages = null,
    bool IsGridFs = false);
