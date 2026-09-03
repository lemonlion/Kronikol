using System.Data;
using System.Data.Common;
using System.Diagnostics.CodeAnalysis;

namespace Kronikol.Tests.Dapper.Fakes;

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

    public int ExecuteReaderCallCount { get; private set; }
    public int ExecuteNonQueryCallCount { get; private set; }
    public int ExecuteScalarCallCount { get; private set; }
    public bool WasPrepared { get; private set; }
    public bool WasCancelled { get; private set; }
    public bool WasDisposed { get; private set; }
    public int NonQueryResult { get; set; } = 1;
    public object? ScalarResult { get; set; } = 42;
    public DbDataReader? ReaderResult { get; set; }

    protected override DbDataReader ExecuteDbDataReader(CommandBehavior behavior)
    {
        ExecuteReaderCallCount++;
        return ReaderResult ?? new FakeDbDataReader();
    }

    protected override Task<DbDataReader> ExecuteDbDataReaderAsync(CommandBehavior behavior, CancellationToken cancellationToken)
    {
        ExecuteReaderCallCount++;
        return Task.FromResult(ReaderResult ?? (DbDataReader)new FakeDbDataReader());
    }

    public override int ExecuteNonQuery()
    {
        ExecuteNonQueryCallCount++;
        return NonQueryResult;
    }

    public override Task<int> ExecuteNonQueryAsync(CancellationToken cancellationToken)
    {
        ExecuteNonQueryCallCount++;
        return Task.FromResult(NonQueryResult);
    }

    public override object? ExecuteScalar()
    {
        ExecuteScalarCallCount++;
        return ScalarResult;
    }

    public override Task<object?> ExecuteScalarAsync(CancellationToken cancellationToken)
    {
        ExecuteScalarCallCount++;
        return Task.FromResult(ScalarResult);
    }

    public override void Prepare() => WasPrepared = true;
    public override void Cancel() => WasCancelled = true;
    protected override DbParameter CreateDbParameter() => new FakeDbParameter();

    protected override void Dispose(bool disposing)
    {
        if (disposing) WasDisposed = true;
        base.Dispose(disposing);
    }
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
    public override object SyncRoot => ((System.Collections.ICollection)_parameters).SyncRoot;

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
    public override void CopyTo(Array array, int index) => ((System.Collections.ICollection)_parameters).CopyTo(array, index);

    public override System.Collections.IEnumerator GetEnumerator() => _parameters.GetEnumerator();

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
