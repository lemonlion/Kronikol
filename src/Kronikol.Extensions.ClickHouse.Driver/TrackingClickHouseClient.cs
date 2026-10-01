using System.Collections.Concurrent;
using System.Data;
using System.Data.Common;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text.Json;
using ClickHouse.Driver;
using ClickHouse.Driver.ADO;
using ClickHouse.Driver.ADO.Parameters;
using ClickHouse.Driver.ADO.Readers;
using Microsoft.AspNetCore.Http;
using Kronikol.Sql;
using Kronikol.Tracking;

namespace Kronikol.Extensions.ClickHouse.Driver;

/// <summary>
/// Decorator for ClickHouse.Driver's <see cref="IClickHouseClient"/> that records its calls for test diagrams, as a
/// <see cref="TrackingClickHouseConnection"/> records a connection's: the same labels, URIs, verbosity, phases and
/// options. Thread-safe like the client it wraps.
/// <list type="bullet">
/// <item><c>InsertBinaryAsync</c> (both overloads) is one call, <c>INSERT INTO {table}</c>, whatever the batching, with
/// the rows it sent as the request's body, formatted as a tracked reader formats rows, and the count it returns.</item>
/// <item><c>ExecuteNonQueryAsync</c> records no count: the client returns 0 for every write.</item>
/// <item><c>ExecuteReaderAsync</c> records the call but not its rows: the driver's reader cannot be wrapped.</item>
/// <item><c>QueryAsync</c> is recorded when it is enumerated, with the rows read.</item>
/// <item><c>PingAsync</c>, the <c>Register*</c> members and <c>CreateConnection</c> are forwarded, not recorded; a
/// connection from <c>CreateConnection</c> is not tracked unless it is wrapped as a connection is.</item>
/// </list>
/// A call that fails is recorded with an <c>Error</c> response carrying the exception's message, and the same exception
/// is rethrown.
/// </summary>
public sealed class TrackingClickHouseClient : IClickHouseClient, ITrackingComponent
{
    private const string SummaryHeader = "X-ClickHouse-Summary";
    private static readonly ConcurrentDictionary<Type, PropertyInfo[]> RowProperties = new();

    private readonly IClickHouseClient _inner;
    private readonly ClickHouseTrackingOptions _options;
    private readonly IHttpContextAccessor? _httpContextAccessor;
    private readonly ClickHouseClientTracker _tracker;
    private int _invocationCount;

    public TrackingClickHouseClient(IClickHouseClient inner, ClickHouseTrackingOptions? options = null, IHttpContextAccessor? httpContextAccessor = null)
    {
        _inner = inner ?? throw new ArgumentNullException(nameof(inner));
        _options = options ?? new ClickHouseTrackingOptions();
        _httpContextAccessor = httpContextAccessor ?? _options.HttpContextAccessor;
        _tracker = new ClickHouseClientTracker(_options, _httpContextAccessor);
        TrackingComponentRegistry.Register(this);
    }

    /// <summary>The client this one forwards to.</summary>
    public IClickHouseClient InnerClient => _inner;

    public string ComponentName => $"TrackingClickHouseClient ({_options.ServiceName})";
    public bool WasInvoked => _invocationCount > 0;
    public int InvocationCount => _invocationCount;
    public bool HasHttpContextAccessor => _httpContextAccessor is not null;

    public ClickHouseClientSettings Settings => _inner.Settings;

    // ─── SQL ────────────────────────────────────────────────

    public async Task<int> ExecuteNonQueryAsync(string sql, ClickHouseParameterCollection? parameters = null,
        QueryOptions? options = null, CancellationToken cancellationToken = default)
    {
        var ids = LogRequest(sql, options, parameters);
        int result;
        try
        {
            result = await _inner.ExecuteNonQueryAsync(sql, parameters, options, cancellationToken);
        }
        catch (Exception ex)
        {
            LogFailure(ids, ex);
            throw;
        }
        // No count: the client reads one from the response body, which is empty for a write, so it returns 0 for
        // every INSERT and DELETE, and "0 rows affected" would be false.
        LogResponse(ids);
        return result;
    }

