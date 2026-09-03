using Kronikol.Constants;
using System.Data;
using System.Data.Common;
using System.Diagnostics.CodeAnalysis;
using System.Net;
using Microsoft.AspNetCore.Http;
using Kronikol.Sql;
using Kronikol.Tracking;

namespace Kronikol;

/// <summary>
/// Tracking decorator for Dapper database operations.
/// </summary>
public class TrackingDbCommand : DbCommand
{
    private readonly DbCommand _inner;
    private readonly TrackingDbConnection _connection;
    private readonly DapperTrackingOptions _options;
    private readonly IHttpContextAccessor? _httpContextAccessor;

    public TrackingDbCommand(DbCommand inner, TrackingDbConnection connection, DapperTrackingOptions options, IHttpContextAccessor? httpContextAccessor = null)
    {
        _inner = inner;
        _connection = connection;
        _options = options;
        _httpContextAccessor = httpContextAccessor;
    }

    [AllowNull]
    public override string CommandText
    {
        get => _inner.CommandText;
        set => _inner.CommandText = value;
    }

    public override int CommandTimeout
    {
        get => _inner.CommandTimeout;
        set => _inner.CommandTimeout = value;
    }

    public override CommandType CommandType
    {
        get => _inner.CommandType;
        set => _inner.CommandType = value;
    }

    public override bool DesignTimeVisible
    {
        get => _inner.DesignTimeVisible;
        set => _inner.DesignTimeVisible = value;
    }

    public override UpdateRowSource UpdatedRowSource
    {
        get => _inner.UpdatedRowSource;
        set => _inner.UpdatedRowSource = value;
    }

    protected override DbConnection? DbConnection
    {
        get => _connection;
        set { /* connection is set via constructor */ }
    }

    protected override DbTransaction? DbTransaction
    {
        get => _inner.Transaction;
        set => _inner.Transaction = value;
    }

    protected override DbParameterCollection DbParameterCollection => _inner.Parameters;

    protected override DbParameter CreateDbParameter() => _inner.CreateParameter();
    public override void Prepare() => _inner.Prepare();
    public override void Cancel() => _inner.Cancel();

    protected override DbDataReader ExecuteDbDataReader(CommandBehavior behavior)
    {
        var ids = LogRequest();
        var result = _inner.ExecuteReader(behavior);
        if (ids is not null)
        {
            if (_options.LogResponseContent)
                return WrapReader(result, ids.Value.TraceId, ids.Value.RequestResponseId);
            LogResponse(ids.Value.TraceId, ids.Value.RequestResponseId);
        }
        return result;
    }

    protected override async Task<DbDataReader> ExecuteDbDataReaderAsync(
        CommandBehavior behavior, CancellationToken cancellationToken)
    {
        var ids = LogRequest();
        var result = await _inner.ExecuteReaderAsync(behavior, cancellationToken);
        if (ids is not null)
        {
            if (_options.LogResponseContent)
                return WrapReader(result, ids.Value.TraceId, ids.Value.RequestResponseId);
            LogResponse(ids.Value.TraceId, ids.Value.RequestResponseId);
        }
        return result;
    }

    public override int ExecuteNonQuery()
    {
        var ids = LogRequest();
        var result = _inner.ExecuteNonQuery();
        if (ids is not null) LogResponse(ids.Value.TraceId, ids.Value.RequestResponseId, result);
        return result;
    }

    public override async Task<int> ExecuteNonQueryAsync(CancellationToken cancellationToken)
    {
        var ids = LogRequest();
        var result = await _inner.ExecuteNonQueryAsync(cancellationToken);
        if (ids is not null) LogResponse(ids.Value.TraceId, ids.Value.RequestResponseId, result);
        return result;
    }

    public override object? ExecuteScalar()
    {
        var ids = LogRequest();
        var result = _inner.ExecuteScalar();
        if (ids is not null) LogResponseWithContent(ids.Value.TraceId, ids.Value.RequestResponseId, FormatScalar(result));
        return result;
    }

    public override async Task<object?> ExecuteScalarAsync(CancellationToken cancellationToken)
    {
        var ids = LogRequest();
        var result = await _inner.ExecuteScalarAsync(cancellationToken);
        if (ids is not null) LogResponseWithContent(ids.Value.TraceId, ids.Value.RequestResponseId, FormatScalar(result));
        return result;
    }

