using System.Collections;
using System.Collections.ObjectModel;
using System.Data;
using System.Data.Common;

namespace Kronikol.Sql;

/// <summary>
/// Wraps a DbDataReader to capture row data as rows are consumed, then logs
/// the formatted content when the reader is closed or disposed.
/// </summary>
/// <remarks>
/// The wrapper stands where the provider's own reader stood, so it behaves as that reader does: every member is
/// answered by the provider's reader (DbDataReader's own defaults convert nothing, so until 5.11.1 a
/// <c>GetFieldValue&lt;DateTimeOffset&gt;</c> on Npgsql threw), a reader opened with
/// <see cref="CommandBehavior.SequentialAccess"/> is never read ahead of the caller (each value is recorded as the caller
/// reads it), and every result set is kept. Nothing the capture or the note does can throw into the caller: a value that
/// cannot be read or written is shown as such, and a note that cannot be made is left out.
/// </remarks>
public sealed class TrackingDbDataReader : DbDataReader, IDbColumnSchemaGenerator
{
    private readonly DbDataReader _inner;
    private readonly SqlResponseDetail _detail;
    private readonly int _maxRows;
    private readonly int _maxValueLen;
    private readonly Action<string?> _onComplete;
    private readonly bool _sequential;
    private readonly List<ResultSet> _sets = [new()];
    private Dictionary<string, object?>? _rowBeingRead;
    private bool _logged;
    private bool _innerClosed;

    private sealed class ResultSet
    {
        public readonly List<Dictionary<string, object?>> Rows = [];
        public long TotalRows;
        public string[]? ColumnNames;
    }

    public TrackingDbDataReader(
        DbDataReader inner,
        SqlResponseDetail detail,
        int maxRows,
        int maxValueLen,
        Action<string?> onComplete)
        : this(inner, detail, maxRows, maxValueLen, onComplete, CommandBehavior.Default)
    {
    }

    /// <summary>
    /// A wrapper for a reader the caller opened with <paramref name="behavior"/>; under
    /// <see cref="CommandBehavior.SequentialAccess"/> a row is recorded as the caller reads it, never read ahead.
    /// </summary>
    internal TrackingDbDataReader(
        DbDataReader inner,
        SqlResponseDetail detail,
        int maxRows,
        int maxValueLen,
        Action<string?> onComplete,
        CommandBehavior behavior)
    {
        _inner = inner;
        _detail = detail;
        _maxRows = Math.Max(0, maxRows);
        _maxValueLen = maxValueLen;
        _onComplete = onComplete;
        _sequential = (behavior & CommandBehavior.SequentialAccess) != 0;
    }

    private ResultSet Current => _sets[^1];

    private bool KeepsRows => _detail != SqlResponseDetail.RowCountOnly && _maxRows > 0;

    // ─── Read + capture ─────────────────────────────────────

    public override bool Read()
    {
        FinishRowBeingRead();
        if (!_inner.Read())
            return false;

        OnRow();
        return true;
    }

    public override async Task<bool> ReadAsync(CancellationToken cancellationToken)
    {
        FinishRowBeingRead();
        if (!await _inner.ReadAsync(cancellationToken))
            return false;

        OnRow();
        return true;
    }

    public override bool NextResult()
    {
        BeforeNextResult();
        var more = _inner.NextResult();
        AfterNextResult(more);
        return more;
    }

    public override async Task<bool> NextResultAsync(CancellationToken cancellationToken)
    {
        BeforeNextResult();
        var more = await _inner.NextResultAsync(cancellationToken);
        AfterNextResult(more);
        return more;
    }

    private void BeforeNextResult()
    {
        FinishRowBeingRead();
        Current.ColumnNames ??= TryReadColumnNames();
    }

    private void AfterNextResult(bool more)
    {
        if (more)
            _sets.Add(new ResultSet());
        else
            LogIfNotDone(); // no set follows, so the note is complete
    }

    private void OnRow()
    {
        var set = Current;
        set.TotalRows++;
        try
        {
            set.ColumnNames ??= ReadColumnNames();
            if (!KeepsRows || set.Rows.Count >= _maxRows) return;

            if (_sequential)
            {
                // A sequential reader gives each column once, in order: record what the caller reads.
                _rowBeingRead = new Dictionary<string, object?>();
                return;
            }

            var row = new Dictionary<string, object?>();
            for (var i = 0; i < _inner.FieldCount; i++)
                row[_inner.GetName(i)] = CaptureCell(i);
            set.Rows.Add(row);
        }
        catch (Exception)
        {
            // The capture is a copy for the report: whatever it fails on, the caller's row is untouched.
        }
    }

