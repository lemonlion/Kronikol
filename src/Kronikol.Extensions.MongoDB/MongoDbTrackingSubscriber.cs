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
/// <remarks>
/// The driver runs these handlers inside the application's own call, so none lets an exception through: a command
/// that cannot be recorded is left out and reported once in the run's diagnostics. A reply with <c>writeErrors</c> or a
/// <c>writeConcernError</c>, which the driver raises as a success and the application as an exception, is drawn as a
/// failure (500), and a failed call carries the server's message in <see cref="RequestResponseLog.Error"/>.
/// </remarks>
public class MongoDbTrackingSubscriber : ITrackingComponent, IEventSubscriber
{
    private readonly MongoDbTrackingOptions _options;
    private readonly IHttpContextAccessor? _httpContextAccessor;
    private readonly ConcurrentDictionary<int, PendingOperation> _pending = new();
    private int _invocationCount;

    /// <summary>
    /// Creates a subscriber that tracks commands with <paramref name="options"/>.
    /// </summary>
    /// <param name="options">The tracking options.</param>
    /// <param name="httpContextAccessor">
    /// Resolves the scenario from the request headers when a command runs inside a host's request pipeline. Takes
    /// precedence over <see cref="MongoDbTrackingOptions.HttpContextAccessor"/>, which is used when this is null.
    /// </param>
    public MongoDbTrackingSubscriber(MongoDbTrackingOptions options, IHttpContextAccessor? httpContextAccessor = null)
    {
        _options = options;
        _httpContextAccessor = httpContextAccessor ?? options.HttpContextAccessor;
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

    // The driver runs these handlers inside the application's own call, so an exception leaving one fails that call
    // (at Raw a claim by filter once made the application's FindOneAndUpdate throw after the server had applied it).
    // Each handler catches: the command goes unrecorded and the run's diagnostics say so.

    public void OnCommandStarted(CommandStartedEvent e)
    {
        Interlocked.Increment(ref _invocationCount);
        try
        {
            Started(e);
        }
        catch (Exception ex)
        {
            // A command whose request could not be logged is dropped whole: its reply finds nothing pending.
            _pending.TryRemove(e.RequestId, out _);
            NotRecorded(ex);
        }
    }

    public void OnCommandSucceeded(CommandSucceededEvent e)
    {
        try
        {
            Succeeded(e);
        }
        catch (Exception ex)
        {
            NotRecorded(ex);
        }
    }

    public void OnCommandFailed(CommandFailedEvent e)
    {
        try
        {
            Failed(e);
        }
        catch (Exception ex)
        {
            NotRecorded(ex);
        }
    }

    private void Started(CommandStartedEvent e)
    {
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
                // in the reply: the request half waits for it, and is attributed with it. The driver disposes the
                // command when this handler returns, so the half keeps its note at every verbosity it can be
                // logged at, rendered now.
                var command = e.Command;
                var notes = new Dictionary<MongoDbTrackingVerbosity, string?>();
                foreach (var verbosity in new[] { _options.Verbosity, _options.SetupVerbosity ?? _options.Verbosity, _options.ActionVerbosity ?? _options.Verbosity })
                    notes.TryAdd(verbosity, RequestNote(command, verbosity));
                _pending[e.RequestId] = new PendingOperation(default, opInfo,
                    BuildUri(opInfo, effectiveVerbosity), MongoDbOperationClassifier.GetDiagramLabel(opInfo, effectiveVerbosity),
                    Guid.NewGuid(), Guid.NewGuid())
                {
                    Deferred = new DeferredRequest(testInfo, notes, DateTimeOffset.UtcNow, TestPhaseContext.Current)
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
        var startedCommand = e.Command;
        LogRequest(pending, verbosity => RequestNote(startedCommand, verbosity), effectiveVerbosity, timestamp: null, TestPhaseContext.Current);
    }

    private void Succeeded(CommandSucceededEvent e)
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
            LogRequest(pending, verbosity => deferred.Notes[verbosity], effectiveVerbosity, deferred.StartedAt, deferred.Phase);
        }

        AutoCorrelateIfWrite(pending);

        // A write error arrives as a success (ok: 1 with writeErrors), and the application's call throws.
        var reply = e.Reply;
        bool writeFailed;
        string? error;
        try
        {
            writeFailed = reply is not null && MongoDbNotes.IsWriteFailure(reply);
            error = writeFailed ? MongoDbNotes.FailureReason(reply!) : null;
        }
        catch (Exception)
        {
            // A reply that cannot be read says nothing of a failure; its note says why it is missing.
            writeFailed = false;
            error = null;
        }
        var operation = pending.OpInfo.Operation;

        RequestResponseLogger.Log(new RequestResponseLog(
            pending.TestInfo.Name, pending.TestInfo.Id,
            pending.Label,
            ReplyNote(reply, operation, effectiveVerbosity), pending.Uri,
            [], _options.ServiceName, _options.CallerName,
            RequestResponseType.Response, pending.TraceId, pending.RequestResponseId, false,
            writeFailed ? HttpStatusCode.InternalServerError : HttpStatusCode.OK,
            DependencyCategory: DependencyCategories.MongoDB)
        {
            Error = error,
            AttributionSource = pending.TestInfo.Source,
            Phase = TestPhaseContext.Current
        }.WithVariants(_options.Verbosity, _options.SetupVerbosity, _options.ActionVerbosity,
            v => new PhaseVariant(
                MongoDbOperationClassifier.GetDiagramLabel(pending.OpInfo, v),
                BuildUri(pending.OpInfo, v),
                ReplyNote(reply, operation, v), [],
                v == MongoDbTrackingVerbosity.Summarised && pending.OpInfo.Operation == MongoDbOperation.Other)));
    }

