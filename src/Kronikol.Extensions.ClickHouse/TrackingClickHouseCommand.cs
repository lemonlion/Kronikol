using System.Data;
using System.Data.Common;
using System.Diagnostics.CodeAnalysis;
using Kronikol.Sql;

namespace Kronikol.Extensions.ClickHouse;

/// <summary>
/// Tracking decorator for a ClickHouse <see cref="DbCommand"/> that intercepts SQL execution
/// and logs operations for inclusion in test diagrams.
/// </summary>
public class TrackingClickHouseCommand : DbCommand
{
    private readonly DbCommand _inner;
    private readonly TrackingClickHouseConnection _connection;

    public TrackingClickHouseCommand(DbCommand inner, TrackingClickHouseConnection connection)
    {
        _inner = inner;
        _connection = connection;
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
        set { }
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
            if (_connection.Options.LogResponseContent)
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
            if (_connection.Options.LogResponseContent)
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

    private (Guid TraceId, Guid RequestResponseId)? LogRequest()
    {
        _connection.IncrementInvocationCount();

        var parameters = BuildParameterString();
        return _connection.Tracker.DoLogRequest(
            CommandText,
            _connection.FormattedDataSource,
            _connection.InnerConnection.Database,
            CommandType,
            parameters);
    }

    private void LogResponse(Guid traceId, Guid requestResponseId, int? rowsAffected = null)
    {
        _connection.Tracker.DoLogResponse(traceId, requestResponseId, rowsAffected);
    }

    private void LogResponseWithContent(Guid traceId, Guid requestResponseId, string? content)
    {
        _connection.Tracker.DoLogResponse(traceId, requestResponseId, content);
    }

    private TrackingDbDataReader WrapReader(DbDataReader inner, Guid traceId, Guid requestResponseId)
    {
        var opts = _connection.Options;
        return new TrackingDbDataReader(
            inner, opts.ResponseDetail, opts.MaxResponseRows, opts.MaxValueDisplayLength,
            content => LogResponseWithContent(traceId, requestResponseId, content));
    }

    private string? FormatScalar(object? result)
    {
        if (!_connection.Options.LogResponseContent) return null;
        if (result is null or DBNull) return "null";
        var str = result.ToString() ?? "";
        var maxLen = _connection.Options.MaxValueDisplayLength;
        return str.Length > maxLen ? $"{str[..maxLen]}... ({str.Length} chars)" : str;
    }

    private string? BuildParameterString()
    {
        if (!_connection.Options.LogParameters || _inner.Parameters.Count == 0)
            return null;

        var paramStr = string.Join(", ", _inner.Parameters
            .Cast<DbParameter>()
            .Select(p => $"{p.ParameterName}={p.Value}"));

        return paramStr;
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