    public async Task<object> ExecuteScalarAsync(string sql, ClickHouseParameterCollection? parameters = null,
        QueryOptions? options = null, CancellationToken cancellationToken = default)
    {
        var ids = LogRequest(sql, options, parameters);
        object result;
        try
        {
            result = await _inner.ExecuteScalarAsync(sql, parameters, options, cancellationToken);
        }
        catch (Exception ex)
        {
            LogFailure(ids, ex);
            throw;
        }
        LogResponseContent(ids, FormatScalar(result));
        return result;
    }

    public async Task<ClickHouseDataReader> ExecuteReaderAsync(string sql, ClickHouseParameterCollection? parameters = null,
        QueryOptions? options = null, CancellationToken cancellationToken = default)
    {
        var ids = LogRequest(sql, options, parameters);
        ClickHouseDataReader reader;
        try
        {
            reader = await _inner.ExecuteReaderAsync(sql, parameters, options, cancellationToken);
        }
        catch (Exception ex)
        {
            LogFailure(ids, ex);
            throw;
        }
        // The rows cannot be captured: the driver's reader has only a private constructor, so no tracking reader can
        // stand in for it. QueryAsync and a tracked connection show rows.
        LogResponse(ids);
        return reader;
    }

    public async Task<ClickHouseRawResult> ExecuteRawResultAsync(string sql, QueryOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        var ids = LogRequest(sql, options);
        ClickHouseRawResult result;
        try
        {
            result = await _inner.ExecuteRawResultAsync(sql, options, cancellationToken);
        }
        catch (Exception ex)
        {
            LogFailure(ids, ex);
            throw;
        }
        // The body belongs to the caller, who reads it once.
        LogResponse(ids);
        return result;
    }

    public IAsyncEnumerable<T> QueryAsync<T>(string sql, ClickHouseParameterCollection? parameters = null,
        QueryOptions? options = null, CancellationToken cancellationToken = default) where T : class
        => TrackQuery(_inner.QueryAsync<T>(sql, parameters, options, cancellationToken), sql, parameters, options, cancellationToken);

    // Nothing is sent before the rows are enumerated, so nothing is recorded before: the request when enumeration
    // starts, the response with the rows read when it ends or is left.
    private async IAsyncEnumerable<T> TrackQuery<T>(IAsyncEnumerable<T> inner, string sql,
        ClickHouseParameterCollection? parameters, QueryOptions? options,
        [EnumeratorCancellation] CancellationToken cancellationToken = default) where T : class
    {
        var ids = LogRequest(sql, options, parameters);
        var keep = RowsToKeep();
        var kept = new List<T>();
        long read = 0;
        var failed = false;
        await using var rows = inner.GetAsyncEnumerator(cancellationToken);
        try
        {
            while (true)
            {
                bool more;
                try
                {
                    more = await rows.MoveNextAsync();
                }
                catch (Exception ex)
                {
                    failed = true;
                    LogFailure(ids, ex);
                    throw;
                }
                if (!more)
                    break;
                read++;
                if (kept.Count < keep)
                    kept.Add(rows.Current);
                yield return rows.Current;
            }
        }
        finally
        {
            if (!failed && ids is not null)
                LogResponseContent(ids, _options.LogResponseContent
                    ? FormatRows(() => kept.Select(FormatObject).ToList(), read, PropertyNames(typeof(T)))
                    : null);
        }
    }

    // ─── Inserts ────────────────────────────────────────────

    public Task<long> InsertBinaryAsync(string table, IEnumerable<string> columns, IEnumerable<object[]> rows,
        InsertOptions? options = null, CancellationToken cancellationToken = default)
    {
        var names = columns?.ToArray();
        var captured = rows is null ? null : new CapturedRows<object[]>(rows, RowsToKeep());
        return Insert(table, options,
            () => _inner.InsertBinaryAsync(table, names!, captured ?? rows!, options, cancellationToken),
            () => captured is null ? null : FormatRows(() => captured.Kept.Select(r => FormatRow(r, names)).ToList(), captured.Count, names));
    }

    public Task<long> InsertBinaryAsync<T>(string table, IEnumerable<T> rows, InsertOptions? options = null,
        CancellationToken cancellationToken = default) where T : class
    {
        var captured = rows is null ? null : new CapturedRows<T>(rows, RowsToKeep());
        return Insert(table, options,
            () => _inner.InsertBinaryAsync(table, captured ?? rows!, options, cancellationToken),
            () => captured is null ? null : FormatRows(() => captured.Kept.Select(FormatObject).ToList(), captured.Count, PropertyNames(typeof(T))));
    }

