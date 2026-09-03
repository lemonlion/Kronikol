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
        var op = SpannerOperationClassifier.ClassifySql(CommandText, CommandType);

        var rawContent = BuildParameterizedContent();
        var detailedContent = _options.LogSqlText ? CommandText : null;

        var content = _options.Verbosity == SpannerTrackingVerbosity.Raw
            ? rawContent
            : detailedContent;

        var (reqId, traceId) = _connection.Tracker.LogRequest(op, content, rawContent);
        return reqId == Guid.Empty ? null : (traceId, reqId);
    }

    internal void LogResponse(Guid traceId, Guid requestResponseId, int? rowsAffected = null)
    {
        var op = SpannerOperationClassifier.ClassifySql(CommandText, CommandType);

        var effectiveVerbosity = PhaseConfiguration.GetEffectiveVerbosity(
            _options.Verbosity, _options.SetupVerbosity, _options.ActionVerbosity);

        var responseContent = effectiveVerbosity == SpannerTrackingVerbosity.Summarised && !_options.LogResponseContent
            ? null
            : rowsAffected.HasValue ? $"{rowsAffected.Value} rows affected" : null;

        _connection.Tracker.LogResponse(op, requestResponseId, traceId, responseContent);
    }

    private void LogResponseWithContent(Guid traceId, Guid requestResponseId, string? content)
    {
        var op = SpannerOperationClassifier.ClassifySql(CommandText, CommandType);
        _connection.Tracker.LogResponse(op, requestResponseId, traceId, content);
    }

    private TrackingDbDataReader WrapReader(DbDataReader inner, Guid traceId, Guid requestResponseId)
    {
        var detail = (SqlResponseDetail)(int)_options.ResolveResponseDetail();
        return new TrackingDbDataReader(
            inner, detail, _options.MaxResponseRows, 500,
            content => LogResponseWithContent(traceId, requestResponseId, content));
    }

    private string? FormatScalar(object? result)
    {
        if (!_options.LogResponseContent) return null;
        if (result is null or DBNull) return "null";
        var str = result.ToString() ?? "";
        return str.Length > 500 ? $"{str[..500]}... ({str.Length} chars)" : str;
    }

    private string? BuildParameterizedContent()
    {
        if (_inner.Parameters.Count == 0)
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
