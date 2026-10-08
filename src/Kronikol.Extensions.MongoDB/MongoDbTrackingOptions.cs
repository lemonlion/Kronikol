using Kronikol.Constants;
namespace Kronikol.Extensions.MongoDB;

/// <summary>
/// Configuration options for MongoDB test tracking.
/// </summary>
public record MongoDbTrackingOptions
{
    /// <summary>
    /// The participant name for the MongoDB server in diagrams, and the name the subscriber registers under.
    /// Default: <c>"MongoDB"</c>.
    /// </summary>
    public string ServiceName { get; set; } = "MongoDB";

    /// <summary>The participant name for the calling service in diagrams.</summary>
    public string CallerName { get; set; } = TrackingDefaults.CallerName;

    /// <summary>Use <see cref="CallerName"/> instead.</summary>
    [Obsolete("Use CallerName instead. CallingServiceName will be removed in a future version.")]
    public string CallingServiceName { get => CallerName; set => CallerName = value; }

    /// <summary>How much of each command the diagrams show. Default: <see cref="MongoDbTrackingVerbosity.Detailed"/>.</summary>
    public MongoDbTrackingVerbosity Verbosity { get; set; } = MongoDbTrackingVerbosity.Detailed;

    /// <summary>
    /// Callback that returns the current test name and ID, such as a framework adapter's <c>CurrentTestInfo.Fetcher</c>.
    /// A command takes its scenario from the request headers first, when <see cref="HttpContextAccessor"/> sees a
    /// request, and then from this callback.
    /// </summary>
    public Func<(string Name, string Id)>? CurrentTestInfoFetcher { get; set; }

    /// <summary>
    /// Callback that returns the current test step type (e.g. "Given", "When", "Then"). The MongoDB subscriber does not
    /// read it: <see cref="SetupVerbosity"/>, <see cref="ActionVerbosity"/>, <see cref="TrackDuringSetup"/> and
    /// <see cref="TrackDuringAction"/> act on the phase <see cref="Kronikol.Tracking.TestPhaseContext"/> holds, which the
    /// framework adapters set.
    /// </summary>
    public Func<string?>? CurrentStepTypeFetcher { get; set; }

    /// <summary>
    /// Resolves the scenario from the test-tracking request headers when a command runs inside a host's request
    /// pipeline (a host <c>WebApplicationFactory</c> starts, or any other host in the test process). Pass the host's own
    /// (<c>sp.GetService&lt;IHttpContextAccessor&gt;()</c>): ASP.NET Core fills an accessor only in a host that registers
    /// one (<c>AddHttpContextAccessor</c>, which <c>TrackDependenciesForDiagrams</c> calls).
    /// <see cref="MongoDbServiceCollectionExtensions.AddMongoDbTestTracking"/> uses the container's when this is null, and
    /// an accessor passed to the <see cref="MongoDbTrackingSubscriber"/> constructor takes precedence.
    /// </summary>
    public Microsoft.AspNetCore.Http.IHttpContextAccessor? HttpContextAccessor { get; set; }

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

    /// <summary>Verbosity override for the Setup phase. <c>null</c> = use <see cref="Verbosity"/>.</summary>
    public MongoDbTrackingVerbosity? SetupVerbosity { get; set; }

    /// <summary>Verbosity override for the Action phase. <c>null</c> = use <see cref="Verbosity"/>.</summary>
    public MongoDbTrackingVerbosity? ActionVerbosity { get; set; }

    /// <summary>When <c>false</c>, commands run during the Setup phase are not tracked. Default: <c>true</c>.</summary>
    public bool TrackDuringSetup { get; set; } = true;

    /// <summary>When <c>false</c>, commands run during the Action phase are not tracked. Default: <c>true</c>.</summary>
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
    /// <see cref="AutoCorrelateWrites"/> fills. From 3.20.0 a claim by filter (a <c>findAndModify</c> that
    /// names no <c>_id</c>) is attributed by the document the reply names, and a detached flow that wrote
    /// such a document keeps the scenario for what it does until its next command on a document that is not
    /// the scenario's (the dispatch between the claim and the status update), held until that command
    /// confirms it; see <see cref="Kronikol.Tracking.DocumentOwnership"/>. Default: <c>true</c> (3.19.0).
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