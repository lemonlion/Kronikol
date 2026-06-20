using System.Data.Common;

namespace Kronikol.Extensions.ClickHouse;

/// <summary>
/// Tracking decorator for a ClickHouse <see cref="DbTransaction"/> that logs BEGIN, COMMIT,
/// and ROLLBACK operations for inclusion in test diagrams.
/// <para>
/// Note: ClickHouse has limited transaction support; this decorator is used only when the
/// underlying client/provider exposes transactions (e.g. experimental support).
/// </para>
/// </summary>
public class TrackingClickHouseTransaction : DbTransaction
{
    private readonly DbTransaction _inner;
    private readonly TrackingClickHouseConnection _connection;

    public TrackingClickHouseTransaction(DbTransaction inner, TrackingClickHouseConnection connection)
    {
        _inner = inner;
        _connection = connection;

        var ids = _connection.Tracker.DoLogRequest("BEGIN TRANSACTION", _connection.FormattedDataSource, _connection.InnerConnection.Database);
        if (ids is not null)
            _connection.Tracker.DoLogResponse(ids.Value.TraceId, ids.Value.RequestResponseId);
    }

    protected override DbConnection DbConnection => _connection;

    public override System.Data.IsolationLevel IsolationLevel => _inner.IsolationLevel;

    public override void Commit()
    {
        _inner.Commit();
        var ids = _connection.Tracker.DoLogRequest("COMMIT", _connection.FormattedDataSource, _connection.InnerConnection.Database);
        if (ids is not null)
            _connection.Tracker.DoLogResponse(ids.Value.TraceId, ids.Value.RequestResponseId);
    }

    public override void Rollback()
    {
        _inner.Rollback();
        var ids = _connection.Tracker.DoLogRequest("ROLLBACK", _connection.FormattedDataSource, _connection.InnerConnection.Database);
        if (ids is not null)
            _connection.Tracker.DoLogResponse(ids.Value.TraceId, ids.Value.RequestResponseId);
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
