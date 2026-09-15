using Kronikol.Constants;
namespace Kronikol.Extensions.MongoDB;

/// <summary>
/// Configuration options for MongoDB test tracking.
/// </summary>
public record MongoDbTrackingOptions
{
    public string ServiceName { get; set; } = "MongoDB";

    /// <summary>The participant name for the calling service in diagrams.</summary>
    public string CallerName { get; set; } = TrackingDefaults.CallerName;

    /// <summary>Use <see cref="CallerName"/> instead.</summary>
    [Obsolete("Use CallerName instead. CallingServiceName will be removed in a future version.")]
    public string CallingServiceName { get => CallerName; set => CallerName = value; }

    public MongoDbTrackingVerbosity Verbosity { get; set; } = MongoDbTrackingVerbosity.Detailed;
    public Func<(string Name, string Id)>? CurrentTestInfoFetcher { get; set; }
    public Func<string?>? CurrentStepTypeFetcher { get; set; }

    /// <summary>
    /// Commands to ignore (e.g., monitoring noise like "isMaster", "hello", "ping").
    /// </summary>
    public HashSet<string> IgnoredCommands { get; set; } =
    [
        "isMaster", "hello", "saslStart", "saslContinue",
        "ping", "buildInfo", "getLastError",
        "killCursors", "endSessions"
    ];

    /// <summary>
    /// Whether to track getMore (cursor continuation) operations.
    /// Disabled by default as they add noise.
    /// </summary>
    public bool TrackGetMore { get; set; } = false;
    public MongoDbTrackingVerbosity? SetupVerbosity { get; set; }
    public MongoDbTrackingVerbosity? ActionVerbosity { get; set; }
    public bool TrackDuringSetup { get; set; } = true;
    public bool TrackDuringAction { get; set; } = true;

    /// <summary>
    /// Classified operations to exclude from tracking.
    /// Unlike <see cref="IgnoredCommands"/> (which filters raw driver command names),
    /// this filters at the classified operation level.
    /// </summary>
    public HashSet<MongoDbOperation> ExcludedOperations { get; set; } = [];

    /// <summary>
    /// Whether to include filter BSON text in the logged content for Detailed verbosity.
    /// </summary>
    public bool LogFilterText { get; set; } = true;

    /// <summary>
    /// When <c>true</c>, tracked write operations (Insert, Update, FindAndModify) auto-populate
    /// <see cref="Kronikol.Tracking.TestCorrelationStore"/> for parallel-safe
    /// background thread correlation (e.g. Change Stream attribution).
    /// Default: <c>true</c>.
    /// </summary>
    public bool AutoCorrelateWrites { get; set; } = true;

    /// <summary>
    /// When <c>true</c>, a command that names a document (a filter or update on its <c>_id</c>) and resolved
    /// no scenario is attributed to the scenario that last wrote the document, per call, with the provenance
    /// <see cref="Kronikol.Tracking.AttributionSource.DocumentOwner"/>. Reads the store
    /// <see cref="AutoCorrelateWrites"/> fills. Default: <c>true</c> (3.19.0).
    /// </summary>
    public bool AttributeByDocumentOwner { get; set; } = true;

    /// <summary>
    /// Whether to include response content (documents from cursor.firstBatch) in diagrams.
    /// Response arrows show payload data (metadata, document previews) at all verbosity levels.
    /// At Raw, the full reply is always shown regardless.
    /// Default: true.
    /// </summary>
    public bool LogResponseContent { get; set; } = true;

    /// <summary>
    /// Maximum number of documents to include from cursor.firstBatch in response content.
    /// Default: 10.
    /// </summary>
    public int MaxResponseDocuments { get; set; } = 10;
}