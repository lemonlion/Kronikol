using Kronikol.Constants;
using Kronikol.Tracking;

namespace Kronikol.Extensions.CosmosDB;

/// <summary>
/// Configuration options for the Azure Cosmos DB test tracking message handler.
/// </summary>
public record CosmosTrackingMessageHandlerOptions
{
    public string ServiceName { get; set; } = "CosmosDB";

    /// <summary>The participant name for the calling service in diagrams.</summary>
    public string CallerName { get; set; } = TrackingDefaults.CallerName;

    /// <summary>Use <see cref="CallerName"/> instead.</summary>
    [Obsolete("Use CallerName instead. CallingServiceName will be removed in a future version.")]
    public string CallingServiceName { get => CallerName; set => CallerName = value; }

    public CosmosTrackingVerbosity Verbosity { get; set; } = CosmosTrackingVerbosity.Detailed;
    public Func<(string Name, string Id)>? CurrentTestInfoFetcher { get; set; }
    public Func<string?>? CurrentStepTypeFetcher { get; set; }

    /// <summary>
    /// Optional list of Cosmos headers to exclude from the diagram notes.
    /// Defaults to common noisy headers.
    /// </summary>
    public HashSet<string> ExcludedHeaders { get; set; } =
    [
        "Authorization",
        "x-ms-date",
        "x-ms-version",
        "x-ms-session-token",
        "User-Agent",
        "Cache-Control",
        "x-ms-cosmos-sdk-supportedcapabilities",
        "x-ms-cosmos-internal-operation-type"
    ];
    public CosmosTrackingVerbosity? SetupVerbosity { get; set; }
    public CosmosTrackingVerbosity? ActionVerbosity { get; set; }
    public bool TrackDuringSetup { get; set; } = true;
    public bool TrackDuringAction { get; set; } = true;
    public Microsoft.AspNetCore.Http.IHttpContextAccessor? HttpContextAccessor { get; set; }

    /// <summary>
    /// When <c>true</c>, tracked write operations (Create, Upsert, Replace) auto-populate
    /// <see cref="Kronikol.Tracking.TestCorrelationStore"/> for parallel-safe
    /// background thread correlation (e.g. Change Feed Processor attribution).
    /// Default: <c>true</c>.
    /// </summary>
    public bool AutoCorrelateWrites { get; set; } = true;

    /// <summary>
    /// When <c>true</c>, a document operation (a read, replace, patch or delete by id) that resolved no
    /// scenario is attributed to the scenario that last wrote the document, per call, with the provenance
    /// <see cref="Kronikol.Tracking.AttributionSource.DocumentOwner"/>: a detached hosted service claiming,
    /// retrying and failing an outbox row lands where the row was seeded. Reads the store
    /// <see cref="AutoCorrelateWrites"/> fills, through <see cref="ChangeFeedKeyExtractor"/> when one is set.
    /// From 3.20.0 a detached flow that wrote such a document also keeps the scenario for what it does until
    /// its next operation on a document that is not the scenario's (the dispatch between the claim and the
    /// status update), held until that operation confirms it; see <see cref="Kronikol.Tracking.DocumentOwnership"/>.
    /// Default: <c>true</c> (3.19.0).
    /// </summary>
    public bool AttributeByDocumentOwner { get; set; } = true;

    /// <summary>
    /// Optional custom key extractor for Change Feed correlation.
    /// When <c>null</c>, the default key format is used: <c>cosmos:{ServiceName}:{documentId}</c>.
    /// </summary>
    public Func<string, string, string>? ChangeFeedKeyExtractor { get; set; }

    /// <summary>
    /// Whether to include response content in diagrams. Default: <c>true</c>.
    /// When <c>true</c>, response arrows show the HTTP response body even at Summarised verbosity.
    /// When <c>false</c>, response arrows are empty (previous behaviour).
    /// </summary>
    public bool LogResponseContent { get; set; } = true;
}