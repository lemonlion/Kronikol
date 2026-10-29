// R2c's fourth test for Kronikol.Extensions.Oracle (plans/CLIENT_FLOORS_PLAN.md 5.1, 5.2): the real ODP.NET client
// through WithTestTracking against Oracle Database Free, on the floor the extension asks for (no property) or on the
// newest 23.x (-p:Newest=true). Prints each call's outcome and what Kronikol recorded.
// Usage: dotnet run -- [<connection string>, default the container on 127.0.0.1:21521]
using System.Data;
using System.Reflection;
using Kronikol.Extensions.Oracle;
using Kronikol.Tracking;
using Oracle.ManagedDataAccess.Client;

var connectionString = args.Length > 0 ? args[0] : "User Id=system;Password=R2c_Probe_Pw1;Data Source=127.0.0.1:21521/FREEPDB1";
var assembly = typeof(OracleConnection).Assembly;
var version = (assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion ?? assembly.GetName().Version?.ToString() ?? "?").Split('+')[0];
Console.WriteLine($"# Oracle.ManagedDataAccess.Core {version} | Oracle Database Free");

await using var connection = new OracleConnection(connectionString).WithTestTracking(new OracleTrackingOptions
{
    CurrentTestInfoFetcher = () => ("probe scenario", "probe-1"),
});
var table = "probe_" + new string(version.Where(char.IsLetterOrDigit).ToArray());

await Step("open the connection", async () => { await connection.OpenAsync(); return "open"; });
await Step("create a table", () => NonQuery($"CREATE TABLE {table} (id NUMBER PRIMARY KEY, text VARCHAR2(50))"));
await Step("insert a row with parameters", () => NonQuery($"INSERT INTO {table} (id, text) VALUES (:id, :text)", ("id", 1), ("text", "hello")));
await Step("select it", async () =>
{
    await using var command = Command($"SELECT text FROM {table} WHERE id = :id", ("id", 1));
    return (await command.ExecuteScalarAsync())?.ToString() ?? "null";
});
await Step("read it with a reader", async () =>
{
    await using var command = Command($"SELECT id, text FROM {table}");
    await using var reader = await command.ExecuteReaderAsync();
    var rows = new List<string>();
    while (await reader.ReadAsync())
        rows.Add($"{reader.GetValue(0)}={reader.GetString(1)}");
    return string.Join(",", rows);
});
await Step("update it in a transaction", async () =>
{
    await using var transaction = await connection.BeginTransactionAsync();
    await using var command = Command($"UPDATE {table} SET text = :text WHERE id = :id", ("text", "again"), ("id", 1));
    command.Transaction = transaction;
    var rows = await command.ExecuteNonQueryAsync();
    await transaction.CommitAsync();
    return rows.ToString();
});
await Step("drop the table", () => NonQuery($"DROP TABLE {table}"));

Console.WriteLine("## What Kronikol recorded");
foreach (var log in RequestResponseLogger.RequestAndResponseLogs)
    Console.WriteLine($"  {log.Type} {log.Method.Value} {log.Uri} ({log.ServiceName})");

System.Data.Common.DbCommand Command(string sql, params (string Name, object Value)[] parameters)
{
    var command = connection.CreateCommand();
    command.CommandText = sql;
    foreach (var (name, value) in parameters)
    {
        var parameter = command.CreateParameter();
        parameter.ParameterName = name;
        parameter.Value = value;
        command.Parameters.Add(parameter);
    }
    return command;
}

async Task<string> NonQuery(string sql, params (string Name, object Value)[] parameters)
{
    await using var command = Command(sql, parameters);
    return (await command.ExecuteNonQueryAsync()).ToString();
}

static async Task Step<T>(string name, Func<Task<T>> call)
{
    try
    {
        var result = await call();
        Console.WriteLine($"{name}: ok{(result is string s ? " " + s : "")}");
    }
    catch (Exception ex)
    {
        Console.WriteLine($"{name}: THROWS {ex.GetType().Name}: {ex.Message.Split(Environment.NewLine)[0]}");
    }
}