    // One call whatever the batching, timed from before the driver's schema probe to after its last batch. The
    // request is logged after the call, since the rows can only be read then, with the time the call started.
    private async Task<long> Insert(string table, QueryOptions? options, Func<Task<long>> send, Func<string?> rows)
    {
        Interlocked.Increment(ref _invocationCount);
        var startedAt = DateTimeOffset.UtcNow;
        long sent;
        try
        {
            sent = await send();
        }
        catch (Exception ex)
        {
            LogFailure(LogInsert(table, options, rows, startedAt), ex);
            throw;
        }
        LogResponse(LogInsert(table, options, rows, startedAt), sent);
        return sent;
    }

    private (Guid TraceId, Guid RequestResponseId)? LogInsert(string table, QueryOptions? options, Func<string?> rows, DateTimeOffset startedAt)
        => _tracker.LogRequestAfterCall($"INSERT INTO {table}", DataSource, Database(options),
            () => _options.LogResponseContent ? rows() : null, startedAt);

    public async Task<HttpResponseMessage> InsertRawStreamAsync(string table, Stream stream, string format,
        IEnumerable<string>? columns = null, bool useCompression = true, QueryOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        var names = columns?.ToArray();
        var statement = names is { Length: > 0 }
            ? $"INSERT INTO {table} ({string.Join(", ", names)}) FORMAT {format}"
            : $"INSERT INTO {table} FORMAT {format}";
        return await SendStream(statement, options,
            () => _inner.InsertRawStreamAsync(table, stream, format, names, useCompression, options, cancellationToken));
    }

    public Task<HttpResponseMessage> PostStreamAsync(string sql, Stream data, bool isCompressed, CancellationToken token,
        QueryOptions? queryOptions = null)
        => SendStream(sql, queryOptions, () => _inner.PostStreamAsync(sql, data, isCompressed, token, queryOptions));

    public Task<HttpResponseMessage> PostStreamAsync(string sql, Func<Stream, CancellationToken, Task> callback,
        bool isCompressed, CancellationToken token, QueryOptions? queryOptions = null)
        => SendStream(sql, queryOptions, () => _inner.PostStreamAsync(sql, callback, isCompressed, token, queryOptions));

    // The stream belongs to the driver, which reads and disposes it, so it is never read here. The server's count of
    // the rows it wrote is in the response's summary header.
    private async Task<HttpResponseMessage> SendStream(string? sql, QueryOptions? options, Func<Task<HttpResponseMessage>> send)
    {
        var ids = LogRequest(sql, options);
        HttpResponseMessage response;
        try
        {
            response = await send();
        }
        catch (Exception ex)
        {
            LogFailure(ids, ex);
            throw;
        }
        LogResponse(ids, UnifiedSqlClassifier.Classify(sql).Operation == UnifiedSqlOperation.Insert ? WrittenRows(response) : null);
        return response;
    }

    // ─── Forwarded, not recorded ────────────────────────────

    public Task<bool> PingAsync(QueryOptions? queryOptions = null, CancellationToken cancellationToken = default)
        => _inner.PingAsync(queryOptions, cancellationToken);

    public void RegisterBinaryInsertType<T>() where T : class => _inner.RegisterBinaryInsertType<T>();
    public void RegisterPocoType<T>() where T : class => _inner.RegisterPocoType<T>();
    public void RegisterJsonSerializationType<T>() where T : class => _inner.RegisterJsonSerializationType<T>();
    public void RegisterJsonSerializationType(Type type) => _inner.RegisterJsonSerializationType(type);

    /// <summary>
    /// The inner client's connection, not tracked: the driver's connection hands out its own concrete commands. Wrap
    /// it with <c>WithClickHouseDriverTestTracking()</c> and use it as a <see cref="DbConnection"/> to track it.
    /// </summary>
    public ClickHouseConnection CreateConnection() => _inner.CreateConnection();

    /// <summary>Disposes the inner client, as disposing it without Kronikol would.</summary>
    public void Dispose() => _inner.Dispose();

    // ─── Recording ──────────────────────────────────────────

    private string DataSource => _inner.Settings.Host;

    private string? Database(QueryOptions? options) => options?.Database ?? _inner.Settings.Database;

    private (Guid TraceId, Guid RequestResponseId)? LogRequest(string? sql, QueryOptions? options, ClickHouseParameterCollection? parameters = null)
    {
        Interlocked.Increment(ref _invocationCount);
        return _tracker.LogRequest(sql, DataSource, Database(options), CommandType.Text, FormatParameters(parameters));
    }

