// R2a's fourth test for Kronikol.Extensions.Spanner (plans/CLIENT_FLOORS_PLAN.md 5.1, 5.2): the real
// Google.Cloud.Spanner.Data client, through both ways the extension tracks it (the interceptor that
// SpannerConnectionStringBuilder.WithTestTracking installs, and TrackingSpannerConnection), against the Spanner
// emulator (SPANNER_EMULATOR_HOST), on the floor the extension asks for (no property) or on the newest 5.x
// (-p:Newest=true). Prints each call's outcome and what Kronikol recorded.
// Usage: SPANNER_EMULATOR_HOST=127.0.0.1:9010 dotnet run
using System.Reflection;
using Google.Api.Gax;
using Google.Cloud.Spanner.Admin.Database.V1;
using Google.Cloud.Spanner.Admin.Instance.V1;
using Google.Cloud.Spanner.Data;
using Kronikol.Extensions.Spanner;
using Kronikol.Tracking;

var version = typeof(SpannerConnection).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion ?? "?";
Console.WriteLine($"# Google.Cloud.Spanner.Data {version.Split('+')[0]} | emulator at {Environment.GetEnvironmentVariable("SPANNER_EMULATOR_HOST")}");

var suffix = version.Split('+')[0].Replace('.', '-');
var instance = "i-" + suffix;
var database = "d-" + suffix;
var options = new SpannerTrackingOptions { CurrentTestInfoFetcher = () => ("probe scenario", "probe-1") };

await Step("create the instance and the database", async () =>
{
    var instances = await new InstanceAdminClientBuilder { EmulatorDetection = EmulatorDetection.EmulatorOnly }.BuildAsync();
    var created = await instances.CreateInstanceAsync(new CreateInstanceRequest
    {
        Parent = "projects/probe",
        InstanceId = instance,
        Instance = new Instance { Config = "projects/probe/instanceConfigs/emulator-config", DisplayName = instance, NodeCount = 1 },
    });
    await created.PollUntilCompletedAsync();
    var databases = await new DatabaseAdminClientBuilder { EmulatorDetection = EmulatorDetection.EmulatorOnly }.BuildAsync();
    var db = await databases.CreateDatabaseAsync(new CreateDatabaseRequest
    {
        Parent = $"projects/probe/instances/{instance}",
        CreateStatement = $"CREATE DATABASE `{database}`",
        ExtraStatements = { "CREATE TABLE t (id INT64 NOT NULL, name STRING(100)) PRIMARY KEY (id)" },
    });
    await db.PollUntilCompletedAsync();
    return "created";
});

var source = $"Data Source=projects/probe/instances/{instance}/databases/{database}";

await Step("insert and read through the interceptor (WithTestTracking on the builder)", async () =>
{
    var builder = new SpannerConnectionStringBuilder(source) { EmulatorDetection = EmulatorDetection.EmulatorOnly }.WithTestTracking(options);
    await using var connection = new SpannerConnection(builder);
    await connection.OpenAsync();
    var insert = connection.CreateDmlCommand("INSERT INTO t (id, name) VALUES (1, 'one')");
    await insert.ExecuteNonQueryAsync();
    var select = connection.CreateSelectCommand("SELECT name FROM t WHERE id = 1");
    return (string)(await select.ExecuteScalarAsync())!;
});

await Step("read through TrackingSpannerConnection", async () =>
{
    var builder = new SpannerConnectionStringBuilder(source) { EmulatorDetection = EmulatorDetection.EmulatorOnly };
    await using var connection = new SpannerConnection(builder).WithTestTracking(options);
    await connection.OpenAsync();
    var command = connection.CreateCommand();
    command.CommandText = "SELECT COUNT(*) FROM t";
    return Convert.ToString(await command.ExecuteScalarAsync())!;
});

Console.WriteLine("## What Kronikol recorded");
foreach (var log in RequestResponseLogger.RequestAndResponseLogs)
    Console.WriteLine($"  {log.Type} {log.Method.Value} {log.Uri} {log.StatusCode?.Value} ({log.ServiceName})");

static async Task Step<T>(string name, Func<Task<T>> call)
{
    try
    {
        var result = await call();
        Console.WriteLine($"{name}: ok{(result is string s ? " " + s : "")}");
    }
    catch (Exception ex)
    {
        Console.WriteLine($"{name}: THROWS {ex.GetType().Name}: {ex.Message.Split('\n')[0]}");
    }
}
