using System.Collections;
using System.Data;
using System.Data.Common;
using System.Diagnostics.CodeAnalysis;

namespace Kronikol.Tests.ClickHouse.Fakes;

/// <summary>A non-ClickHouse fake connection used to exercise the tracking decorators without a server.</summary>
public class FakeDbConnection : DbConnection
{
    private ConnectionState _state = ConnectionState.Closed;

    [AllowNull]
    public override string ConnectionString { get; set; } = "";
    public override string Database { get; } = "analytics";
    public override string DataSource { get; } = "ch-host:8123";
    public override string ServerVersion { get; } = "24.1";
    public override ConnectionState State => _state;

    public FakeDbCommand? LastCreatedCommand { get; private set; }
    public bool WasDisposed { get; private set; }
    public IsolationLevel? LastBeginTransactionIsolationLevel { get; private set; }

    public override void Open() => _state = ConnectionState.Open;

    public override Task OpenAsync(CancellationToken cancellationToken)
    {
        Open();
        return Task.CompletedTask;
    }

    public override void Close() => _state = ConnectionState.Closed;
    public override void ChangeDatabase(string databaseName) { }

    protected override DbCommand CreateDbCommand()
    {
        var cmd = new FakeDbCommand { Connection = this };
        LastCreatedCommand = cmd;
        return cmd;
    }

    protected override DbTransaction BeginDbTransaction(IsolationLevel isolationLevel)
    {
        LastBeginTransactionIsolationLevel = isolationLevel;
        return new FakeDbTransaction(this, isolationLevel);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing) WasDisposed = true;
        base.Dispose(disposing);
    }
}

public class FakeDbCommand : DbCommand
{
    [AllowNull]
    public override string CommandText { get; set; } = "";
    public override int CommandTimeout { get; set; } = 30;
    public override CommandType CommandType { get; set; } = CommandType.Text;
    public override bool DesignTimeVisible { get; set; }
    public override UpdateRowSource UpdatedRowSource { get; set; }

    protected override DbConnection? DbConnection { get; set; }
    protected override DbTransaction? DbTransaction { get; set; }
    protected override DbParameterCollection DbParameterCollection { get; } = new FakeDbParameterCollection();

    public bool WasDisposed { get; private set; }
    public int NonQueryResult { get; set; } = 1;
    public object? ScalarResult { get; set; } = 42;
    public DbDataReader? ReaderResult { get; set; }

    protected override DbDataReader ExecuteDbDataReader(CommandBehavior behavior) => ReaderResult ?? new FakeDbDataReader();

    protected override Task<DbDataReader> ExecuteDbDataReaderAsync(CommandBehavior behavior, CancellationToken cancellationToken)
        => Task.FromResult(ReaderResult ?? (DbDataReader)new FakeDbDataReader());

    public override int ExecuteNonQuery() => NonQueryResult;
    public override Task<int> ExecuteNonQueryAsync(CancellationToken cancellationToken) => Task.FromResult(NonQueryResult);
    public override object? ExecuteScalar() => ScalarResult;
    public override Task<object?> ExecuteScalarAsync(CancellationToken cancellationToken) => Task.FromResult(ScalarResult);

    public override void Prepare() { }
    public override void Cancel() { }
    protected override DbParameter CreateDbParameter() => new FakeDbParameter();

    protected override void Dispose(bool disposing)
    {
        if (disposing) WasDisposed = true;
        base.Dispose(disposing);
    }
}

/// <summary>Records the call and returns a canned rows-affected count.</summary>
public sealed class FakeDriverAdapter(int resolvedRows) : Kronikol.Extensions.ClickHouse.IClickHouseDriverAdapter
{
    public DbCommand? LastInnerCommand { get; private set; }
    public int? LastDriverResult { get; private set; }

    public int ResolveRowsAffected(DbCommand innerCommand, int driverResult)
    {
        LastInnerCommand = innerCommand;
        LastDriverResult = driverResult;
        return resolvedRows;
    }
}

/// <summary>An adapter that throws, to prove tracking never breaks execution.</summary>
public sealed class ThrowingDriverAdapter : Kronikol.Extensions.ClickHouse.IClickHouseDriverAdapter
{
    public int ResolveRowsAffected(DbCommand innerCommand, int driverResult)
        => throw new InvalidOperationException("stats unavailable");
}

