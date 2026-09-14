using Kronikol.Constants;
using Google.Cloud.Spanner.V1;
using Google.Protobuf;
using Grpc.Core;
using Microsoft.AspNetCore.Http;
using Kronikol.Tracking;

namespace Kronikol.Extensions.Spanner;

/// <summary>
/// Tracks Google Cloud Spanner operations by intercepting client interactions for test visualization.
/// </summary>
public class SpannerTracker : ITrackingComponent
{
    private readonly SpannerTrackingOptions _options;
    private readonly IHttpContextAccessor? _httpContextAccessor;
    private int _invocationCount;

    public SpannerTracker(SpannerTrackingOptions options, IHttpContextAccessor? httpContextAccessor = null)
    {
        _options = options;
        _httpContextAccessor = httpContextAccessor;
        TrackingComponentRegistry.Register(this);
    }

    public string ComponentName => $"SpannerTracker ({_options.ServiceName})";
    public bool WasInvoked => _invocationCount > 0;
    public int InvocationCount => _invocationCount;
    public bool HasHttpContextAccessor => _httpContextAccessor is not null;
    internal SpannerTrackingOptions Options => _options;

    public (Guid RequestResponseId, Guid TraceId) LogRequest(
        SpannerOperationInfo operation, string? content, string? rawContent = null)
    {
        if (!PhaseConfiguration.ShouldTrack(_options.TrackDuringSetup, _options.TrackDuringAction))
            return (Guid.Empty, Guid.Empty);

        if (_options.ExcludedOperations.Contains(operation.Operation))
            return (Guid.Empty, Guid.Empty);

        Interlocked.Increment(ref _invocationCount);

        var testInfo = TestInfoResolver.ResolveWithSource(_httpContextAccessor, _options.CurrentTestInfoFetcher);
        if (testInfo is null) return (Guid.Empty, Guid.Empty);

        var effectiveVerbosity = PhaseConfiguration.GetEffectiveVerbosity(
            _options.Verbosity, _options.SetupVerbosity, _options.ActionVerbosity);

        var uri = BuildUri(operation, effectiveVerbosity);
        var label = SpannerOperationClassifier.GetDiagramLabel(operation, effectiveVerbosity);
        OneOf<HttpMethod, string> method = effectiveVerbosity == SpannerTrackingVerbosity.Raw
            ? SpannerOperationClassifier.GetRawKeyword(operation) ?? "Spanner"
            : label;
        var traceId = Guid.NewGuid();
        var requestResponseId = Guid.NewGuid();

        var logContent = effectiveVerbosity == SpannerTrackingVerbosity.Summarised ? null :
            effectiveVerbosity == SpannerTrackingVerbosity.Raw ? (rawContent ?? content) : content;

        RequestResponseLogger.Log(new RequestResponseLog(
            testInfo.Value.Name, testInfo.Value.Id,
            method, logContent, uri, [],
            _options.ServiceName, _options.CallerName,
            RequestResponseType.Request, traceId, requestResponseId, false,
            MetaType: RequestResponseMetaType.Event,
            DependencyCategory: DependencyCategories.Spanner)
        {
            AttributionSource = testInfo.Value.Source,
            Phase = TestPhaseContext.Current
        }.WithVariants(_options.Verbosity, _options.SetupVerbosity, _options.ActionVerbosity,
            v => new PhaseVariant(
                v == SpannerTrackingVerbosity.Raw
                    ? SpannerOperationClassifier.GetRawKeyword(operation) ?? "Spanner"
                    : SpannerOperationClassifier.GetDiagramLabel(operation, v),
                BuildUri(operation, v),
                v == SpannerTrackingVerbosity.Summarised ? null :
                    v == SpannerTrackingVerbosity.Raw ? (rawContent ?? content) : content,
                [], false)));

        return (requestResponseId, traceId);
    }

