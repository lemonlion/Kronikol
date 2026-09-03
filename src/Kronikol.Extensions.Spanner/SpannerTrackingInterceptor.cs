using Google.Cloud.Spanner.V1;
using Google.Protobuf;
using Grpc.Core;
using Grpc.Core.Interceptors;
using Microsoft.AspNetCore.Http;
using Kronikol.Tracking;

namespace Kronikol.Extensions.Spanner;

/// <summary>
/// gRPC interceptor that tracks all Spanner operations at the transport level.
/// Captures operations regardless of whether the calling code uses SpannerConnection-specific
/// methods (CreateInsertCommand, CreateSelectCommand, etc.) or standard DbCommand.
/// </summary>
public class SpannerTrackingInterceptor : Interceptor, ITrackingComponent
{
    private readonly SpannerTracker _tracker;
    private readonly SpannerTrackingOptions _options;
    private int _invocationCount;

    public SpannerTrackingInterceptor(SpannerTrackingOptions options, IHttpContextAccessor? httpContextAccessor = null)
    {
        _options = options;
        _tracker = new SpannerTracker(options, httpContextAccessor);
    }

    public string ComponentName => $"SpannerTrackingInterceptor ({_options.ServiceName})";
    public bool WasInvoked => _invocationCount > 0;
    public int InvocationCount => _invocationCount;

    public override AsyncUnaryCall<TResponse> AsyncUnaryCall<TRequest, TResponse>(
        TRequest request,
        ClientInterceptorContext<TRequest, TResponse> context,
        AsyncUnaryCallContinuation<TRequest, TResponse> continuation)
    {
        Interlocked.Increment(ref _invocationCount);

        var methodName = ExtractMethodName(context.Method);
        var opInfo = ClassifyRequest(methodName, request);

        if (!ShouldTrack(opInfo))
            return continuation(request, context);

        var content = ExtractContent(methodName, request);
        var (reqId, traceId) = _tracker.LogRequest(opInfo, content);
        if (reqId == Guid.Empty)
            return continuation(request, context);

        var call = continuation(request, context);

        var wrappedResponse = WrapUnaryResponse(call.ResponseAsync, opInfo, reqId, traceId);

        return new AsyncUnaryCall<TResponse>(
            wrappedResponse,
            call.ResponseHeadersAsync,
            call.GetStatus,
            call.GetTrailers,
            call.Dispose);
    }

    public override AsyncServerStreamingCall<TResponse> AsyncServerStreamingCall<TRequest, TResponse>(
        TRequest request,
        ClientInterceptorContext<TRequest, TResponse> context,
        AsyncServerStreamingCallContinuation<TRequest, TResponse> continuation)
    {
        Interlocked.Increment(ref _invocationCount);

        var methodName = ExtractMethodName(context.Method);
        var opInfo = ClassifyRequest(methodName, request);

        if (!ShouldTrack(opInfo))
            return continuation(request, context);

        var content = ExtractContent(methodName, request);
        var (reqId, traceId) = _tracker.LogRequest(opInfo, content);
        if (reqId == Guid.Empty)
            return continuation(request, context);

        var call = continuation(request, context);

        if (_options.LogResponseContent)
        {
            var wrappedStream = new TrackingAsyncStreamReader<TResponse>(
                call.ResponseStream, _tracker, _options, opInfo, reqId, traceId);

            var originalDispose = call.Dispose;
            void WrappedDispose()
            {
                wrappedStream.LogIfNotAlreadyLogged();
                originalDispose();
            }

            return new AsyncServerStreamingCall<TResponse>(
                wrappedStream,
                call.ResponseHeadersAsync,
                call.GetStatus,
                call.GetTrailers,
                WrappedDispose);
        }

        // When response content logging is disabled, log immediately and return as-is
        _tracker.LogResponse(opInfo, reqId, traceId, null);
        return call;
    }

    public override AsyncClientStreamingCall<TRequest, TResponse> AsyncClientStreamingCall<TRequest, TResponse>(
        ClientInterceptorContext<TRequest, TResponse> context,
        AsyncClientStreamingCallContinuation<TRequest, TResponse> continuation)
    {
        Interlocked.Increment(ref _invocationCount);
        return continuation(context);
    }

