using System.Net;
using System.Runtime.CompilerServices;
using ClickHouse.Driver;
using ClickHouse.Driver.ADO;
using ClickHouse.Driver.ADO.Parameters;
using ClickHouse.Driver.ADO.Readers;

namespace Kronikol.Tests.ClickHouse.Fakes;

/// <summary>
/// An <see cref="IClickHouseClient"/> that answers from canned values. Like the driver, it enumerates an insert's rows
/// once, lazily, and a query's rows only when the caller enumerates them; it counts both so a fact can prove the
/// tracker never pulls rows itself.
/// </summary>
public sealed class FakeClickHouseClient : IClickHouseClient
{
    public ClickHouseClientSettings Settings { get; } = new() { Host = "ch-host", Database = "analytics" };

    /// <summary>When set, every call fails with it, as the driver does for a statement the server rejects.</summary>
    public Exception? Throw { get; set; }

    public int NonQueryResult { get; set; }
    public object ScalarResult { get; set; } = 42;
    public List<object> QueryRows { get; } = [];
    public HttpResponseMessage StreamResponse { get; set; } = new(HttpStatusCode.OK);

    public List<string> Calls { get; } = [];
    public int InsertEnumerations { get; private set; }
    public DateTimeOffset? InsertStarted { get; private set; }
    public DateTimeOffset? InsertEnded { get; private set; }
    public int QueryEnumerations { get; private set; }
    public bool WasDisposed { get; private set; }

    public Task<int> ExecuteNonQueryAsync(string sql, ClickHouseParameterCollection? parameters = null,
        QueryOptions? options = null, CancellationToken cancellationToken = default)
        => Answer(nameof(ExecuteNonQueryAsync), NonQueryResult);

    public Task<object> ExecuteScalarAsync(string sql, ClickHouseParameterCollection? parameters = null,
        QueryOptions? options = null, CancellationToken cancellationToken = default)
        => Answer(nameof(ExecuteScalarAsync), ScalarResult);

    // The driver's reader has only a private constructor; the tracked client forwards it untouched.
    public Task<ClickHouseDataReader> ExecuteReaderAsync(string sql, ClickHouseParameterCollection? parameters = null,
        QueryOptions? options = null, CancellationToken cancellationToken = default)
        => Answer<ClickHouseDataReader>(nameof(ExecuteReaderAsync), null!);

    public Task<ClickHouseRawResult> ExecuteRawResultAsync(string sql, QueryOptions? options = null,
        CancellationToken cancellationToken = default)
        => Answer<ClickHouseRawResult>(nameof(ExecuteRawResultAsync), null!);

    public Task<long> InsertBinaryAsync(string table, IEnumerable<string> columns, IEnumerable<object[]> rows,
        InsertOptions? options = null, CancellationToken cancellationToken = default)
        => Insert(nameof(InsertBinaryAsync), rows);

    public Task<long> InsertBinaryAsync<T>(string table, IEnumerable<T> rows, InsertOptions? options = null,
        CancellationToken cancellationToken = default) where T : class
        => Insert(nameof(InsertBinaryAsync) + "<T>", rows);

    private async Task<long> Insert<T>(string call, IEnumerable<T> rows)
    {
        Calls.Add(call);
        InsertStarted = DateTimeOffset.UtcNow;
        await Task.Delay(20);
        long sent = 0;
        InsertEnumerations++;
        foreach (var _ in rows)
        {
            sent++;
            if (Throw is { } e && sent == 2) throw e; // fails part way, as a batch the server rejects does
        }
        if (Throw is { } late) throw late;
        InsertEnded = DateTimeOffset.UtcNow;
        return sent;
    }

    public Task<HttpResponseMessage> InsertRawStreamAsync(string table, Stream stream, string format,
        IEnumerable<string>? columns = null, bool useCompression = true, QueryOptions? options = null,
        CancellationToken cancellationToken = default)
        => Answer(nameof(InsertRawStreamAsync), StreamResponse);

    public Task<HttpResponseMessage> PostStreamAsync(string sql, Stream data, bool isCompressed, CancellationToken token,
        QueryOptions? queryOptions = null)
        => Answer(nameof(PostStreamAsync), StreamResponse);

    public Task<HttpResponseMessage> PostStreamAsync(string sql, Func<Stream, CancellationToken, Task> callback,
        bool isCompressed, CancellationToken token, QueryOptions? queryOptions = null)
        => Answer(nameof(PostStreamAsync) + "(callback)", StreamResponse);

    public IAsyncEnumerable<T> QueryAsync<T>(string sql, ClickHouseParameterCollection? parameters = null,
        QueryOptions? options = null, CancellationToken cancellationToken = default) where T : class
    {
        Calls.Add(nameof(QueryAsync));
        return Rows<T>(cancellationToken);
    }

    private async IAsyncEnumerable<T> Rows<T>([EnumeratorCancellation] CancellationToken cancellationToken)
    {
        QueryEnumerations++;
        await Task.Yield();
        if (Throw is { } e) throw e;
        foreach (var row in QueryRows)
            yield return (T)row;
    }

    public Task<bool> PingAsync(QueryOptions? queryOptions = null, CancellationToken cancellationToken = default)
        => Answer(nameof(PingAsync), true);

    public void RegisterBinaryInsertType<T>() where T : class => Calls.Add(nameof(RegisterBinaryInsertType));
    public void RegisterPocoType<T>() where T : class => Calls.Add(nameof(RegisterPocoType));
    public void RegisterJsonSerializationType<T>() where T : class => Calls.Add(nameof(RegisterJsonSerializationType));
    public void RegisterJsonSerializationType(Type type) => Calls.Add(nameof(RegisterJsonSerializationType) + "(Type)");

    public ClickHouseConnection CreateConnection()
    {
        Calls.Add(nameof(CreateConnection));
        return new ClickHouseConnection("Host=ch-host");
    }

    public void Dispose() => WasDisposed = true;

    private async Task<T> Answer<T>(string call, T value)
    {
        Calls.Add(call);
        await Task.Yield();
        if (Throw is { } e) throw e;
        return value;
    }
}

/// <summary>A stream that counts every read, to prove the tracker never reads a caller's insert stream.</summary>
public sealed class CountingStream(byte[] bytes) : MemoryStream(bytes)
{
    public int Reads { get; private set; }

    public override int Read(byte[] buffer, int offset, int count) { Reads++; return base.Read(buffer, offset, count); }
    public override int Read(Span<byte> buffer) { Reads++; return base.Read(buffer); }
    public override Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
    { Reads++; return base.ReadAsync(buffer, offset, count, cancellationToken); }
    public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
    { Reads++; return base.ReadAsync(buffer, cancellationToken); }
}
