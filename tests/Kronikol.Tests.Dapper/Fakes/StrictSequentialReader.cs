using System.Collections;
using System.Data.Common;

namespace Kronikol.Tests.Dapper.Fakes;

/// <summary>
/// A reader that enforces <see cref="System.Data.CommandBehavior.SequentialAccess"/> as SqlClient and Npgsql do: within
/// a row, each column can be read once, and never one before the last read. IsDBNull does not consume a column.
/// </summary>
public sealed class StrictSequentialReader(string[] columns, object?[][] rows) : DbDataReader
{
    private readonly HashSet<int> _consumed = [];
    private int _row = -1;
    private int _last = -1;

    private object? Consume(int ordinal)
    {
        if (_row < 0 || _row >= rows.Length)
            throw new InvalidOperationException("No row is available.");
        if (ordinal < _last || _consumed.Contains(ordinal))
            throw new InvalidOperationException(
                $"Invalid attempt to read from column ordinal '{ordinal}'. With CommandBehavior.SequentialAccess, " +
                $"you may only read from column ordinal '{_last + 1}' or greater.");
        _consumed.Add(ordinal);
        _last = ordinal;
        return rows[_row][ordinal];
    }

    public override bool Read()
    {
        _consumed.Clear();
        _last = -1;
        if (_row < rows.Length) _row++;
        return _row < rows.Length;
    }

    public override Task<bool> ReadAsync(CancellationToken cancellationToken) => Task.FromResult(Read());
    public override bool NextResult() => false;
    public override Task<bool> NextResultAsync(CancellationToken cancellationToken) => Task.FromResult(false);
    public override int FieldCount => columns.Length;
    public override int RecordsAffected => -1;
    public override bool HasRows => rows.Length > 0;
    public override bool IsClosed => false;
    public override int Depth => 0;
    public override string GetName(int ordinal) => columns[ordinal];
    public override int GetOrdinal(string name) => Array.IndexOf(columns, name);
    public override string GetDataTypeName(int ordinal) => GetFieldType(ordinal).Name;
    public override Type GetFieldType(int ordinal) => rows.Select(r => r[ordinal]).FirstOrDefault(v => v is not null)?.GetType() ?? typeof(object);

    public override bool IsDBNull(int ordinal)
    {
        if (ordinal < _last) throw new InvalidOperationException($"Invalid attempt to read from column ordinal '{ordinal}'.");
        return rows[_row][ordinal] is null;
    }

    public override object GetValue(int ordinal) => Consume(ordinal) ?? DBNull.Value;
    public override T GetFieldValue<T>(int ordinal) => (T)Consume(ordinal)!;
    public override object this[int ordinal] => GetValue(ordinal);
    public override object this[string name] => GetValue(GetOrdinal(name));
    public override bool GetBoolean(int ordinal) => (bool)Consume(ordinal)!;
    public override byte GetByte(int ordinal) => (byte)Consume(ordinal)!;
    public override long GetBytes(int ordinal, long dataOffset, byte[]? buffer, int bufferOffset, int length) => 0;
    public override char GetChar(int ordinal) => (char)Consume(ordinal)!;
    public override long GetChars(int ordinal, long dataOffset, char[]? buffer, int bufferOffset, int length) => 0;
    public override DateTime GetDateTime(int ordinal) => (DateTime)Consume(ordinal)!;
    public override decimal GetDecimal(int ordinal) => (decimal)Consume(ordinal)!;
    public override double GetDouble(int ordinal) => (double)Consume(ordinal)!;
    public override float GetFloat(int ordinal) => (float)Consume(ordinal)!;
    public override Guid GetGuid(int ordinal) => (Guid)Consume(ordinal)!;
    public override short GetInt16(int ordinal) => (short)Consume(ordinal)!;
    public override int GetInt32(int ordinal) => (int)Consume(ordinal)!;
    public override long GetInt64(int ordinal) => (long)Consume(ordinal)!;
    public override string GetString(int ordinal) => (string)Consume(ordinal)!;

    public override int GetValues(object[] values)
    {
        var count = Math.Min(values.Length, columns.Length);
        for (var i = 0; i < count; i++) values[i] = GetValue(i);
        return count;
    }

    public override IEnumerator GetEnumerator() => new DbEnumerator(this);
}
