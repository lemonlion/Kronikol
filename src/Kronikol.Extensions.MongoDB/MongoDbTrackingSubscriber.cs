using Kronikol.Constants;
using System.Collections.Concurrent;
using System.Net;
using global::MongoDB.Bson;
using global::MongoDB.Driver.Core.Events;
using Microsoft.AspNetCore.Http;
using Kronikol.Tracking;

namespace Kronikol.Extensions.MongoDB;

/// <summary>
/// Implements the MongoDB event subscriber interface to track MongoDB operations for test visualization.
/// Also implements <see cref="IEventSubscriber"/> for use with InMemoryEmulator.MongoDB's CommandEventSubscriptionBuilder
/// or any other event source that accepts <see cref="IEventSubscriber"/>.
/// </summary>
public class MongoDbTrackingSubscriber : ITrackingComponent, IEventSubscriber
{
    private readonly MongoDbTrackingOptions _options;
    private readonly IHttpContextAccessor? _httpContextAccessor;
    private readonly ConcurrentDictionary<int, PendingOperation> _pending = new();
    private int _invocationCount;

    public MongoDbTrackingSubscriber(MongoDbTrackingOptions options, IHttpContextAccessor? httpContextAccessor = null)
    {
        _options = options;
        _httpContextAccessor = httpContextAccessor;
        TrackingComponentRegistry.Register(this);
    }

    public string ComponentName => $"MongoDbTrackingSubscriber ({_options.ServiceName})";
    public bool WasInvoked => _invocationCount > 0;
    public int InvocationCount => _invocationCount;
    public bool HasHttpContextAccessor => _httpContextAccessor is not null;

    /// <summary>
    /// Subscribe this tracker to a ClusterBuilder.
    /// Call from MongoClientSettings.ClusterConfigurator.
    /// </summary>
    public void Subscribe(global::MongoDB.Driver.Core.Configuration.ClusterBuilder builder)
    {
        builder.Subscribe<CommandStartedEvent>(OnCommandStarted);
        builder.Subscribe<CommandSucceededEvent>(OnCommandSucceeded);
        builder.Subscribe<CommandFailedEvent>(OnCommandFailed);
    }

    /// <inheritdoc />
    public bool TryGetEventHandler<T>(out Action<T> handler)
    {
        if (typeof(T) == typeof(CommandStartedEvent))
        {
            handler = (Action<T>)(object)(Action<CommandStartedEvent>)OnCommandStarted;
            return true;
        }
        if (typeof(T) == typeof(CommandSucceededEvent))
        {
            handler = (Action<T>)(object)(Action<CommandSucceededEvent>)OnCommandSucceeded;
            return true;
        }
        if (typeof(T) == typeof(CommandFailedEvent))
        {
            handler = (Action<T>)(object)(Action<CommandFailedEvent>)OnCommandFailed;
            return true;
        }

        handler = null!;
        return false;
    }

