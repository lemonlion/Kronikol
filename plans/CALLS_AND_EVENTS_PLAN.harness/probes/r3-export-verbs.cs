// F16 on the export path: a label that is a verb in another casing, in process and through NDJSON, which is what kronikol export reads
// (ExportCommand.cs:175, record.ToLogs()). Does a Cosmos DB Delete get http.request.method? Published 5.27.13 reads the same way:
// nothing on this path moved in R1 or in R3's first cut.
//
// Usage, from the harness directory: probes/on-prototype.sh <worktree> probes/r3-export-verbs.cs <scratch> > results/r3-export-verbs.txt
// Run as it stands, on the published packages, it gives results/r3-export-verbs-5.27.13.txt.
#:package Kronikol@5.27.13
#:package Kronikol.Extensions.Otlp@5.27.13
#:property PublishAot=false

using Kronikol.Constants;
using Kronikol.Extensions.Otlp;
using Kronikol.Ingestion;
using Kronikol.Tracking;

var options = new OtlpExportOptions { Endpoint = new Uri("http://localhost:4318/v1/traces") };
var at = new DateTimeOffset(2026, 11, 2, 10, 0, 0, TimeSpan.Zero);

Console.WriteLine("label\turi\tcategory\tmetaType\tpath\tspan name\thttp.request.method\tdb.system.name\tkind");
foreach (var (label, uri, category, kind) in new (string, string, string?, RequestResponseMetaType)[]
{
    ("Delete", "cosmosdb:///shop/orders", DependencyCategories.CosmosDB, RequestResponseMetaType.Default),
    ("Patch", "cosmosdb:///shop/orders", DependencyCategories.CosmosDB, RequestResponseMetaType.Default),
    ("Delete", "bigquery:///shop/orders", DependencyCategories.BigQuery, RequestResponseMetaType.Default),
    ("Delete", "storagequeue:///orders", DependencyCategories.MessageQueue, RequestResponseMetaType.Default),
    ("DELETE", "spanner:///shop", DependencyCategories.Spanner, RequestResponseMetaType.Default),
    ("GET", "redis://db0/k", DependencyCategories.Redis, RequestResponseMetaType.Default),
    ("Get", "redis://db0/k", DependencyCategories.Redis, RequestResponseMetaType.Default),
    ("Delete", "event://cake_events", DependencyCategories.MessageQueue, RequestResponseMetaType.Event),
})
{
    var id = Guid.NewGuid();
    var request = new RequestResponseLog("t", "t1", label, null, new Uri(uri), [], "Service", "App", RequestResponseType.Request, id, id, false,
        MetaType: kind, DependencyCategory: category) { Timestamp = at };
    var response = request with { Type = RequestResponseType.Response };
    Report("in process", OtlpSpanMapper.MapAll([request, response], options, at).Spans.Single());
    var viaLine = OtlpSpanMapper.MapAll(new[] { request, response }.Select(l => InteractionRecord.FromJson(InteractionRecord.FromLog(l).ToJson()).ToLog()), options, at).Spans.Single();
    Report("through NDJSON", viaLine);

    void Report(string path, OtlpExportSpan span) => Console.WriteLine(string.Join('\t', label, uri, category, kind, path, span.Name,
        span.Attribute("http.request.method") ?? "-", span.Attribute("db.system.name") ?? "-", span.Kind));
}