    private void Failed(CommandFailedEvent e)
    {
        if (!_pending.TryRemove(e.RequestId, out var pending)) return;

        var effectiveVerbosity = PhaseConfiguration.GetEffectiveVerbosity(_options.Verbosity, _options.SetupVerbosity, _options.ActionVerbosity);

        if (pending.Deferred is { } deferred)
        {
            // A claim that failed took nothing: it is what the resolver said it was, or nothing at all.
            var testInfo = DocumentOwnership.ForOperation(deferred.Resolved, null, DocumentOperationKind.Write);
            if (testInfo is null) return;
            pending = pending with { TestInfo = testInfo.Value, Deferred = null };
            LogRequest(pending, verbosity => deferred.Notes[verbosity], effectiveVerbosity, deferred.StartedAt, deferred.Phase);
        }

        // The server's words are in the reply the exception carries, not in its message ("find command failed"). The
        // driver disposes that reply when this handler returns, so it is read now.
        var result = (e.Failure as global::MongoDB.Driver.MongoCommandException)?.Result;
        var message = e.Failure?.Message;
        string? error;
        try
        {
            error = (result is null ? null : MongoDbNotes.FailureReason(result)) ?? message;
        }
        catch (Exception)
        {
            error = message;
        }

        RequestResponseLogger.Log(new RequestResponseLog(
            pending.TestInfo.Name, pending.TestInfo.Id,
            pending.Label,
            FailureNote(result, message, effectiveVerbosity), pending.Uri,
            [], _options.ServiceName, _options.CallerName,
            RequestResponseType.Response, pending.TraceId, pending.RequestResponseId, false,
            HttpStatusCode.InternalServerError,
            DependencyCategory: DependencyCategories.MongoDB)
        {
            Error = error,
            AttributionSource = pending.TestInfo.Source,
            Phase = TestPhaseContext.Current
        }.WithVariants(_options.Verbosity, _options.SetupVerbosity, _options.ActionVerbosity,
            v => new PhaseVariant(
                MongoDbOperationClassifier.GetDiagramLabel(pending.OpInfo, v),
                BuildUri(pending.OpInfo, v),
                FailureNote(result, message, v), [],
                v == MongoDbTrackingVerbosity.Summarised && pending.OpInfo.Operation == MongoDbOperation.Other)));
    }

