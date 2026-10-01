using System.Data.Common;

namespace Kronikol.Extensions.SqlClient;

/// <summary>
/// Tracking decorator for SQL Server <see cref="DbTransaction"/> that logs BEGIN, COMMIT,
/// and ROLLBACK operations for inclusion in test diagrams.
/// </summary>
public class TrackingSqlTransaction : DbTransaction
{
    private readonly DbTransaction _inner;
    private readonly TrackingSqlConnection _connection;

    public TrackingSqlTransaction(DbTransaction inner, TrackingSqlConnection connection)
    {
        _inner = inner;
        _connection = connection;

        var ids = _connection.Tracker.DoLogRequest("BEGIN TRANSACTION", _connection.InnerConnection.DataSource, _connection.InnerConnection.Database);
        if (ids is not null)
            _connection.Tracker.DoLogResponse(ids.Value.TraceId, ids.Value.RequestResponseId);
    }

    protected override DbConnection DbConnection => _connection;

    public override System.Data.IsolationLevel IsolationLevel => _inner.IsolationLevel;

    public override void Commit()
    {
        var ids = _connection.Tracker.DoLogRequest("COMMIT", _connection.InnerConnection.DataSource, _connection.InnerConnection.Database);
        try
        {
            _inner.Commit();
        }
        catch (Exception ex)
        {
            // A rejected commit or rollback is drawn with its error, not left out of the diagram.
            if (ids is not null)
                _connection.Tracker.DoLogResponse(ids.Value.TraceId, ids.Value.RequestResponseId, exception: ex);
            throw;
        }
        if (ids is not null)
            _connection.Tracker.DoLogResponse(ids.Value.TraceId, ids.Value.RequestResponseId);
    }

    public override void Rollback()
    {
        var ids = _connection.Tracker.DoLogRequest("ROLLBACK", _connection.InnerConnection.DataSource, _connection.InnerConnection.Database);
        try
        {
            _inner.Rollback();
        }
        catch (Exception ex)
        {
            // A rejected commit or rollback is drawn with its error, not left out of the diagram.
            if (ids is not null)
                _connection.Tracker.DoLogResponse(ids.Value.TraceId, ids.Value.RequestResponseId, exception: ex);
            throw;
        }
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