    private object? CaptureCell(int ordinal)
    {
        try
        {
            return _inner.IsDBNull(ordinal) ? null : SqlRowFormatter.FormatCellValue(_inner.GetValue(ordinal), _maxValueLen);
        }
        catch (Exception ex)
        {
            // SqlClient's GetValue on a geography column throws without Microsoft.SqlServer.Types, though the caller
            // reads the column another way.
            return $"(not readable as a value: {ex.GetType().Name})";
        }
    }

    private T Recorded<T>(int ordinal, T value)
    {
        if (_rowBeingRead is not null)
            Record(ordinal, value);
        return value;
    }

    private void Record(int ordinal, object? value)
    {
        try
        {
            _rowBeingRead![_inner.GetName(ordinal)] = value is null or DBNull ? null : SqlRowFormatter.FormatCellValue(value, _maxValueLen);
        }
        catch (Exception)
        {
            // Recording is for the report only.
        }
    }

    private void FinishRowBeingRead()
    {
        if (_rowBeingRead is null) return;
        Current.Rows.Add(_rowBeingRead);
        _rowBeingRead = null;
    }

    private string[]? TryReadColumnNames()
    {
        try
        {
            return ReadColumnNames();
        }
        catch (Exception)
        {
            return null;
        }
    }

    private string[] ReadColumnNames()
    {
        var names = new string[_inner.FieldCount];
        for (var i = 0; i < names.Length; i++)
            names[i] = _inner.GetName(i);
        return names;
    }

    // ─── Logging ────────────────────────────────────────────

    private void LogIfNotDone()
    {
        if (_logged) return;
        _logged = true;
        try
        {
            FinishRowBeingRead();
            Current.ColumnNames ??= TryReadColumnNames();
            _onComplete(FormatContent());
        }
        catch (Exception)
        {
            // A note that cannot be made is left out of the report; the caller's Close or Dispose goes on.
        }
    }

    private string FormatContent()
    {
        // A set with no columns is a statement that returned none (an UPDATE in a batch); the first set always counts.
        var sets = _sets.Where((set, index) => index == 0 || set.TotalRows > 0 || set.ColumnNames is { Length: > 0 });
        return string.Join("\n", sets.Select(Format));
    }

    private string Format(ResultSet set)
    {
        var columnNames = _detail is SqlResponseDetail.RowCountOnly || set.ColumnNames is null
            ? null
            : SqlRowFormatter.FormatColumnNames(set.ColumnNames);
        return SqlRowFormatter.Format(_detail, _maxRows, set.TotalRows, set.Rows, columnNames);
    }

    // ─── Close / Dispose ────────────────────────────────────

    public override void Close()
    {
        LogIfNotDone();
        if (_innerClosed) return;
        _innerClosed = true;
        _inner.Close();
    }