    private void LogResponse((Guid TraceId, Guid RequestResponseId)? ids, long? rowsAffected = null)
    {
        if (ids is not null)
            _tracker.LogResponse(ids.Value.TraceId, ids.Value.RequestResponseId,
                rowsAffected is { } n ? (int)Math.Min(n, int.MaxValue) : null);
    }

    private void LogResponseContent((Guid TraceId, Guid RequestResponseId)? ids, string? content)
    {
        if (ids is not null)
            _tracker.LogResponse(ids.Value.TraceId, ids.Value.RequestResponseId, content);
    }

    private void LogFailure((Guid TraceId, Guid RequestResponseId)? ids, Exception exception)
    {
        if (ids is not null)
            _tracker.LogResponse(ids.Value.TraceId, ids.Value.RequestResponseId, exception: exception);
    }

    private string? FormatParameters(ClickHouseParameterCollection? parameters)
    {
        if (!_options.LogParameters || parameters is null || parameters.Count == 0)
            return null;
        return string.Join(", ", parameters.Cast<DbParameter>().Select(p => $"{p.ParameterName}={p.Value}"));
    }

    private string? FormatScalar(object? result)
    {
        if (!_options.LogResponseContent) return null;
        if (result is null or DBNull) return "null";
        var text = result.ToString() ?? "";
        var maxLength = _options.MaxValueDisplayLength;
        return text.Length > maxLength ? $"{text[..maxLength]}... ({text.Length} chars)" : text;
    }

    private int RowsToKeep()
        => SqlResponseDetailResolver.Resolve(_options) == SqlResponseDetail.FullRows ? Math.Max(0, _options.MaxResponseRows) : 0;

    // A row the note cannot show must not fail a call that has succeeded: an unformattable row leaves the note empty.
    private string? FormatRows(Func<IReadOnlyList<object>> rows, long total, IReadOnlyList<string>? columnNames)
    {
        try
        {
            return SqlRowFormatter.Format(SqlResponseDetailResolver.Resolve(_options), _options.MaxResponseRows, total,
                rows(), columnNames is null ? null : SqlRowFormatter.FormatColumnNames(columnNames));
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            return null;
        }
    }

    private object FormatRow(object[] row, IReadOnlyList<string>? names)
    {
        if (names is null || names.Count != row.Length)
            return row.Select(FormatCell).ToArray();

        var formatted = new Dictionary<string, object?>(names.Count);
        for (var i = 0; i < names.Count; i++)
            formatted[names[i]] = FormatCell(row[i]);
        return formatted;
    }

    private object FormatObject<T>(T row) where T : class
    {
        var formatted = new Dictionary<string, object?>();
        foreach (var property in PropertiesOf(row.GetType()))
            formatted[property.Name] = FormatCell(property.GetValue(row));
        return formatted;
    }

    private object? FormatCell(object? value)
        => value is null or DBNull ? null : SqlRowFormatter.FormatCellValue(value, _options.MaxValueDisplayLength);

    private static string[] PropertyNames(Type type) => PropertiesOf(type).Select(p => p.Name).ToArray();

    private static PropertyInfo[] PropertiesOf(Type type)
        => RowProperties.GetOrAdd(type, t => t.GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Where(p => p.CanRead && p.GetIndexParameters().Length == 0)
            .ToArray());

    internal static long? WrittenRows(HttpResponseMessage response)
    {
        if (!response.Headers.TryGetValues(SummaryHeader, out var values))
            return null;
        try
        {
            using var summary = JsonDocument.Parse(values.First());
            if (!summary.RootElement.TryGetProperty("written_rows", out var written))
                return null;
            var text = written.ValueKind == JsonValueKind.String ? written.GetString() : written.GetRawText();
            return long.TryParse(text, out var rows) ? rows : null;
        }
        catch (JsonException)
        {
            return null;
        }
    }
}

/// <summary>The shared SQL tracker, for a <see cref="TrackingClickHouseClient"/>.</summary>
internal sealed class ClickHouseClientTracker(SqlTrackingOptionsBase options, IHttpContextAccessor? httpContextAccessor)
    : SqlDiagnosticTracker(options, httpContextAccessor)
{
    public override string ComponentName => "ClickHouseClientTracker";
}
