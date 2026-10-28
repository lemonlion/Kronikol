// #205 probe: what Google.Cloud.BigQuery.V2 sends for each way of reading a TIMESTAMP, what the emulator answers,
// and what the client makes of it, with and without Kronikol's tracking handler in the pipeline.
// Usage (from client-<version>/): dotnet run -- <baseUri> <plain|kronikol> <run tag>
using System.IO.Compression;
using System.Reflection;
using System.Text;
using System.Text.RegularExpressions;
using Google.Apis.Bigquery.v2.Data;
using Google.Cloud.BigQuery.V2;
using Kronikol.Extensions.BigQuery;
using Kronikol.Tracking;

var baseUri = args.Length > 0 ? args[0] : "http://127.0.0.1:19050/";
var mode = args.Length > 1 ? args[1] : "plain";
var tag = args.Length > 2 ? args[2] : "run";
var clientVersion = typeof(BigQueryClient).Assembly
    .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion ?? "?";
Console.WriteLine($"# Google.Cloud.BigQuery.V2 {clientVersion} | mode {mode} | BaseUri {baseUri}");

var exchanges = new List<Exchange>();
var tracking = mode == "kronikol"
    ? new BigQueryTrackingMessageHandlerOptions { CurrentTestInfoFetcher = () => ("probe scenario", "probe-1") }
    : null;
var client = await new BigQueryClientBuilder
{
    ProjectId = "probe",
    BaseUri = baseUri,
    ApiKey = "any",
    HttpClientFactory = new RecordingFactory(exchanges, tracking),
}.BuildAsync();

var dataset = "ds_" + Regex.Replace($"{clientVersion.Split('+')[0]}_{mode}_{tag}", "[^A-Za-z0-9]", "_");
var t0 = new DateTime(2023, 10, 27, 0, 0, 0, DateTimeKind.Utc);
var table = $"`probe.{dataset}.t`";

await Run("S0 setup: dataset, table (ts, rec.inner_ts, arr), insertAll one row", async () =>
{
    await client.CreateDatasetAsync(dataset);
    var schema = new TableSchemaBuilder
    {
        { "id", BigQueryDbType.Int64 },
        { "ts", BigQueryDbType.Timestamp },
        { "rec", new TableSchemaBuilder { { "inner_ts", BigQueryDbType.Timestamp }, { "label", BigQueryDbType.String } }.Build() },
        { "arr", BigQueryDbType.Timestamp, BigQueryFieldMode.Repeated },
    }.Build();
    var created = await client.CreateTableAsync(dataset, "t", schema);
    await created.InsertRowAsync(new BigQueryInsertRow
    {
        { "id", 1 },
        { "ts", t0 },
        { "rec", new BigQueryInsertRow { { "inner_ts", t0.AddSeconds(1.5) }, { "label", "x" } } },
        { "arr", new[] { t0, t0.AddSeconds(2) } },
    });
    return "ok";
});

await Run("Q1 ExecuteQueryAsync(sql, null, QueryOptions { UseQueryCache = false }), the issue's call", async () =>
    Read(await client.ExecuteQueryAsync($"SELECT id, ts FROM {table}", null, new QueryOptions { UseQueryCache = false }), "ts"));

await Run("Q2 ExecuteQueryAsync(sql, null), no options", async () =>
    Read(await client.ExecuteQueryAsync($"SELECT id, ts FROM {table}", null), "ts"));

await Run("Q3 a TIMESTAMP literal, no table", async () =>
    Read(await client.ExecuteQueryAsync("SELECT TIMESTAMP '2023-10-27 00:00:00+00' AS ts", null), "ts"));

await Run("Q4 a TIMESTAMP query parameter", async () =>
    Read(await client.ExecuteQueryAsync("SELECT @p AS ts", [new BigQueryParameter("p", BigQueryDbType.Timestamp, t0)]), "ts"));

await Run("Q5 RECORD and REPEATED timestamps", async () =>
    Read(await client.ExecuteQueryAsync($"SELECT rec, arr FROM {table}", null), "rec", "arr"));

await Run("Q6 ListRows (tabledata.list)", () =>
    Task.FromResult(Read(client.ListRows(dataset, "t").ToList(), "ts", "rec", "arr")));

await Run("Q7 DML INSERT, then SELECT the inserted row", async () =>
{
    await client.ExecuteQueryAsync($"INSERT INTO {table} (id, ts) VALUES (2, TIMESTAMP '2023-10-27 00:00:01+00')", null);
    return Read(await client.ExecuteQueryAsync($"SELECT ts FROM {table} WHERE id = 2", null), "ts");
});

await Run("Q8 QueryOptions { Priority = Interactive } (the default priority, set explicitly)", async () =>
    Read(await client.ExecuteQueryAsync($"SELECT id, ts FROM {table}", null,
        new QueryOptions { UseQueryCache = false, Priority = QueryPriority.Interactive }), "ts"));

await Run("Q9 QueryOptions { JobIdPrefix }", async () =>
    Read(await client.ExecuteQueryAsync($"SELECT id, ts FROM {table}", null,
        new QueryOptions { UseQueryCache = false, JobIdPrefix = "probe_" }), "ts"));