public class FakeDbParameter : DbParameter
{
    public override DbType DbType { get; set; }
    public override ParameterDirection Direction { get; set; }
    public override bool IsNullable { get; set; }
    [AllowNull]
    public override string ParameterName { get; set; } = "";
    public override int Size { get; set; }
    [AllowNull]
    public override string SourceColumn { get; set; } = "";
    public override bool SourceColumnNullMapping { get; set; }
    public override object? Value { get; set; }

    public override void ResetDbType() => DbType = DbType.String;
}

public class FakeDbParameterCollection : DbParameterCollection
{
    private readonly List<DbParameter> _parameters = [];

    public override int Count => _parameters.Count;
    public override object SyncRoot => ((ICollection)_parameters).SyncRoot;

    public override int Add(object value)
    {
        _parameters.Add((DbParameter)value);
        return _parameters.Count - 1;
    }

    public override void AddRange(Array values)
    {
        foreach (DbParameter p in values) _parameters.Add(p);
    }

    public override void Clear() => _parameters.Clear();
    public override bool Contains(object value) => _parameters.Contains((DbParameter)value);
    public override bool Contains(string value) => _parameters.Any(p => p.ParameterName == value);
    public override void CopyTo(Array array, int index) => ((ICollection)_parameters).CopyTo(array, index);
    public override IEnumerator GetEnumerator() => _parameters.GetEnumerator();
    public override int IndexOf(object value) => _parameters.IndexOf((DbParameter)value);
    public override int IndexOf(string parameterName) => _parameters.FindIndex(p => p.ParameterName == parameterName);
    public override void Insert(int index, object value) => _parameters.Insert(index, (DbParameter)value);
    public override void Remove(object value) => _parameters.Remove((DbParameter)value);
    public override void RemoveAt(int index) => _parameters.RemoveAt(index);
    public override void RemoveAt(string parameterName) => _parameters.RemoveAll(p => p.ParameterName == parameterName);
    protected override DbParameter GetParameter(int index) => _parameters[index];
    protected override DbParameter GetParameter(string parameterName) => _parameters.First(p => p.ParameterName == parameterName);
    protected override void SetParameter(int index, DbParameter value) => _parameters[index] = value;
    protected override void SetParameter(string parameterName, DbParameter value)
    {
        var idx = IndexOf(parameterName);
        if (idx >= 0) _parameters[idx] = value;
    }
}

/// <summary>A reader with actual data — two rows of (id, name) — for response-detail facts.</summary>
public class FakeRowDbDataReader : DbDataReader
{
    private static readonly (int Id, string Name)[] Rows = [(1, "Pancakes"), (2, "Waffles")];
    private int _index = -1;

    public override int FieldCount => 2;
    public override int RecordsAffected => -1;
    public override bool HasRows => true;
    public override bool IsClosed => false;
    public override int Depth => 0;

    public override bool Read() => ++_index < Rows.Length;
    public override Task<bool> ReadAsync(CancellationToken cancellationToken) => Task.FromResult(Read());

    public override string GetName(int ordinal) => ordinal == 0 ? "id" : "name";
    public override object GetValue(int ordinal) => ordinal == 0 ? Rows[_index].Id : Rows[_index].Name;
    public override bool IsDBNull(int ordinal) => false;

