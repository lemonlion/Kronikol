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

        var testInfo = TestInfoResolver.Resolve(_httpContextAccessor, _options.CurrentTestInfoFetcher);
        if (testInfo is null) return;

        var opInfo = MongoDbOperationClassifier.Classify(
            e.CommandName,
            e.DatabaseNamespace?.DatabaseName,
            e.Command);

        if (_options.ExcludedOperations.Contains(opInfo.Operation))
            return;

        if (effectiveVerbosity == MongoDbTrackingVerbosity.Summarised &&
            opInfo.Operation == MongoDbOperation.Other)
            return;

        var uri = BuildUri(opInfo, effectiveVerbosity);
        var label = MongoDbOperationClassifier.GetDiagramLabel(opInfo, effectiveVerbosity);

        var content = effectiveVerbosity switch
        {
            MongoDbTrackingVerbosity.Summarised => null,
            MongoDbTrackingVerbosity.Raw => e.Command?.ToString(),
            _ => _options.LogFilterText ? opInfo.FilterText : null // Detailed: show filter if enabled
        };

        var traceId = Guid.NewGuid();
        var requestResponseId = Guid.NewGuid();

        _pending[e.RequestId] = new PendingOperation(
            testInfo.Value, opInfo, uri, label, traceId, requestResponseId);

        RequestResponseLogger.Log(new RequestResponseLog(
            testInfo.Value.Name, testInfo.Value.Id,
            label,
            content, uri,
            [], _options.ServiceName, _options.CallerName,
            RequestResponseType.Request, traceId, requestResponseId, false,
            DependencyCategory: DependencyCategories.MongoDB)
        {
            Phase = TestPhaseContext.Current
        }.WithVariants(_options.Verbosity, _options.SetupVerbosity, _options.ActionVerbosity,
            v =>
            {
                var vContent = v switch
                {
                    MongoDbTrackingVerbosity.Summarised => null,
                    MongoDbTrackingVerbosity.Raw => e.Command?.ToString(),
                    _ => _options.LogFilterText ? opInfo.FilterText : null
                };
                return new PhaseVariant(
                    MongoDbOperationClassifier.GetDiagramLabel(opInfo, v),
                    BuildUri(opInfo, v),
                    vContent, [],
                    v == MongoDbTrackingVerbosity.Summarised && opInfo.Operation == MongoDbOperation.Other);
            }));
    }

    public void OnCommandSucceeded(CommandSucceededEvent e)
    {
        if (!_pending.TryRemove(e.RequestId, out var pending)) return;

        AutoCorrelateIfWrite(pending);

        var effectiveVerbosity = PhaseConfiguration.GetEffectiveVerbosity(_options.Verbosity, _options.SetupVerbosity, _options.ActionVerbosity);

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

        RequestResponseLogger.Log(new RequestResponseLog(
            pending.TestInfo.Name, pending.TestInfo.Id,
            pending.Label,
            e.Failure?.Message, pending.Uri,
            [], _options.ServiceName, _options.CallerName,
            RequestResponseType.Response, pending.TraceId, pending.RequestResponseId, false,
            HttpStatusCode.InternalServerError,
            DependencyCategory: DependencyCategories.MongoDB)
        {
            Phase = TestPhaseContext.Current
        }.WithVariants(_options.Verbosity, _options.SetupVerbosity, _options.ActionVerbosity,
            v => new PhaseVariant(
                MongoDbOperationClassifier.GetDiagramLabel(pending.OpInfo, v),
                BuildUri(pending.OpInfo, v),
                e.Failure?.Message, [],
                v == MongoDbTrackingVerbosity.Summarised && pending.OpInfo.Operation == MongoDbOperation.Other)));
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

    private record PendingOperation(
        (string Name, string Id) TestInfo,
        MongoDbOperationInfo OpInfo,
        Uri Uri,
        string Label,
        Guid TraceId,
        Guid RequestResponseId);

    private void AutoCorrelateIfWrite(PendingOperation pending)
    {
        if (!_options.AutoCorrelateWrites) return;
        if (pending.OpInfo.DocumentId is null) return;

        var isWrite = pending.OpInfo.Operation is MongoDbOperation.Insert
            or MongoDbOperation.Update
            or MongoDbOperation.FindAndModify;
        if (!isWrite) return;

        var key = CorrelationKeys.Mongo(_options.ServiceName, pending.OpInfo.DocumentId);
        TestCorrelationStore.Correlate(key, pending.TestInfo.Name, pending.TestInfo.Id);
    }
}