    public override AsyncDuplexStreamingCall<TRequest, TResponse> AsyncDuplexStreamingCall<TRequest, TResponse>(
        ClientInterceptorContext<TRequest, TResponse> context,
        AsyncDuplexStreamingCallContinuation<TRequest, TResponse> continuation)
    {
        Interlocked.Increment(ref _invocationCount);
        return continuation(context);
    }

    // ─── Private helpers ────────────────────────────────────

    private bool ShouldTrack(SpannerOperationInfo opInfo) =>
        !_options.ExcludedOperations.Contains(opInfo.Operation);

    private static string ExtractMethodName<TRequest, TResponse>(Method<TRequest, TResponse> method)
        where TRequest : class where TResponse : class =>
        method.Name;

    private static SpannerOperationInfo ClassifyRequest<TRequest>(string methodName, TRequest request)
    {
        string? tableName = null;
        string? databaseId = null;
        string? sqlText = null;

        switch (request)
        {
            case ExecuteSqlRequest sql:
                sqlText = sql.Sql;
                databaseId = ExtractDatabaseId(sql.Session);
                // Use SQL classifier to get table name from SQL
                var sqlClassified = SpannerOperationClassifier.ClassifySql(sql.Sql);
                tableName = sqlClassified.TableName;
                break;
            case ReadRequest read:
                tableName = read.Table;
                databaseId = ExtractDatabaseId(read.Session);
                break;
            case CommitRequest commit:
                databaseId = ExtractDatabaseId(commit.Session);
                tableName = ExtractMutationTableName(commit);
                break;
            case ExecuteBatchDmlRequest batch:
                databaseId = ExtractDatabaseId(batch.Session);
                break;
            case BeginTransactionRequest txn:
                databaseId = ExtractDatabaseId(txn.Session);
                break;
            case RollbackRequest rollback:
                databaseId = ExtractDatabaseId(rollback.Session);
                break;
            case CreateSessionRequest create:
                databaseId = ExtractDatabaseIdFromDatabase(create.Database);
                break;
            case BatchCreateSessionsRequest batchCreate:
                databaseId = ExtractDatabaseIdFromDatabase(batchCreate.Database);
                break;
            case DeleteSessionRequest delete:
                databaseId = ExtractDatabaseId(delete.Name);
                break;
        }

        var grpcOp = SpannerOperationClassifier.ClassifyGrpc(methodName, tableName, databaseId);

        // Enrich with SQL text if available
        if (sqlText is not null)
            grpcOp = grpcOp with { SqlText = sqlText };

        // For Commit with mutations, reclassify based on mutation types
        if (request is CommitRequest commitReq && commitReq.Mutations.Count > 0)
            grpcOp = ClassifyCommitMutations(commitReq, grpcOp);

        return grpcOp;
    }

    private static SpannerOperationInfo ClassifyCommitMutations(CommitRequest commit, SpannerOperationInfo baseOp)
    {
        var mutation = commit.Mutations[0];
        var (operation, table) = mutation.OperationCase switch
        {
            Mutation.OperationOneofCase.Insert =>
                (SpannerOperation.Insert, mutation.Insert.Table),
            Mutation.OperationOneofCase.Update =>
                (SpannerOperation.Update, mutation.Update.Table),
            Mutation.OperationOneofCase.InsertOrUpdate =>
                (SpannerOperation.InsertOrUpdate, mutation.InsertOrUpdate.Table),
            Mutation.OperationOneofCase.Replace =>
                (SpannerOperation.Replace, mutation.Replace.Table),
            Mutation.OperationOneofCase.Delete =>
                (SpannerOperation.Delete, mutation.Delete.Table),
            _ => (SpannerOperation.Commit, baseOp.TableName)
        };

        return baseOp with { Operation = operation, TableName = table };
    }