    public void LogResponse(
        SpannerOperationInfo operation,
        Guid requestResponseId, Guid traceId, string? content, string? rawContent = null)
    {
        if (!PhaseConfiguration.ShouldTrack(_options.TrackDuringSetup, _options.TrackDuringAction)) return;
        if (_options.ExcludedOperations.Contains(operation.Operation)) return;

        var testInfo = TestInfoResolver.ResolveWithSource(_httpContextAccessor, _options.CurrentTestInfoFetcher);
        if (testInfo is null) return;

        var effectiveVerbosity = PhaseConfiguration.GetEffectiveVerbosity(
            _options.Verbosity, _options.SetupVerbosity, _options.ActionVerbosity);

        var uri = BuildUri(operation, effectiveVerbosity);
        var label = SpannerOperationClassifier.GetDiagramLabel(operation, effectiveVerbosity);
        OneOf<HttpMethod, string> method = effectiveVerbosity == SpannerTrackingVerbosity.Raw
            ? SpannerOperationClassifier.GetRawKeyword(operation) ?? "Spanner"
            : label;

        var logContent = effectiveVerbosity == SpannerTrackingVerbosity.Summarised && !_options.LogResponseContent ? null :
            effectiveVerbosity == SpannerTrackingVerbosity.Raw ? (rawContent ?? content) : content;

        RequestResponseLogger.Log(new RequestResponseLog(
            testInfo.Value.Name, testInfo.Value.Id,
            method, logContent, uri, [],
            _options.ServiceName, _options.CallerName,
            RequestResponseType.Response, traceId, requestResponseId, false,
            DependencyCategory: DependencyCategories.Spanner)
        {
            AttributionSource = testInfo.Value.Source,
            Phase = TestPhaseContext.Current
        }.WithVariants(_options.Verbosity, _options.SetupVerbosity, _options.ActionVerbosity,
            v => new PhaseVariant(
                v == SpannerTrackingVerbosity.Raw
                    ? SpannerOperationClassifier.GetRawKeyword(operation) ?? "Spanner"
                    : SpannerOperationClassifier.GetDiagramLabel(operation, v),
                BuildUri(operation, v),
                v == SpannerTrackingVerbosity.Summarised && !_options.LogResponseContent ? null :
                    v == SpannerTrackingVerbosity.Raw ? (rawContent ?? content) : content,
                [], false)));
    }

    private static Uri BuildUri(SpannerOperationInfo op, SpannerTrackingVerbosity effectiveVerbosity)
    {
        var db = op.DatabaseId ?? "unknown";
        var table = op.TableName;

        return effectiveVerbosity switch
        {
            SpannerTrackingVerbosity.Raw when op.DatabaseId is not null =>
                new Uri($"spanner:///{op.DatabaseId}" + (table is not null ? $"/{table}" : "")),
            SpannerTrackingVerbosity.Raw =>
                new Uri($"spanner:///unknown" + (table is not null ? $"/{table}" : "")),
            _ when table is not null =>
                new Uri($"spanner:///{table}"),
            _ =>
                new Uri($"spanner:///{db}")
        };
    }

    /// <summary>
    /// Creates a pair of observer delegates for wiring to a Spanner server's
    /// OnRequestReceived/OnResponseSent callbacks. The delegates use standard types
    /// (string, IMessage, DateTimeOffset, etc.) — no dependency on any emulator package.
    /// </summary>
    /// <returns>
    /// A tuple of (onRequest, onResponse) delegates that can be assigned to server callbacks.
    /// </returns>
    public static (
        Action<string, IMessage, DateTimeOffset> OnRequest,
        Action<string, IMessage, IMessage?, TimeSpan, StatusCode?, DateTimeOffset> OnResponse
    ) CreateServerObservers(SpannerTracker tracker)
    {
        // Track request-response ID pairs keyed by a per-call correlation
        var pendingRequests = new System.Collections.Concurrent.ConcurrentDictionary<string, (Guid ReqId, Guid TraceId, SpannerOperationInfo Op)>();

        void OnRequest(string methodName, IMessage request, DateTimeOffset timestamp)
        {
            var opInfo = ClassifyServerRequest(methodName, request);
            var content = ExtractServerContent(methodName, request, tracker._options);
            var (reqId, traceId) = tracker.LogRequest(opInfo, content);
            if (reqId != Guid.Empty)
            {
                // Store for pairing with response using a correlation key
                var key = $"{methodName}:{timestamp.Ticks}:{request.GetHashCode()}";
                pendingRequests[key] = (reqId, traceId, opInfo);
            }
        }

        void OnResponse(string methodName, IMessage request, IMessage? response,
            TimeSpan duration, StatusCode? statusCode, DateTimeOffset timestamp)
        {
            // Try to find the matching request
            // Walk backwards through pending requests for this method
            (Guid ReqId, Guid TraceId, SpannerOperationInfo Op)? matched = null;
            foreach (var kvp in pendingRequests)
            {
                if (kvp.Key.StartsWith($"{methodName}:"))
                {
                    if (pendingRequests.TryRemove(kvp.Key, out var value))
                    {
                        matched = value;
                        break;
                    }
                }
            }

            if (matched is not null)
            {
                tracker.LogResponse(matched.Value.Op, matched.Value.ReqId, matched.Value.TraceId, null);
            }
            else
            {
                // Fallback: log as standalone if we can't match
                var opInfo = ClassifyServerRequest(methodName, request);
                tracker.LogResponse(opInfo, Guid.Empty, Guid.Empty, null);
            }
        }

        return (OnRequest, OnResponse);
    }