    internal (Guid TraceId, Guid RequestResponseId)? LogRequest()
    {
        _connection.IncrementInvocationCount();

        if (!PhaseConfiguration.ShouldTrack(_options.TrackDuringSetup, _options.TrackDuringAction))
            return null;
        var effectiveVerbosity = PhaseConfiguration.GetEffectiveVerbosity(_options.Verbosity, _options.SetupVerbosity, _options.ActionVerbosity);

        var op = DapperOperationClassifier.Classify(CommandText, CommandType);
        if (_options.ExcludedOperations.Contains(op.Operation)) return null;

        var testInfo = TestInfoResolver.Resolve(_httpContextAccessor, _options.CurrentTestInfoFetcher);
        if (testInfo is null) return null;

        var traceId = Guid.NewGuid();
        var requestResponseId = Guid.NewGuid();

        var label = DapperOperationClassifier.GetDiagramLabel(op, effectiveVerbosity);
        OneOf<System.Net.Http.HttpMethod, string> method = effectiveVerbosity == DapperTrackingVerbosity.Raw
            ? DapperOperationClassifier.GetRawKeyword(CommandText) ?? "SQL"
            : label;
        var uri = BuildUri(op, effectiveVerbosity);
        var content = effectiveVerbosity == DapperTrackingVerbosity.Raw
            ? BuildParameterizedContent()
            : _options.LogSqlText ? CommandText : null;

        RequestResponseLogger.Log(new RequestResponseLog(
            testInfo.Value.Name,
            testInfo.Value.Id,
            method,
            content,
            uri,
            [],
            _options.ServiceName,
            _options.CallerName,
            RequestResponseType.Request,
            traceId,
            requestResponseId,
            false,
            DependencyCategory: DependencyCategories.SQL)
        {
            Phase = TestPhaseContext.Current
        }.WithVariants(_options.Verbosity, _options.SetupVerbosity, _options.ActionVerbosity,
            v =>
            {
                OneOf<System.Net.Http.HttpMethod, string> vMethod = v == DapperTrackingVerbosity.Raw
                    ? DapperOperationClassifier.GetRawKeyword(CommandText) ?? "SQL"
                    : DapperOperationClassifier.GetDiagramLabel(op, v);
                var vContent = v == DapperTrackingVerbosity.Raw
                    ? BuildParameterizedContent()
                    : _options.LogSqlText ? CommandText : null;
                return new PhaseVariant(
                    vMethod, BuildUri(op, v), vContent, [], false);
            }));

        return (traceId, requestResponseId);
    }

    internal void LogResponse(Guid traceId, Guid requestResponseId, int? rowsAffected = null)
    {
        var effectiveVerbosity = PhaseConfiguration.GetEffectiveVerbosity(_options.Verbosity, _options.SetupVerbosity, _options.ActionVerbosity);

        var op = DapperOperationClassifier.Classify(CommandText, CommandType);
        if (_options.ExcludedOperations.Contains(op.Operation)) return;

        var testInfo = TestInfoResolver.Resolve(_httpContextAccessor, _options.CurrentTestInfoFetcher);
        if (testInfo is null) return;

        var label = DapperOperationClassifier.GetDiagramLabel(op, effectiveVerbosity);
        OneOf<System.Net.Http.HttpMethod, string> method = effectiveVerbosity == DapperTrackingVerbosity.Raw
            ? DapperOperationClassifier.GetRawKeyword(CommandText) ?? "SQL"
            : label;
        var uri = BuildUri(op, effectiveVerbosity);
        var responseContent = effectiveVerbosity == DapperTrackingVerbosity.Summarised && !_options.LogResponseContent
            ? null
            : rowsAffected.HasValue ? $"{rowsAffected.Value} rows affected" : null;

        RequestResponseLogger.Log(new RequestResponseLog(
            testInfo.Value.Name,
            testInfo.Value.Id,
            method,
            responseContent,
            uri,
            [],
            _options.ServiceName,
            _options.CallerName,
            RequestResponseType.Response,
            traceId,
            requestResponseId,
            false,
            (OneOf<HttpStatusCode, string>)"OK",
            DependencyCategory: DependencyCategories.SQL)
        {
            Phase = TestPhaseContext.Current
        }.WithVariants(_options.Verbosity, _options.SetupVerbosity, _options.ActionVerbosity,
            v =>
            {
                OneOf<System.Net.Http.HttpMethod, string> vMethod = v == DapperTrackingVerbosity.Raw
                    ? DapperOperationClassifier.GetRawKeyword(CommandText) ?? "SQL"
                    : DapperOperationClassifier.GetDiagramLabel(op, v);
                var vContent = v == DapperTrackingVerbosity.Summarised && !_options.LogResponseContent
                    ? null
                    : rowsAffected.HasValue ? $"{rowsAffected.Value} rows affected" : null;
                return new PhaseVariant(
                    vMethod, BuildUri(op, v), vContent, [], false);
            }));
    }

