using System.Data;
using System.Data.Common;
using System.Diagnostics.CodeAnalysis;
using Microsoft.AspNetCore.Http;
using Kronikol.Sql;
using Kronikol.Tracking;

namespace Kronikol.Extensions.ClickHouse;

/// <summary>
/// Decorator wrapping a ClickHouse <see cref="DbConnection"/> (from ClickHouse.Client or
/// Octonica.ClickHouseClient) to intercept and track all SQL operations for test diagram generation.
/// </summary>
public class TrackingClickHouseConnection : DbConnection, ITrackingComponent
{
    private readonly DbConnection _inner;
    private readonly ClickHouseTrackingOptions _options;
    private readonly IHttpContextAccessor? _httpContextAccessor;
    private readonly SqlDiagnosticTrackerForClickHouseWrapping _tracker;
    private int _invocationCount;

    public TrackingClickHouseConnection(DbConnection inner, ClickHouseTrackingOptions options, IHttpContextAccessor? httpContextAccessor = null)
    {
        _inner = inner;
        _options = options;
        _httpContextAccessor = httpContextAccessor ?? options.HttpContextAccessor;
        _tracker = new SqlDiagnosticTrackerForClickHouseWrapping(options, _httpContextAccessor);
        TrackingComponentRegistry.Register(this);
    }

    public DbConnection InnerConnection => _inner;

    public string ComponentName => $"TrackingClickHouseConnection ({_options.ServiceName})";
    public bool WasInvoked => _invocationCount > 0;
    public int InvocationCount => _invocationCount;
    public bool HasHttpContextAccessor => _httpContextAccessor is not null;

    internal void IncrementInvocationCount() => Interlocked.Increment(ref _invocationCount);
    internal SqlDiagnosticTrackerForClickHouseWrapping Tracker => _tracker;
    internal ClickHouseTrackingOptions Options => _options;

    [AllowNull]
    public override string ConnectionString
    {
        get => _inner.ConnectionString!;
        set => _inner.ConnectionString = value;
    }

    public override string Database => _inner.Database;
    public override string DataSource => _inner.DataSource!;
    public override string ServerVersion => _inner.ServerVersion;
    public override ConnectionState State => _inner.State;

    /// <summary>The data source used for diagram URIs, falling back to "localhost".</summary>
    internal string FormattedDataSource => string.IsNullOrEmpty(_inner.DataSource) ? "localhost" : _inner.DataSource;

    public override void Open() => _inner.Open();
    public override Task OpenAsync(CancellationToken cancellationToken) => _inner.OpenAsync(cancellationToken);
    public override void Close() => _inner.Close();
    public override Task CloseAsync() => _inner.CloseAsync();
    public override void ChangeDatabase(string databaseName) => _inner.ChangeDatabase(databaseName);

    protected override DbCommand CreateDbCommand()
    {
        var innerCommand = _inner.CreateCommand();
        return new TrackingClickHouseCommand(innerCommand, this);
    }

    protected override DbTransaction BeginDbTransaction(IsolationLevel isolationLevel)
    {
        var innerTx = _inner.BeginTransaction(isolationLevel);
        return new TrackingClickHouseTransaction(innerTx, this);
    }

    protected override async ValueTask<DbTransaction> BeginDbTransactionAsync(
        IsolationLevel isolationLevel, CancellationToken cancellationToken)
    {
        var innerTx = await _inner.BeginTransactionAsync(isolationLevel, cancellationToken);
        return new TrackingClickHouseTransaction(innerTx, this);
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

internal sealed class SqlDiagnosticTrackerForClickHouseWrapping : SqlDiagnosticTracker
{
    public SqlDiagnosticTrackerForClickHouseWrapping(SqlTrackingOptionsBase options, IHttpContextAccessor? httpContextAccessor = null)
        : base(options, httpContextAccessor) { }

    public override string ComponentName => "SqlDiagnosticTrackerForClickHouseWrapping";

    public (Guid TraceId, Guid RequestResponseId)? DoLogRequest(string? commandText, string? dataSource, string? database,
        CommandType commandType = CommandType.Text, string? parameters = null)
        => LogRequest(commandText, dataSource, database, commandType, parameters);

    public void DoLogResponse(Guid traceId, Guid requestResponseId, int? rowsAffected = null, Exception? exception = null)
        => LogResponse(traceId, requestResponseId, rowsAffected, exception);

    public void DoLogResponse(Guid traceId, Guid requestResponseId, string? content, Exception? exception = null)
        => LogResponse(traceId, requestResponseId, content, exception);
}