    private static SpannerOperationInfo ClassifyServerRequest(string methodName, IMessage request)
    {
        string? tableName = null;
        string? databaseId = null;
        string? sqlText = null;

        switch (request)
        {
            case ExecuteSqlRequest sql:
                sqlText = sql.Sql;
                var sqlClassified = SpannerOperationClassifier.ClassifySql(sql.Sql);
                tableName = sqlClassified.TableName;
                break;
            case ReadRequest read:
                tableName = read.Table;
                break;
            case CommitRequest commit when commit.Mutations.Count > 0:
                var m = commit.Mutations[0];
                tableName = m.OperationCase switch
                {
                    Mutation.OperationOneofCase.Insert => m.Insert.Table,
                    Mutation.OperationOneofCase.Update => m.Update.Table,
                    Mutation.OperationOneofCase.InsertOrUpdate => m.InsertOrUpdate.Table,
                    Mutation.OperationOneofCase.Replace => m.Replace.Table,
                    Mutation.OperationOneofCase.Delete => m.Delete.Table,
                    _ => null
                };
                break;
        }

        var op = SpannerOperationClassifier.ClassifyGrpc(methodName, tableName, databaseId);
        if (sqlText is not null) op = op with { SqlText = sqlText };

        // Reclassify Commit with mutations to specific operation type
        if (request is CommitRequest commitReq && commitReq.Mutations.Count > 0)
        {
            var mutation = commitReq.Mutations[0];
            var (operation, table) = mutation.OperationCase switch
            {
                Mutation.OperationOneofCase.Insert => (SpannerOperation.Insert, mutation.Insert.Table),
                Mutation.OperationOneofCase.Update => (SpannerOperation.Update, mutation.Update.Table),
                Mutation.OperationOneofCase.InsertOrUpdate => (SpannerOperation.InsertOrUpdate, mutation.InsertOrUpdate.Table),
                Mutation.OperationOneofCase.Replace => (SpannerOperation.Replace, mutation.Replace.Table),
                Mutation.OperationOneofCase.Delete => (SpannerOperation.Delete, mutation.Delete.Table),
                _ => (SpannerOperation.Commit, op.TableName)
            };
            op = op with { Operation = operation, TableName = table };
        }

        return op;
    }

    private static string? ExtractServerContent(string methodName, IMessage request, SpannerTrackingOptions options)
    {
        var effectiveVerbosity = PhaseConfiguration.GetEffectiveVerbosity(
            options.Verbosity, options.SetupVerbosity, options.ActionVerbosity);

        if (effectiveVerbosity == SpannerTrackingVerbosity.Summarised)
            return null;

        return request switch
        {
            ExecuteSqlRequest sql => sql.Sql,
            ReadRequest read => $"Read table: {read.Table}",
            CommitRequest commit => FormatCommitContent(commit),
            ExecuteBatchDmlRequest batch => string.Join("\n", batch.Statements.Select(s => s.Sql)),
            _ => null
        };
    }

    private static string FormatCommitContent(CommitRequest commit)
    {
        if (commit.Mutations.Count == 0) return "Commit (no mutations)";

        var parts = commit.Mutations.Select(m => m.OperationCase switch
        {
            Mutation.OperationOneofCase.Insert => $"INSERT {m.Insert.Table}",
            Mutation.OperationOneofCase.Update => $"UPDATE {m.Update.Table}",
            Mutation.OperationOneofCase.InsertOrUpdate => $"UPSERT {m.InsertOrUpdate.Table}",
            Mutation.OperationOneofCase.Replace => $"REPLACE {m.Replace.Table}",
            Mutation.OperationOneofCase.Delete => $"DELETE {m.Delete.Table}",
            _ => "Unknown mutation"
        });

        return string.Join("; ", parts);
    }
}