    public void OnCommandStarted(CommandStartedEvent e)
    {
        Interlocked.Increment(ref _invocationCount);

        if (!PhaseConfiguration.ShouldTrack(_options.TrackDuringSetup, _options.TrackDuringAction))
            return;
        var effectiveVerbosity = PhaseConfiguration.GetEffectiveVerbosity(_options.Verbosity, _options.SetupVerbosity, _options.ActionVerbosity);

        if (_options.IgnoredCommands.Contains(e.CommandName)) return;
        if (!_options.TrackGetMore && e.CommandName.Equals("getMore", StringComparison.OrdinalIgnoreCase)) return;

        var opInfo = MongoDbOperationClassifier.Classify(
            e.CommandName,
            e.DatabaseNamespace?.DatabaseName,
            e.Command);

        // An excluded operation is invisible to tracking, and so to the owner window below.
        if (_options.ExcludedOperations.Contains(opInfo.Operation))
            return;

        var testInfo = TestInfoResolver.ResolveWithSource(_httpContextAccessor, _options.CurrentTestInfoFetcher);
        // A document a scenario wrote is the scenario's: a command that names a document and resolved no
        // scenario is attributed to the document's last attributed writer, for this one call, and the flow
        // that wrote it is doing the scenario's work until its next command on a document that is not the
        // scenario's. See DocumentOwnership and plans/DOCUMENT_OWNERSHIP_PLAN.md.
        if (_options.AttributeByDocumentOwner)
        {
            var (ownedKey, kind) = ForOwnership(opInfo);
            if (opInfo.Operation == MongoDbOperation.FindAndModify && ownedKey is null
                && testInfo is not { IsAttributed: true, Source: not AttributionSource.DocumentFlow })
            {
                // A claim by filter (findAndModify on a status, not an _id) names the document it took only
                // in the reply: the request half waits for it, and is attributed with it.
                _pending[e.RequestId] = new PendingOperation(default, opInfo,
                    BuildUri(opInfo, effectiveVerbosity), MongoDbOperationClassifier.GetDiagramLabel(opInfo, effectiveVerbosity),
                    Guid.NewGuid(), Guid.NewGuid())
                {
                    Deferred = new DeferredRequest(testInfo, e.Command, DateTimeOffset.UtcNow, TestPhaseContext.Current)
                };
                return;
            }
            testInfo = DocumentOwnership.ForOperation(testInfo, ownedKey, kind);
        }
        if (testInfo is null) return;

        if (effectiveVerbosity == MongoDbTrackingVerbosity.Summarised &&
            opInfo.Operation == MongoDbOperation.Other)
            return;

        var pending = new PendingOperation(testInfo.Value, opInfo,
            BuildUri(opInfo, effectiveVerbosity), MongoDbOperationClassifier.GetDiagramLabel(opInfo, effectiveVerbosity),
            Guid.NewGuid(), Guid.NewGuid());
        _pending[e.RequestId] = pending;
        LogRequest(pending, e.Command, effectiveVerbosity, timestamp: null, TestPhaseContext.Current);
    }

    public void OnCommandSucceeded(CommandSucceededEvent e)
    {
        if (!_pending.TryRemove(e.RequestId, out var pending)) return;

        var effectiveVerbosity = PhaseConfiguration.GetEffectiveVerbosity(_options.Verbosity, _options.SetupVerbosity, _options.ActionVerbosity);

        if (pending.Deferred is { } deferred)
        {
            // The claim by filter: the reply names the document it took, or nothing.
            var claimed = ClaimedDocument(e.Reply);
            var key = claimed is null ? null : CorrelationKeys.Mongo(_options.ServiceName, claimed);
            var testInfo = DocumentOwnership.ForOperation(deferred.Resolved, key, DocumentOperationKind.Write);
            if (testInfo is null) return;
            pending = pending with { TestInfo = testInfo.Value, OpInfo = pending.OpInfo with { DocumentId = claimed }, Deferred = null };
            LogRequest(pending, deferred.Command, effectiveVerbosity, deferred.StartedAt, deferred.Phase);
        }

        AutoCorrelateIfWrite(pending);

        var content = effectiveVerbosity switch
        {
            MongoDbTrackingVerbosity.Summarised when !_options.LogResponseContent => null,
            MongoDbTrackingVerbosity.Raw => e.Reply?.ToString(),
            _ => ExtractDetailedResponse(e.Reply) // Detailed/Summarised+LogResponseContent: metadata + optional document preview
        };

        RequestResponseLogger.Log(new RequestResponseLog(
            pending.TestInfo.Name, pending.TestInfo.Id,
            pending.Label,
            content, pending.Uri,
            [], _options.ServiceName, _options.CallerName,
            RequestResponseType.Response, pending.TraceId, pending.RequestResponseId, false,
            HttpStatusCode.OK,
            DependencyCategory: DependencyCategories.MongoDB)
        {
            AttributionSource = pending.TestInfo.Source,
            Phase = TestPhaseContext.Current
        }.WithVariants(_options.Verbosity, _options.SetupVerbosity, _options.ActionVerbosity,
            v =>
            {
                var vContent = v switch
                {
                    MongoDbTrackingVerbosity.Summarised when !_options.LogResponseContent => null,
                    MongoDbTrackingVerbosity.Raw => e.Reply?.ToString(),
                    _ => ExtractDetailedResponse(e.Reply)
                };
                return new PhaseVariant(
                    MongoDbOperationClassifier.GetDiagramLabel(pending.OpInfo, v),
                    BuildUri(pending.OpInfo, v),
                    vContent, [],
                    v == MongoDbTrackingVerbosity.Summarised && pending.OpInfo.Operation == MongoDbOperation.Other);
            }));
    }