    public override object this[int ordinal] => GetValue(ordinal);
    public override object this[string name] => GetValue(name == "id" ? 0 : 1);
    public override bool NextResult() => false;
    public override Task<bool> NextResultAsync(CancellationToken cancellationToken) => Task.FromResult(false);
    public override bool GetBoolean(int ordinal) => throw new NotSupportedException();
    public override byte GetByte(int ordinal) => throw new NotSupportedException();
    public override long GetBytes(int ordinal, long dataOffset, byte[]? buffer, int bufferOffset, int length) => 0;
    public override char GetChar(int ordinal) => throw new NotSupportedException();
    public override long GetChars(int ordinal, long dataOffset, char[]? buffer, int bufferOffset, int length) => 0;
    public override string GetDataTypeName(int ordinal) => ordinal == 0 ? "Int32" : "String";
    public override DateTime GetDateTime(int ordinal) => throw new NotSupportedException();
    public override decimal GetDecimal(int ordinal) => throw new NotSupportedException();
    public override double GetDouble(int ordinal) => throw new NotSupportedException();
    public override Type GetFieldType(int ordinal) => ordinal == 0 ? typeof(int) : typeof(string);
    public override float GetFloat(int ordinal) => throw new NotSupportedException();
    public override Guid GetGuid(int ordinal) => throw new NotSupportedException();
    public override short GetInt16(int ordinal) => throw new NotSupportedException();
    public override int GetInt32(int ordinal) => Rows[_index].Id;
    public override long GetInt64(int ordinal) => Rows[_index].Id;
    public override string GetString(int ordinal) => Rows[_index].Name;
    public override int GetOrdinal(string name) => name == "id" ? 0 : 1;
    public override int GetValues(object[] values) { values[0] = Rows[_index].Id; values[1] = Rows[_index].Name; return 2; }
    public override IEnumerator GetEnumerator() => Rows.GetEnumerator();
}

public class FakeDbDataReader : DbDataReader
{
    public override int FieldCount => 0;
    public override int RecordsAffected => 0;
    public override bool HasRows => false;
    public override bool IsClosed => true;
    public override int Depth => 0;

    public override object this[int ordinal] => throw new IndexOutOfRangeException();
    public override object this[string name] => throw new IndexOutOfRangeException();

    public override bool Read() => false;
    public override Task<bool> ReadAsync(CancellationToken cancellationToken) => Task.FromResult(false);
    public override bool NextResult() => false;
    public override Task<bool> NextResultAsync(CancellationToken cancellationToken) => Task.FromResult(false);

    public override bool GetBoolean(int ordinal) => throw new IndexOutOfRangeException();
    public override byte GetByte(int ordinal) => throw new IndexOutOfRangeException();
    public override long GetBytes(int ordinal, long dataOffset, byte[]? buffer, int bufferOffset, int length) => 0;
    public override char GetChar(int ordinal) => throw new IndexOutOfRangeException();
    public override long GetChars(int ordinal, long dataOffset, char[]? buffer, int bufferOffset, int length) => 0;
    public override string GetDataTypeName(int ordinal) => throw new IndexOutOfRangeException();
    public override DateTime GetDateTime(int ordinal) => throw new IndexOutOfRangeException();
    public override decimal GetDecimal(int ordinal) => throw new IndexOutOfRangeException();
    public override double GetDouble(int ordinal) => throw new IndexOutOfRangeException();
    public override Type GetFieldType(int ordinal) => throw new IndexOutOfRangeException();
    public override float GetFloat(int ordinal) => throw new IndexOutOfRangeException();
    public override Guid GetGuid(int ordinal) => throw new IndexOutOfRangeException();
    public override short GetInt16(int ordinal) => throw new IndexOutOfRangeException();
    public override int GetInt32(int ordinal) => throw new IndexOutOfRangeException();
    public override long GetInt64(int ordinal) => throw new IndexOutOfRangeException();
    public override string GetName(int ordinal) => throw new IndexOutOfRangeException();
    public override int GetOrdinal(string name) => throw new IndexOutOfRangeException();
    public override string GetString(int ordinal) => throw new IndexOutOfRangeException();
    public override object GetValue(int ordinal) => throw new IndexOutOfRangeException();
    public override int GetValues(object[] values) => 0;
    public override bool IsDBNull(int ordinal) => throw new IndexOutOfRangeException();

    public override IEnumerator GetEnumerator() => Array.Empty<object>().GetEnumerator();
}

public class FakeDbTransaction : DbTransaction
{
    private readonly DbConnection _connection;

    public FakeDbTransaction(DbConnection connection, IsolationLevel isolationLevel)
    {
        _connection = connection;
        IsolationLevel = isolationLevel;
    }

    protected override DbConnection? DbConnection => _connection;
    public override IsolationLevel IsolationLevel { get; }

    public bool WasCommitted { get; private set; }
    public bool WasRolledBack { get; private set; }

    public override void Commit() => WasCommitted = true;
    public override void Rollback() => WasRolledBack = true;
}