    private string? ExtractContent<TRequest>(string methodName, TRequest request)
    {
        var effectiveVerbosity = PhaseConfiguration.GetEffectiveVerbosity(
            _options.Verbosity, _options.SetupVerbosity, _options.ActionVerbosity);

        if (effectiveVerbosity == SpannerTrackingVerbosity.Summarised)
            return null;

        return request switch
        {
            ExecuteSqlRequest sql => sql.Sql,
            ReadRequest read => $"Read table: {read.Table}",
            CommitRequest commit => FormatCommitContent(commit),
            ExecuteBatchDmlRequest batch => FormatBatchDmlContent(batch),
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

    private static string FormatBatchDmlContent(ExecuteBatchDmlRequest batch)
    {
        return string.Join("\n", batch.Statements.Select(s => s.Sql));
    }

    private async Task<TResponse> WrapUnaryResponse<TResponse>(
        Task<TResponse> responseTask,
        SpannerOperationInfo opInfo, Guid reqId, Guid traceId)
    {
        try
        {
            var response = await responseTask;
            var (responseContent, rawContent) = ExtractResponseContent(response);
            _tracker.LogResponse(opInfo, reqId, traceId, responseContent, rawContent);
            return response;
        }
        catch (RpcException)
        {
            _tracker.LogResponse(opInfo, reqId, traceId, null);
            throw;
        }
    }

    private (string? Content, string? RawContent) ExtractResponseContent<TResponse>(TResponse response)
    {
        if (!_options.LogResponseContent)
            return (null, null);

        var content = response switch
        {
            ResultSet rs => SpannerResponseFormatter.FormatResultSet(
                rs, _options.ResolveResponseDetail(), _options.MaxResponseRows),
            CommitResponse cr => SpannerResponseFormatter.FormatCommitResponse(cr),
            ExecuteBatchDmlResponse batch => SpannerResponseFormatter.FormatBatchDmlResponse(
                batch, _options.ResolveResponseDetail()),
            Transaction tx => $"Transaction: {tx.Id.ToBase64()}",
            _ => null
        };

        var rawContent = (_options.Verbosity == SpannerTrackingVerbosity.Raw
                         || _options.SetupVerbosity == SpannerTrackingVerbosity.Raw
                         || _options.ActionVerbosity == SpannerTrackingVerbosity.Raw)
            ? SpannerResponseFormatter.FormatRaw(response)
            : null;

        return (content, rawContent);
    }

    private static string? ExtractMutationTableName(CommitRequest commit)
    {
        if (commit.Mutations.Count == 0) return null;
        var m = commit.Mutations[0];
        return m.OperationCase switch
        {
            Mutation.OperationOneofCase.Insert => m.Insert.Table,
            Mutation.OperationOneofCase.Update => m.Update.Table,
            Mutation.OperationOneofCase.InsertOrUpdate => m.InsertOrUpdate.Table,
            Mutation.OperationOneofCase.Replace => m.Replace.Table,
            Mutation.OperationOneofCase.Delete => m.Delete.Table,
            _ => null
        };
    }

    /// <summary>
    /// Extracts database ID from a session resource name:
    /// projects/{project}/instances/{instance}/databases/{database}/sessions/{session}
    /// </summary>
    private static string? ExtractDatabaseId(string? sessionName)
    {
        if (string.IsNullOrEmpty(sessionName)) return null;
        var parts = sessionName.Split('/');
        // Format: projects/P/instances/I/databases/D/sessions/S
        var dbIdx = Array.IndexOf(parts, "databases");
        return dbIdx >= 0 && dbIdx + 1 < parts.Length ? parts[dbIdx + 1] : null;
    }

    /// <summary>
    /// Extracts database ID from a database resource name:
    /// projects/{project}/instances/{instance}/databases/{database}
    /// </summary>
    private static string? ExtractDatabaseIdFromDatabase(string? databaseName)
    {
        if (string.IsNullOrEmpty(databaseName)) return null;
        var parts = databaseName.Split('/');
        var dbIdx = Array.IndexOf(parts, "databases");
        return dbIdx >= 0 && dbIdx + 1 < parts.Length ? parts[dbIdx + 1] : null;
    }
}