    public void OnCommandFailed(CommandFailedEvent e)
    {
        if (!_pending.TryRemove(e.RequestId, out var pending)) return;

        if (pending.Deferred is { } deferred)
        {
            // A claim that failed took nothing: it is what the resolver said it was, or nothing at all.
            var testInfo = DocumentOwnership.ForOperation(deferred.Resolved, null, DocumentOperationKind.Write);
            if (testInfo is null) return;
            pending = pending with { TestInfo = testInfo.Value, Deferred = null };
            var effectiveVerbosity = PhaseConfiguration.GetEffectiveVerbosity(_options.Verbosity, _options.SetupVerbosity, _options.ActionVerbosity);
            LogRequest(pending, deferred.Command, effectiveVerbosity, deferred.StartedAt, deferred.Phase);
        }

        RequestResponseLogger.Log(new RequestResponseLog(
            pending.TestInfo.Name, pending.TestInfo.Id,
            pending.Label,
            e.Failure?.Message, pending.Uri,
            [], _options.ServiceName, _options.CallerName,
            RequestResponseType.Response, pending.TraceId, pending.RequestResponseId, false,
            HttpStatusCode.InternalServerError,
            DependencyCategory: DependencyCategories.MongoDB)
        {
            AttributionSource = pending.TestInfo.Source,
            Phase = TestPhaseContext.Current
        }.WithVariants(_options.Verbosity, _options.SetupVerbosity, _options.ActionVerbosity,
            v => new PhaseVariant(
                MongoDbOperationClassifier.GetDiagramLabel(pending.OpInfo, v),
                BuildUri(pending.OpInfo, v),
                e.Failure?.Message, [],
                v == MongoDbTrackingVerbosity.Summarised && pending.OpInfo.Operation == MongoDbOperation.Other)));
    }

    /// <summary>The request half of a command, as it was when the command started.</summary>
    private void LogRequest(PendingOperation pending, BsonDocument? command, MongoDbTrackingVerbosity effectiveVerbosity, DateTimeOffset? timestamp, TestPhase phase)
    {
        var opInfo = pending.OpInfo;
        var content = effectiveVerbosity switch
        {
            MongoDbTrackingVerbosity.Summarised => null,
            MongoDbTrackingVerbosity.Raw => command?.ToString(),
            _ => _options.LogFilterText ? opInfo.FilterText : null // Detailed: show filter if enabled
        };

        RequestResponseLogger.Log(new RequestResponseLog(
            pending.TestInfo.Name, pending.TestInfo.Id,
            pending.Label,
            content, pending.Uri,
            [], _options.ServiceName, _options.CallerName,
            RequestResponseType.Request, pending.TraceId, pending.RequestResponseId, false,
            DependencyCategory: DependencyCategories.MongoDB)
        {
            Timestamp = timestamp,
            AttributionSource = pending.TestInfo.Source,
            Phase = phase
        }.WithVariants(_options.Verbosity, _options.SetupVerbosity, _options.ActionVerbosity,
            v =>
            {
                var vContent = v switch
                {
                    MongoDbTrackingVerbosity.Summarised => null,
                    MongoDbTrackingVerbosity.Raw => command?.ToString(),
                    _ => _options.LogFilterText ? opInfo.FilterText : null
                };
                return new PhaseVariant(
                    MongoDbOperationClassifier.GetDiagramLabel(opInfo, v),
                    BuildUri(opInfo, v),
                    vContent, [],
                    v == MongoDbTrackingVerbosity.Summarised && opInfo.Operation == MongoDbOperation.Other);
            }));
    }