    private void LogResponseWithContent(Guid traceId, Guid requestResponseId, string? content)
    {
        var effectiveVerbosity = PhaseConfiguration.GetEffectiveVerbosity(_options.Verbosity, _options.SetupVerbosity, _options.ActionVerbosity);

        var op = DapperOperationClassifier.Classify(CommandText, CommandType);
        if (_options.ExcludedOperations.Contains(op.Operation)) return;

        var testInfo = TestInfoResolver.Resolve(_httpContextAccessor, _options.CurrentTestInfoFetcher);
        if (testInfo is null) return;

        var label = DapperOperationClassifier.GetDiagramLabel(op, effectiveVerbosity);
        OneOf<System.Net.Http.HttpMethod, string> method = effectiveVerbosity == DapperTrackingVerbosity.Raw
            ? DapperOperationClassifier.GetRawKeyword(CommandText) ?? "SQL"
            : label;
        var uri = BuildUri(op, effectiveVerbosity);
        var responseContent = effectiveVerbosity == DapperTrackingVerbosity.Summarised && !_options.LogResponseContent ? null : content;

        RequestResponseLogger.Log(new RequestResponseLog(
            testInfo.Value.Name,
            testInfo.Value.Id,
            method,
            responseContent,
            uri,
            [],
            _options.ServiceName,
            _options.CallerName,
            RequestResponseType.Response,
            traceId,
            requestResponseId,
            false,
            (OneOf<HttpStatusCode, string>)"OK",
            DependencyCategory: DependencyCategories.SQL)
        {
            Phase = TestPhaseContext.Current
        }.WithVariants(_options.Verbosity, _options.SetupVerbosity, _options.ActionVerbosity,
            v =>
            {
                OneOf<System.Net.Http.HttpMethod, string> vMethod = v == DapperTrackingVerbosity.Raw
                    ? DapperOperationClassifier.GetRawKeyword(CommandText) ?? "SQL"
                    : DapperOperationClassifier.GetDiagramLabel(op, v);
                var vContent = v == DapperTrackingVerbosity.Summarised && !_options.LogResponseContent ? null : content;
                return new PhaseVariant(
                    vMethod, BuildUri(op, v), vContent, [], false);
            }));
    }

    private TrackingDbDataReader WrapReader(DbDataReader inner, Guid traceId, Guid requestResponseId)
    {
        var effectiveVerbosity = PhaseConfiguration.GetEffectiveVerbosity(
            _options.Verbosity, _options.SetupVerbosity, _options.ActionVerbosity);
        var detail = SqlResponseDetailResolver.Resolve(
            _options.ResponseDetail, effectiveVerbosity == DapperTrackingVerbosity.Summarised);
        return new TrackingDbDataReader(
            inner, detail, _options.MaxResponseRows, _options.MaxValueDisplayLength,
            content => LogResponseWithContent(traceId, requestResponseId, content));
    }

    private string? FormatScalar(object? result)
    {
        if (!_options.LogResponseContent) return null;
        if (result is null or DBNull) return "null";
        var str = result.ToString() ?? "";
        return str.Length > _options.MaxValueDisplayLength
            ? $"{str[.._options.MaxValueDisplayLength]}... ({str.Length} chars)"
            : str;
    }

    private Uri BuildUri(DapperOperationInfo op, DapperTrackingVerbosity verbosity)
    {
        var database = _connection.InnerConnection.Database;
        var dataSource = _connection.InnerConnection.DataSource;

        if (string.IsNullOrEmpty(database)) database = "unknown";
        if (string.IsNullOrEmpty(dataSource)) dataSource = "localhost";

        return verbosity switch
        {
            DapperTrackingVerbosity.Raw => new Uri($"sql://{dataSource}/{database}"),
            DapperTrackingVerbosity.Summarised => op.TableName is not null
                ? new Uri($"sql:///{database}/{op.TableName}")
                : new Uri($"sql:///{database}"),
            _ => op.TableName is not null
                ? new Uri($"sql:///{dataSource}/{database}/{op.TableName}")
                : new Uri($"sql:///{dataSource}/{database}")
        };
    }

    private string? BuildParameterizedContent()
    {
        if (!_options.LogParameters || _inner.Parameters.Count == 0)
            return CommandText;

        var paramStr = string.Join(", ", _inner.Parameters
            .Cast<DbParameter>()
            .Select(p => $"{p.ParameterName}={p.Value}"));

        return $"{CommandText}\n-- Parameters: {paramStr}";
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing) _inner.Dispose();
        base.Dispose(disposing);
    }

    public override async ValueTask DisposeAsync()
    {
        await _inner.DisposeAsync();
        await base.DisposeAsync();
    }
}