    /// <summary>The request half of a command, with the note <paramref name="note"/> gives it at each verbosity.</summary>
    private void LogRequest(PendingOperation pending, Func<MongoDbTrackingVerbosity, string?> note, MongoDbTrackingVerbosity effectiveVerbosity, DateTimeOffset? timestamp, TestPhase phase)
    {
        var opInfo = pending.OpInfo;
        RequestResponseLogger.Log(new RequestResponseLog(
            pending.TestInfo.Name, pending.TestInfo.Id,
            pending.Label,
            note(effectiveVerbosity), pending.Uri,
            [], _options.ServiceName, _options.CallerName,
            RequestResponseType.Request, pending.TraceId, pending.RequestResponseId, false,
            DependencyCategory: DependencyCategories.MongoDB)
        {
            Timestamp = timestamp,
            AttributionSource = pending.TestInfo.Source,
            Phase = phase
        }.WithVariants(_options.Verbosity, _options.SetupVerbosity, _options.ActionVerbosity,
            v => new PhaseVariant(
                MongoDbOperationClassifier.GetDiagramLabel(opInfo, v),
                BuildUri(opInfo, v),
                note(v), [],
                v == MongoDbTrackingVerbosity.Summarised && opInfo.Operation == MongoDbOperation.Other)));
    }

    /// <summary>The request note at <paramref name="verbosity"/>: at Detailed (when <see cref="MongoDbTrackingOptions.LogFilterText"/>) the command without its name and the driver's own fields, as <see cref="MongoDbNotes.Request"/> writes it; at Raw the whole command.</summary>
    private string? RequestNote(BsonDocument? command, MongoDbTrackingVerbosity verbosity) =>
        Rendered("command", () => verbosity switch
        {
            MongoDbTrackingVerbosity.Summarised => null,
            MongoDbTrackingVerbosity.Raw => MongoDbNotes.Whole(command),
            _ => _options.LogFilterText && command is not null ? MongoDbNotes.Request(command, _options.MaxRequestDocuments) : null
        });

    /// <summary>The reply note at <paramref name="verbosity"/>: the whole reply at Raw, otherwise what <see cref="MongoDbNotes.Reply"/> keeps.</summary>
    private string? ReplyNote(BsonDocument? reply, MongoDbOperation operation, MongoDbTrackingVerbosity verbosity) =>
        Rendered("reply", () => verbosity switch
        {
            MongoDbTrackingVerbosity.Summarised when !_options.LogResponseContent => null,
            MongoDbTrackingVerbosity.Raw => MongoDbNotes.Whole(reply),
            _ => MongoDbNotes.Reply(reply, operation, _options.LogResponseContent, _options.MaxResponseDocuments)
        });

    /// <summary>A failed command's note: the server's reply (whole at Raw, without its bookkeeping otherwise), or the exception's message when there is none.</summary>
    private static string? FailureNote(BsonDocument? result, string? message, MongoDbTrackingVerbosity verbosity) =>
        result is null
            ? message
            : Rendered("reply", () => verbosity == MongoDbTrackingVerbosity.Raw
                ? MongoDbNotes.Whole(result)
                : MongoDbNotes.Failure(result) ?? message);

    /// <summary>A note, or what stopped it being written, as the wire tap writes its own.</summary>
    private static string? Rendered(string what, Func<string?> render)
    {
        try
        {
            return render();
        }
        catch (Exception ex)
        {
            return $"<{what} could not be rendered: {ex.GetType().Name}>";
        }
    }

    /// <summary>Says once, in every report this process writes, that a command went unrecorded and why.</summary>
    private void NotRecorded(Exception ex)
    {
        try
        {
            Kronikol.Reports.ProcessDiagnostics.RecordOnce(
                "mongodb-subscriber-failed",
                Kronikol.Reports.DiagnosticKind.CaptureDegraded,
                $"MongoDbTrackingSubscriber ({_options.ServiceName}) could not record a command: {ex.GetType().Name}: {ex.Message}. " +
                "The application's call was not affected; the command, and any after it that fail the same way, are missing from the diagrams.");
        }
        catch (Exception)
        {
            // Nothing a diagnostic can fail on may reach the driver either.
        }
    }

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

    /// <summary>
    /// What a deferred request half needs to be logged as it was when the command started: its note at each verbosity
    /// it can be logged at, rendered while the driver's command was still readable.
    /// </summary>
    private sealed record DeferredRequest(TestIdentity? Resolved, IReadOnlyDictionary<MongoDbTrackingVerbosity, string?> Notes, DateTimeOffset StartedAt, TestPhase Phase);

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