    private string? ExtractDetailedResponse(BsonDocument? reply) =>
        MongoDbResponseSummary.ExtractDetailed(reply, _options.LogResponseContent, _options.MaxResponseDocuments);

    private Uri BuildUri(MongoDbOperationInfo opInfo, MongoDbTrackingVerbosity verbosity)
    {
        var db = opInfo.DatabaseName ?? "unknown";
        var coll = opInfo.CollectionName;

        return verbosity switch
        {
            MongoDbTrackingVerbosity.Summarised =>
                new Uri($"mongodb:///{db}"),
            _ => coll != null
                ? new Uri($"mongodb:///{db}/{coll}")
                : new Uri($"mongodb:///{db}")
        };
    }

    private sealed record PendingOperation(
        TestIdentity TestInfo,
        MongoDbOperationInfo OpInfo,
        Uri Uri,
        string Label,
        Guid TraceId,
        Guid RequestResponseId)
    {
        /// <summary>A claim by filter whose request half is logged when the reply names its document.</summary>
        public DeferredRequest? Deferred { get; init; }
    }

    /// <summary>What a deferred request half needs to be logged as it was when the command started.</summary>
    private sealed record DeferredRequest(TestIdentity? Resolved, BsonDocument? Command, DateTimeOffset StartedAt, TestPhase Phase);

    /// <summary>The document a findAndModify reply says it took, or null when it took nothing.</summary>
    private static string? ClaimedDocument(BsonDocument? reply) =>
        reply is not null && reply.TryGetValue("value", out var value) && value is BsonDocument document && document.Contains("_id")
            ? document["_id"].ToString()
            : null;

    /// <summary>
    /// What the command is to document ownership: the store's key for the document it names, when it names
    /// one, and whether it reads, writes or queries. A query, an aggregation or a count that names no
    /// document is the poll; a write that names none (a multi-document write) leaves a window as it is; an
    /// index, collection or transaction command is neither.
    /// </summary>
    private (string? Key, DocumentOperationKind Kind) ForOwnership(MongoDbOperationInfo opInfo)
    {
        var key = opInfo.DocumentId is { } id ? CorrelationKeys.Mongo(_options.ServiceName, id) : null;
        return opInfo.Operation switch
        {
            MongoDbOperation.Find or MongoDbOperation.Aggregate or MongoDbOperation.Count or MongoDbOperation.Distinct
                or MongoDbOperation.GetMore or MongoDbOperation.MapReduce
                => key is null ? (null, DocumentOperationKind.Query) : (key, DocumentOperationKind.Read),
            MongoDbOperation.Insert or MongoDbOperation.Update or MongoDbOperation.Delete
                or MongoDbOperation.FindAndModify or MongoDbOperation.BulkWrite
                => (key, DocumentOperationKind.Write),
            _ => (null, DocumentOperationKind.Read)
        };
    }

    private void AutoCorrelateIfWrite(PendingOperation pending)
    {
        if (_options.AttributeByDocumentOwner && ForOwnership(pending.OpInfo).Kind == DocumentOperationKind.Write)
            DocumentOwnership.AfterWrite(pending.TestInfo);

        if (!_options.AutoCorrelateWrites) return;
        // Only a scenario can own a document: the background identity registered as a writer would answer
        // the next identity-less command on the document with the provenance of an owner.
        if (!pending.TestInfo.IsAttributed) return;
        if (pending.OpInfo.DocumentId is null) return;

        var isWrite = pending.OpInfo.Operation is MongoDbOperation.Insert
            or MongoDbOperation.Update
            or MongoDbOperation.FindAndModify;
        if (!isWrite) return;

        var key = CorrelationKeys.Mongo(_options.ServiceName, pending.OpInfo.DocumentId);
        TestCorrelationStore.Correlate(key, pending.TestInfo.Name, pending.TestInfo.Id);
    }
}