    public override async Task CloseAsync()
    {
        LogIfNotDone();
        if (_innerClosed) return;
        _innerClosed = true;
        await _inner.CloseAsync();
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            LogIfNotDone();
            _innerClosed = true;
            _inner.Dispose();
        }
        base.Dispose(disposing);
    }

    public override async ValueTask DisposeAsync()
    {
        LogIfNotDone();
        _innerClosed = true;
        await _inner.DisposeAsync();
        GC.SuppressFinalize(this);
    }

    // ─── Delegation ─────────────────────────────────────────

    public override int FieldCount => _inner.FieldCount;
    public override int VisibleFieldCount => _inner.VisibleFieldCount;
    public override int RecordsAffected => _inner.RecordsAffected;
    public override bool HasRows => _inner.HasRows;
    public override bool IsClosed => _inner.IsClosed;
    public override int Depth => _inner.Depth;

    public override object this[int ordinal] => Recorded(ordinal, _inner[ordinal]);
    public override object this[string name] => Recorded(_inner.GetOrdinal(name), _inner[name]);

    public override string GetName(int ordinal) => _inner.GetName(ordinal);
    public override int GetOrdinal(string name) => _inner.GetOrdinal(name);
    public override Type GetFieldType(int ordinal) => _inner.GetFieldType(ordinal);
    public override string GetDataTypeName(int ordinal) => _inner.GetDataTypeName(ordinal);
    public override Type GetProviderSpecificFieldType(int ordinal) => _inner.GetProviderSpecificFieldType(ordinal);

    public override bool IsDBNull(int ordinal)
    {
        var isNull = _inner.IsDBNull(ordinal);
        if (isNull) Recorded<object?>(ordinal, null);
        return isNull;
    }

    public override async Task<bool> IsDBNullAsync(int ordinal, CancellationToken cancellationToken)
    {
        var isNull = await _inner.IsDBNullAsync(ordinal, cancellationToken);
        if (isNull) Recorded<object?>(ordinal, null);
        return isNull;
    }

    public override object GetValue(int ordinal) => Recorded(ordinal, _inner.GetValue(ordinal));

    public override int GetValues(object[] values)
    {
        var count = _inner.GetValues(values);
        if (_rowBeingRead is not null)
            for (var i = 0; i < count; i++)
                Record(i, values[i]);
        return count;
    }

    public override T GetFieldValue<T>(int ordinal) => Recorded(ordinal, _inner.GetFieldValue<T>(ordinal));

    public override async Task<T> GetFieldValueAsync<T>(int ordinal, CancellationToken cancellationToken)
        => Recorded(ordinal, await _inner.GetFieldValueAsync<T>(ordinal, cancellationToken));

    public override bool GetBoolean(int ordinal) => Recorded(ordinal, _inner.GetBoolean(ordinal));
    public override byte GetByte(int ordinal) => Recorded(ordinal, _inner.GetByte(ordinal));
    public override char GetChar(int ordinal) => Recorded(ordinal, _inner.GetChar(ordinal));
    public override DateTime GetDateTime(int ordinal) => Recorded(ordinal, _inner.GetDateTime(ordinal));
    public override decimal GetDecimal(int ordinal) => Recorded(ordinal, _inner.GetDecimal(ordinal));
    public override double GetDouble(int ordinal) => Recorded(ordinal, _inner.GetDouble(ordinal));
    public override float GetFloat(int ordinal) => Recorded(ordinal, _inner.GetFloat(ordinal));
    public override Guid GetGuid(int ordinal) => Recorded(ordinal, _inner.GetGuid(ordinal));
    public override short GetInt16(int ordinal) => Recorded(ordinal, _inner.GetInt16(ordinal));
    public override int GetInt32(int ordinal) => Recorded(ordinal, _inner.GetInt32(ordinal));
    public override long GetInt64(int ordinal) => Recorded(ordinal, _inner.GetInt64(ordinal));
    public override string GetString(int ordinal) => Recorded(ordinal, _inner.GetString(ordinal));
    public override object GetProviderSpecificValue(int ordinal) => Recorded(ordinal, _inner.GetProviderSpecificValue(ordinal));
    public override int GetProviderSpecificValues(object[] values) => _inner.GetProviderSpecificValues(values);

    public override long GetBytes(int ordinal, long dataOffset, byte[]? buffer, int bufferOffset, int length)
    {
        if (dataOffset == 0) Recorded<object>(ordinal, "(read in parts)");
        return _inner.GetBytes(ordinal, dataOffset, buffer, bufferOffset, length);
    }

    public override long GetChars(int ordinal, long dataOffset, char[]? buffer, int bufferOffset, int length)
    {
        if (dataOffset == 0) Recorded<object>(ordinal, "(read in parts)");
        return _inner.GetChars(ordinal, dataOffset, buffer, bufferOffset, length);
    }

    public override Stream GetStream(int ordinal)
    {
        Recorded<object>(ordinal, "(read as a stream)");
        return _inner.GetStream(ordinal);
    }

    public override TextReader GetTextReader(int ordinal)
    {
        Recorded<object>(ordinal, "(read as a stream)");
        return _inner.GetTextReader(ordinal);
    }

    public override DataTable? GetSchemaTable() => _inner.GetSchemaTable();

    public override Task<DataTable?> GetSchemaTableAsync(CancellationToken cancellationToken = default)
        => _inner.GetSchemaTableAsync(cancellationToken);

    ReadOnlyCollection<DbColumn> IDbColumnSchemaGenerator.GetColumnSchema() => _inner.GetColumnSchema();

    public override Task<ReadOnlyCollection<DbColumn>> GetColumnSchemaAsync(CancellationToken cancellationToken = default)
        => _inner.GetColumnSchemaAsync(cancellationToken);

    protected override DbDataReader GetDbDataReader(int ordinal) => _inner.GetData(ordinal);

    public override IEnumerator GetEnumerator() => new DbEnumerator(this);
}
