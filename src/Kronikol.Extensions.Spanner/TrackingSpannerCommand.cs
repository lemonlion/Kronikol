using System.Globalization;
using System.Data;
using System.Data.Common;
using System.Diagnostics.CodeAnalysis;
using Microsoft.AspNetCore.Http;
using Kronikol.Sql;
using Kronikol.Tracking;

namespace Kronikol.Extensions.Spanner;

/// <summary>
/// Tracking wrapper for Spanner operations.
/// </summary>
public class TrackingSpannerCommand : DbCommand
{
    private readonly DbCommand _inner;
    private readonly TrackingSpannerConnection _connection;
    private readonly SpannerTrackingOptions _options;
    private readonly IHttpContextAccessor? _httpContextAccessor;

    public TrackingSpannerCommand(DbCommand inner, TrackingSpannerConnection connection, SpannerTrackingOptions options, IHttpContextAccessor? httpContextAccessor = null)
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
        var op = SpannerOperationClassifier.ClassifySql(CommandText, CommandType);

        var rawContent = BuildParameterizedContent();
        var detailedContent = _options.LogSqlText ? CommandText : null;

        // Both forms go to the tracker, which takes each phase's own: choosing one here, from the base verbosity, gave a
        // Detailed phase the Raw content when the base was Raw, SQL text and parameters whatever LogSqlText said.
        var (reqId, traceId) = _connection.Tracker.LogRequest(op, detailedContent, rawContent);
        return reqId == Guid.Empty ? null : (traceId, reqId);
    }

    internal void LogResponse(Guid traceId, Guid requestResponseId, int? rowsAffected = null, Exception? exception = null)
    {
        var op = SpannerOperationClassifier.ClassifySql(CommandText, CommandType);

        if (exception is not null)
        {
            _connection.Tracker.LogFailure(op, requestResponseId, traceId, exception);
            return;
        }

        // The tracker leaves the count out of each note whose verbosity hides it: left out here, at the base verbosity,
        // a Detailed phase of a Summarised command had no count with LogResponseContent off.
        var responseContent = rowsAffected.HasValue ? $"{rowsAffected.Value.ToString(CultureInfo.InvariantCulture)} rows affected" : null;

        _connection.Tracker.LogResponse(op, requestResponseId, traceId, responseContent);
    }

    private void LogResponseWithContent(Guid traceId, Guid requestResponseId, string? content)
    {
        var op = SpannerOperationClassifier.ClassifySql(CommandText, CommandType);
        _connection.Tracker.LogResponse(op, requestResponseId, traceId, content);
    }

    // The caller's behavior goes with the reader: under SequentialAccess (Dapper's every query) a row is recorded as
    // the caller reads it, since reading it first made the caller's own read throw on SqlClient and Npgsql. Rows are
    // kept at the most detail any verbosity the call may be drawn at shows, and each note is made at its own: a phase's
    // variant was given the effective verbosity's note, so a Detailed phase of a Summarised command had only a count.
    private TrackingDbDataReader WrapReader(DbDataReader inner, Guid traceId, Guid requestResponseId, CommandBehavior behavior)
    {
        var detail = (SqlResponseDetail)(int)_options.ResolveKeptResponseDetail();
        return new TrackingDbDataReader(
            inner, detail, _options.MaxResponseRows, 500, behavior,
            rowsAt => _connection.Tracker.LogResponseAtDetail(
                SpannerOperationClassifier.ClassifySql(CommandText, CommandType), requestResponseId, traceId,
                spannerDetail => rowsAt((SqlResponseDetail)(int)spannerDetail)));
    }

    private string? FormatScalar(object? result) =>
        _options.LogResponseContent ? SqlRowFormatter.ScalarText(result, 500) : null;

    private string? BuildParameterizedContent()
    {
        if (_inner.Parameters.Count == 0)
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
