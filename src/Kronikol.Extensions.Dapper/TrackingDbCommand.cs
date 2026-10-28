using System.Globalization;
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
        _httpContextAccessor = httpContextAccessor ?? options.HttpContextAccessor;
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
        DbDataReader result;
        try
        {
            result = _inner.ExecuteReader(behavior);
        }
        catch (Exception ex)
        {
            LogFailure(ids, ex);
            throw;
        }
        if (ids is not null)
        {
            if (_options.LogResponseContent)
                return WrapReader(result, ids.Value.TraceId, ids.Value.RequestResponseId, behavior);
            LogResponse(ids.Value.TraceId, ids.Value.RequestResponseId);
        }
        return result;
    }

    protected override async Task<DbDataReader> ExecuteDbDataReaderAsync(
        CommandBehavior behavior, CancellationToken cancellationToken)
    {
        var ids = LogRequest();
        DbDataReader result;
        try
        {
            result = await _inner.ExecuteReaderAsync(behavior, cancellationToken);
        }
        catch (Exception ex)
        {
            LogFailure(ids, ex);
            throw;
        }
        if (ids is not null)
        {
            if (_options.LogResponseContent)
                return WrapReader(result, ids.Value.TraceId, ids.Value.RequestResponseId, behavior);
            LogResponse(ids.Value.TraceId, ids.Value.RequestResponseId);
        }
        return result;
    }

    public override int ExecuteNonQuery()
    {
        var ids = LogRequest();
        int result;
        try
        {
            result = _inner.ExecuteNonQuery();
        }
        catch (Exception ex)
        {
            LogFailure(ids, ex);
            throw;
        }
        if (ids is not null) LogResponse(ids.Value.TraceId, ids.Value.RequestResponseId, result);
        return result;
    }

    public override async Task<int> ExecuteNonQueryAsync(CancellationToken cancellationToken)
    {
        var ids = LogRequest();
        int result;
        try
        {
            result = await _inner.ExecuteNonQueryAsync(cancellationToken);
        }
        catch (Exception ex)
        {
            LogFailure(ids, ex);
            throw;
        }
        if (ids is not null) LogResponse(ids.Value.TraceId, ids.Value.RequestResponseId, result);
        return result;
    }

    public override object? ExecuteScalar()
    {
        var ids = LogRequest();
        object? result;
        try
        {
            result = _inner.ExecuteScalar();
        }
        catch (Exception ex)
        {
            LogFailure(ids, ex);
            throw;
        }
        if (ids is not null) LogResponseWithContent(ids.Value.TraceId, ids.Value.RequestResponseId, FormatScalar(result));
        return result;
    }

    public override async Task<object?> ExecuteScalarAsync(CancellationToken cancellationToken)
    {
        var ids = LogRequest();
        object? result;
        try
        {
            result = await _inner.ExecuteScalarAsync(cancellationToken);
        }
        catch (Exception ex)
        {
            LogFailure(ids, ex);
            throw;
        }
        if (ids is not null) LogResponseWithContent(ids.Value.TraceId, ids.Value.RequestResponseId, FormatScalar(result));
        return result;
    }

    // A statement that fails still gets its response, with the error: without one the diagram shows a call
    // that never answered, and the failure appears nowhere in the report.
    private void LogFailure((Guid TraceId, Guid RequestResponseId)? ids, Exception exception)
    {
        if (ids is not null) LogResponse(ids.Value.TraceId, ids.Value.RequestResponseId, exception: exception);
    }

    internal (Guid TraceId, Guid RequestResponseId)? LogRequest()
    {
        _connection.IncrementInvocationCount();
        try
        {
            return RequestLogged();
        }
        catch (Exception ex)
        {
            NotRecorded(ex);
            return null;
        }
    }

    private (Guid TraceId, Guid RequestResponseId)? RequestLogged()
    {
        if (!PhaseConfiguration.ShouldTrack(_options.TrackDuringSetup, _options.TrackDuringAction))
            return null;
        var effectiveVerbosity = PhaseConfiguration.GetEffectiveVerbosity(_options.Verbosity, _options.SetupVerbosity, _options.ActionVerbosity);

        var op = DapperOperationClassifier.Classify(CommandText, CommandType);
        if (_options.ExcludedOperations.Contains(op.Operation)) return null;

        var testInfo = TestInfoResolver.ResolveWithSource(_httpContextAccessor, _options.CurrentTestInfoFetcher);
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
            AttributionSource = testInfo.Value.Source,
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

    internal void LogResponse(Guid traceId, Guid requestResponseId, int? rowsAffected = null, Exception? exception = null)
    {
        try
        {
            ResponseLogged(traceId, requestResponseId, rowsAffected, exception);
        }
        catch (Exception ex)
        {
            NotRecorded(ex);
        }
    }

    private void ResponseLogged(Guid traceId, Guid requestResponseId, int? rowsAffected, Exception? exception)
    {
        var effectiveVerbosity = PhaseConfiguration.GetEffectiveVerbosity(_options.Verbosity, _options.SetupVerbosity, _options.ActionVerbosity);

        var op = DapperOperationClassifier.Classify(CommandText, CommandType);
        if (_options.ExcludedOperations.Contains(op.Operation)) return;

        var testInfo = TestInfoResolver.ResolveWithSource(_httpContextAccessor, _options.CurrentTestInfoFetcher);
        if (testInfo is null) return;

        var label = DapperOperationClassifier.GetDiagramLabel(op, effectiveVerbosity);
        OneOf<System.Net.Http.HttpMethod, string> method = effectiveVerbosity == DapperTrackingVerbosity.Raw
            ? DapperOperationClassifier.GetRawKeyword(CommandText) ?? "SQL"
            : label;
        var uri = BuildUri(op, effectiveVerbosity);
        var responseContent = effectiveVerbosity == DapperTrackingVerbosity.Summarised && !_options.LogResponseContent
            ? null
            : exception is not null ? exception.Message : rowsAffected.HasValue ? $"{rowsAffected.Value.ToString(CultureInfo.InvariantCulture)} rows affected" : null;

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
            (OneOf<HttpStatusCode, string>)(exception is not null ? "Error" : "OK"),
            DependencyCategory: DependencyCategories.SQL)
        {
            AttributionSource = testInfo.Value.Source,
            Phase = TestPhaseContext.Current
        }.WithVariants(_options.Verbosity, _options.SetupVerbosity, _options.ActionVerbosity,
            v =>
            {
                OneOf<System.Net.Http.HttpMethod, string> vMethod = v == DapperTrackingVerbosity.Raw
                    ? DapperOperationClassifier.GetRawKeyword(CommandText) ?? "SQL"
                    : DapperOperationClassifier.GetDiagramLabel(op, v);
                var vContent = v == DapperTrackingVerbosity.Summarised && !_options.LogResponseContent
                    ? null
                    : exception is not null ? exception.Message : rowsAffected.HasValue ? $"{rowsAffected.Value.ToString(CultureInfo.InvariantCulture)} rows affected" : null;
                return new PhaseVariant(
                    vMethod, BuildUri(op, v), vContent, [], false);
            }));
    }

    private void LogResponseWithContent(Guid traceId, Guid requestResponseId, string? content) =>
        LogResponseWithContent(traceId, requestResponseId, _ => content);

    // A reader's rows, kept at the most detail any verbosity the call may be drawn at shows: each note is made at the
    // detail its own verbosity resolves to.
    private void LogRowsResponse(Guid traceId, Guid requestResponseId, Func<SqlResponseDetail, string> rowsAt) =>
        LogResponseWithContent(traceId, requestResponseId, verbosity => rowsAt(SqlResponseDetailResolver.Resolve(
            _options.ResponseDetail, verbosity == DapperTrackingVerbosity.Summarised)));

    private void LogResponseWithContent(Guid traceId, Guid requestResponseId, Func<DapperTrackingVerbosity, string?> contentAt)
    {
        try
        {
            ResponseLoggedWithContent(traceId, requestResponseId, contentAt);
        }
        catch (Exception ex)
        {
            NotRecorded(ex);
        }
    }

    private void ResponseLoggedWithContent(Guid traceId, Guid requestResponseId, Func<DapperTrackingVerbosity, string?> contentAt)
    {
        var effectiveVerbosity = PhaseConfiguration.GetEffectiveVerbosity(_options.Verbosity, _options.SetupVerbosity, _options.ActionVerbosity);

        var op = DapperOperationClassifier.Classify(CommandText, CommandType);
        if (_options.ExcludedOperations.Contains(op.Operation)) return;

        var testInfo = TestInfoResolver.ResolveWithSource(_httpContextAccessor, _options.CurrentTestInfoFetcher);
        if (testInfo is null) return;

        var label = DapperOperationClassifier.GetDiagramLabel(op, effectiveVerbosity);
        OneOf<System.Net.Http.HttpMethod, string> method = effectiveVerbosity == DapperTrackingVerbosity.Raw
            ? DapperOperationClassifier.GetRawKeyword(CommandText) ?? "SQL"
            : label;
        var uri = BuildUri(op, effectiveVerbosity);
        var responseContent = effectiveVerbosity == DapperTrackingVerbosity.Summarised && !_options.LogResponseContent ? null : contentAt(effectiveVerbosity);

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
            AttributionSource = testInfo.Value.Source,
            Phase = TestPhaseContext.Current
        }.WithVariants(_options.Verbosity, _options.SetupVerbosity, _options.ActionVerbosity,
            v =>
            {
                OneOf<System.Net.Http.HttpMethod, string> vMethod = v == DapperTrackingVerbosity.Raw
                    ? DapperOperationClassifier.GetRawKeyword(CommandText) ?? "SQL"
                    : DapperOperationClassifier.GetDiagramLabel(op, v);
                var vContent = v == DapperTrackingVerbosity.Summarised && !_options.LogResponseContent ? null : contentAt(v);
                return new PhaseVariant(
                    vMethod, BuildUri(op, v), vContent, [], false);
            }));
    }

    // The caller's behavior goes with the reader: under SequentialAccess (Dapper's every query) a row is recorded as
    // the caller reads it, since reading it first made the caller's own read throw on SqlClient and Npgsql. Rows are
    // kept at the most detail any verbosity the call may be drawn at shows: a phase's variant was given the effective
    // verbosity's note, so a Detailed phase of a Summarised wrapper had only a count.
    private TrackingDbDataReader WrapReader(DbDataReader inner, Guid traceId, Guid requestResponseId, CommandBehavior behavior)
    {
        var detail = SqlResponseDetailResolver.ResolveKept(_options.ResponseDetail,
            _options.Verbosity, _options.SetupVerbosity, _options.ActionVerbosity, DapperTrackingVerbosity.Summarised);
        return new TrackingDbDataReader(
            inner, detail, _options.MaxResponseRows, _options.MaxValueDisplayLength, behavior,
            rowsAt => LogRowsResponse(traceId, requestResponseId, rowsAt));
    }

    private string? FormatScalar(object? result) =>
        _options.LogResponseContent ? SqlRowFormatter.ScalarText(result, _options.MaxValueDisplayLength) : null;

    // The wrapper runs inside the application's own call, so whatever recording a command fails on is caught: the
    // command goes on, and the run's diagnostics name what was not recorded.
    private void NotRecorded(Exception ex) => SqlCaptureFailure.Record(_connection.ComponentName, ex);

    private Uri BuildUri(DapperOperationInfo op, DapperTrackingVerbosity verbosity)
    {
        var database = _connection.InnerConnection.Database;
        var dataSource = _connection.InnerConnection.DataSource;

        if (string.IsNullOrEmpty(database)) database = "unknown";
        if (string.IsNullOrEmpty(dataSource)) dataSource = "localhost";

        // Raw puts the data source where the host goes, which "(localdb)\MSSQLLocalDB", "host,1433" or a host list is
        // not: SqlEndpoint makes it one. The other forms put it in the path, as written, and fall back the same way.
        return verbosity switch
        {
            DapperTrackingVerbosity.Raw => SqlEndpoint.Build($"sql://{SqlEndpoint.Host(dataSource)}/{SqlEndpoint.Segment(database)}"),
            DapperTrackingVerbosity.Summarised => op.TableName is not null
                ? PathAddress($"sql:///{database}/{op.TableName}", $"sql:///{SqlEndpoint.Segment(database)}/{op.TableName}")
                : PathAddress($"sql:///{database}", $"sql:///{SqlEndpoint.Segment(database)}"),
            _ => op.TableName is not null
                ? PathAddress($"sql:///{dataSource}/{database}/{op.TableName}",
                    $"sql:///{SqlEndpoint.Host(dataSource)}/{SqlEndpoint.Segment(database)}/{op.TableName}")
                : PathAddress($"sql:///{dataSource}/{database}",
                    $"sql:///{SqlEndpoint.Host(dataSource)}/{SqlEndpoint.Segment(database)}")
        };
    }

    private static Uri PathAddress(string asWritten, string escaped) =>
        Uri.TryCreate(asWritten, UriKind.Absolute, out var uri) ? uri : SqlEndpoint.Build(escaped);

    private string? BuildParameterizedContent()
    {
        if (!_options.LogParameters || _inner.Parameters.Count == 0)
            return CommandText;

        var paramStr = string.Join(", ", _inner.Parameters
            .Cast<DbParameter>()
            .Select(p => $"{p.ParameterName}={(p.Value is null or DBNull ? "" : SqlRowFormatter.ValueText(p.Value))}"));

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