#if !NO_INT64_OPTION // GetQueryResultsOptions.UseInt64Timestamp arrived in 3.6.0
await Run("Q10 GetQueryResultsOptions { UseInt64Timestamp = false }", async () =>
    Read(await client.ExecuteQueryAsync($"SELECT id, ts FROM {table}", null,
        new QueryOptions { UseQueryCache = false }, new GetQueryResultsOptions { UseInt64Timestamp = false }), "ts"));
#endif

await Run("Q11 CreateQueryJob + GetQueryResults (the job path, by hand)", async () =>
{
    var job = await client.CreateQueryJobAsync($"SELECT id, ts FROM {table}", null);
    return Read(await job.GetQueryResultsAsync(), "ts");
});

// Q12 and Q13 were added after the first runs: the issue's call passes parameters, and Q1 passes none.
await Run("Q12 the issue's call with an INT64 parameter in the WHERE", async () =>
    Read(await client.ExecuteQueryAsync($"SELECT id, ts FROM {table} WHERE id = @id",
        [new BigQueryParameter("id", BigQueryDbType.Int64, 1L)], new QueryOptions { UseQueryCache = false }), "ts"));

await Run("Q13 the issue's call with a STRING parameter in the WHERE", async () =>
    Read(await client.ExecuteQueryAsync($"SELECT ts FROM {table} WHERE rec.label = @label",
        [new BigQueryParameter("label", BigQueryDbType.String, "x")], new QueryOptions { UseQueryCache = false }), "ts"));

if (tracking is not null)
{
    Console.WriteLine("## What Kronikol recorded (request records)");
    foreach (var log in RequestResponseLogger.RequestAndResponseLogs.Where(l => l.Type == RequestResponseType.Request))
        Console.WriteLine($"  {log.Method.Value} {log.Uri}");
}

async Task Run(string title, Func<Task<string>> body)
{
    exchanges.Clear();
    string outcome;
    try { outcome = await body(); }
    catch (Exception e) { outcome = $"THROWS {e.GetType().Name}: {OneLine(e.Message, 200)}"; }
    Console.WriteLine($"## {title}");
    foreach (var x in exchanges.ToArray())
    {
        Console.WriteLine($"  > {x.Method} {x.Url.PathAndQuery}");
        if (x.RequestBody is { Length: > 0 } requestBody)
            Console.WriteLine($"    body: {OneLine(requestBody, 360)}");
        Console.WriteLine($"  < {x.Status} {RowsExcerpt(x.ResponseBody)}");
    }
    Console.WriteLine($"  = {outcome}");
}

static string Read(IEnumerable<BigQueryRow> rows, params string[] columns)
{
    var parts = new List<string>();
    try
    {
        foreach (var row in rows)
            foreach (var column in columns)
                parts.Add($"{column}={Show(row[column])}");
    }
    catch (Exception e)
    {
        parts.Add($"THROWS {e.GetType().Name}: {OneLine(e.Message, 160)}");
    }
    return string.Join(", ", parts);
}

static string Show(object? value) => value switch
{
    null => "null",
    DateTime d => d.ToString("O"),
    IDictionary<string, object> record => "{" + string.Join(", ", record.Select(kv => $"{kv.Key}={Show(kv.Value)}")) + "}",
    System.Collections.IEnumerable items and not string => "[" + string.Join(", ", items.Cast<object?>().Select(Show)) + "]",
    _ => value.ToString() ?? ""
};

static string OneLine(string text, int max)
{
    var line = Regex.Replace(text, @"\s+", " ");
    return line.Length <= max ? line : line[..max] + "...";
}

static string RowsExcerpt(string? body)
{
    if (string.IsNullOrEmpty(body)) return "(no body)";
    var at = body.IndexOf("\"rows\"", StringComparison.Ordinal);
    if (at < 0)
    {
        var error = body.IndexOf("\"error\"", StringComparison.Ordinal);
        return error >= 0 ? OneLine(body[error..], 240) : "(no rows)";
    }
    return OneLine(body[at..], 300);
}

record Exchange(string Method, Uri Url, string? RequestBody, int Status, string? ResponseBody);

/// <summary>Records each exchange as the network sees it: inside Kronikol's handler when tracking is on.</summary>
sealed class RecordingHandler(List<Exchange> exchanges) : DelegatingHandler
{
    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
    {
        string? requestBody = null;
        if (request.Content is not null)
        {
            var bytes = await request.Content.ReadAsByteArrayAsync(ct);
            requestBody = request.Content.Headers.ContentEncoding.Contains("gzip")
                ? new StreamReader(new GZipStream(new MemoryStream(bytes), CompressionMode.Decompress)).ReadToEnd()
                : Encoding.UTF8.GetString(bytes);
        }
        var response = await base.SendAsync(request, ct);
        await response.Content.LoadIntoBufferAsync(ct);
        var responseBody = await response.Content.ReadAsStringAsync(ct);
        lock (exchanges)
            exchanges.Add(new Exchange(request.Method.Method, request.RequestUri!, requestBody, (int)response.StatusCode, responseBody));
        return response;
    }
}

sealed class RecordingFactory(List<Exchange> exchanges, BigQueryTrackingMessageHandlerOptions? tracking)
    : Google.Apis.Http.HttpClientFactory
{
    protected override HttpMessageHandler CreateHandler(Google.Apis.Http.CreateHttpClientArgs args)
    {
        var recorder = new RecordingHandler(exchanges) { InnerHandler = base.CreateHandler(args) };
        return tracking is null ? recorder : new BigQueryTrackingMessageHandler(tracking, recorder);
    }
}
