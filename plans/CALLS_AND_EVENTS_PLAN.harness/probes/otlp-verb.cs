// A statement-shaped call whose label is a verb, as the NDJSON route of kronikol export maps it (ExportCommand.cs:175 record.ToLogs, then the mapper),
// beside the same call as a live sink sees it. Published 5.27.13.
#:package Kronikol@5.27.13
#:package Kronikol.Extensions.Otlp@5.27.13
#:property PublishAot=false
#:property NoWarn=CS0618

using Kronikol.Constants;
using Kronikol.Extensions.Otlp;
using Kronikol.Ingestion;
using Kronikol.Tracking;

var at = DateTimeOffset.UtcNow;
void Show(string name, IEnumerable<RequestResponseLog> logs)
{
    var batch = OtlpSpanMapper.MapAll(logs, new OtlpExportOptions { Endpoint = new Uri("http://localhost:4318") }, at);
    foreach (var span in batch.Spans)
        Console.WriteLine($"{name,-34} name={span.Name,-8} kind={span.Kind,-9} http.request.method={span.Attributes.Where(a => a.Key == "http.request.method").Select(a => a.Value).FirstOrDefault() ?? "-"} db.system.name={span.Attributes.Where(a => a.Key == "db.system.name").Select(a => a.Value).FirstOrDefault() ?? "-"}");
}

foreach (var (label, category) in new[] { ("DELETE", DependencyCategories.SQL), ("Delete", DependencyCategories.CosmosDB), ("GET", DependencyCategories.Redis) })
{
    var (request, response) = InteractionRecord.Pair("t", "T", label, "probe:///db", "Db", "Caller", dependencyCategory: category, requestTimestamp: at, responseTimestamp: at.AddMilliseconds(5), requestResponseId: Guid.NewGuid().ToString());
    Show($"NDJSON route, {category} {label}", request.ToLogs().Concat(response.ToLogs()));

    var id = Guid.NewGuid();
    var trace = Guid.NewGuid();
    var uri = new Uri("probe:///db");
    Show($"live logs, {category} {label}", [
        new RequestResponseLog("T", "t", label, null, uri, [], "Db", "Caller", RequestResponseType.Request, trace, id, false, DependencyCategory: category) { Timestamp = at },
        new RequestResponseLog("T", "t", label, null, uri, [], "Db", "Caller", RequestResponseType.Response, trace, id, false, DependencyCategory: category) { Timestamp = at.AddMilliseconds(5) },
    ]);
}
