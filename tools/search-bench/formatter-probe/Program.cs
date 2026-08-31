// Drives the REAL PlantUmlCreator formatting pipeline with stress content that triggers every
// transform the SEARCH_INDEX_PLAN §4.1 normalization must undo (or document): 80-char value
// chunking, >120-char unbreakable-run wrapping, creole escaping, null-property stripping, CRLF.
// Writes the resulting CodeBehind to formatter-output/*.puml for validate-normalize.js to check.
//
// `--corpus N` mode (M1): generates N scenarios' worth of payload-heavy formatter output
// (deterministic seeded content — GUID-like ids, tokens, URLs, item arrays, a planted
// per-scenario needle) into formatter-output/corpus/doc-*.puml, one file per scenario
// (all of that scenario's diagrams concatenated). m1-bench.js builds the index over it.
using System.Net;
using System.Text;
using Kronikol.PlantUml;
using Kronikol.Tracking;

if (args.Length >= 1 && args[0] == "--m3")
{
    // M3 (SEARCH_INDEX_PLAN §11): index build cost inside report generation at monster scale.
    // Loads the --corpus output (one doc per scenario) and times GenerateHtmlReport with the
    // index off (baseline) and on, interleaved; also reports emitted file sizes.
    var m3CorpusDir = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../formatter-output/corpus"));
    var docFiles = Directory.GetFiles(m3CorpusDir, "doc-*.puml")
        .OrderBy(f => int.Parse(Path.GetFileNameWithoutExtension(f).Split('-')[1]))
        .ToArray();
    Console.WriteLine($"loading {docFiles.Length} corpus docs...");
    var m3Diagrams = new Kronikol.DefaultDiagramsFetcher.DiagramAsCode[docFiles.Length];
    var m3Scenarios = new Kronikol.Reports.Scenario[docFiles.Length];
    long corpusChars = 0;
    for (var d = 0; d < docFiles.Length; d++)
    {
        var text = File.ReadAllText(docFiles[d]);
        corpusChars += text.Length;
        m3Diagrams[d] = new Kronikol.DefaultDiagramsFetcher.DiagramAsCode($"m3-{d}", "", text);
        m3Scenarios[d] = new Kronikol.Reports.Scenario
        {
            Id = $"m3-{d}",
            DisplayName = $"Corpus scenario {d}",
            Result = Kronikol.Reports.ExecutionResult.Passed,
            Steps = [new Kronikol.Reports.ScenarioStep { Keyword = "Given", Text = $"step for scenario {d}", Status = Kronikol.Reports.ExecutionResult.Passed }]
        };
    }
    var m3Features = new[] { new Kronikol.Reports.Feature { DisplayName = "M3 Corpus", Scenarios = m3Scenarios } };
    Console.WriteLine($"corpus: {corpusChars / 1024.0 / 1024.0:F1} MB CodeBehind across {docFiles.Length} scenarios");

    var sw = new System.Diagnostics.Stopwatch();
    string Run(string name, bool index)
    {
        sw.Restart();
        var p = Kronikol.Reports.ReportGenerator.GenerateHtmlReport(
            m3Diagrams, m3Features,
            DateTime.UtcNow, DateTime.UtcNow,
            null, name, "M3", true,
            diagramFormat: Kronikol.DiagramFormat.PlantUml,
            plantUmlRendering: Kronikol.PlantUmlRendering.BrowserJs,
            fullSearchIndex: index);
        sw.Stop();
        Console.WriteLine($"{name} (index={index}): {sw.ElapsedMilliseconds} ms, {new FileInfo(p).Length / 1024.0 / 1024.0:F2} MB");
        return p;
    }
    // interleaved warm/measure runs so JIT + file cache treat both variants alike
    Run("m3-warm-off.html", false);
    Run("m3-warm-on.html", true);
    for (var r = 0; r < 3; r++)
    {
        Run($"m3-off-{r}.html", false);
        Run($"m3-on-{r}.html", true);
    }

    // phase breakdown (isolated, not overlapped): normalize+hash | serialize | gzip
    var phase = new System.Diagnostics.Stopwatch();
    phase.Restart();
    var bucketSets = new int[m3Diagrams.Length][];
    Parallel.For(0, m3Diagrams.Length, i =>
    {
        bucketSets[i] = Kronikol.Reports.SearchIndex.SearchIndexBuilder.CollectTrigramBuckets(
            Kronikol.Reports.SearchIndex.SearchNormalizer.Normalize(m3Diagrams[i].CodeBehind));
    });
    Console.WriteLine($"phase normalize+hash (parallel): {phase.ElapsedMilliseconds} ms");
    phase.Restart();
    var rawIndex = Kronikol.Reports.SearchIndex.SearchIndexBuilder.Serialize(
        Enumerable.Range(0, m3Diagrams.Length).Select(i => $"scenario-{i}").ToArray(), bucketSets);
    Console.WriteLine($"phase serialize: {phase.ElapsedMilliseconds} ms ({rawIndex.Length / 1024.0 / 1024.0:F2} MB raw)");
    phase.Restart();
    var blob = Kronikol.Reports.SearchIndex.SearchIndexBuilder.CompressToBase64(rawIndex);
    Console.WriteLine($"phase gzip+b64 Optimal: {phase.ElapsedMilliseconds} ms ({blob.Length / 1024.0 / 1024.0:F2} MB b64)");
    return;
}

if (args.Length >= 2 && args[0] == "--corpus")
{
    var docCount = int.Parse(args[1]);
    var corpusDir = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../formatter-output/corpus"));
    Directory.CreateDirectory(corpusDir);
    foreach (var old in Directory.GetFiles(corpusDir, "doc-*.puml")) File.Delete(old);

    // Deterministic xorshift so the corpus (and its measurements) are reproducible.
    ulong state = 0x9E3779B97F4A7C15UL;
    ulong NextU64() { state ^= state << 13; state ^= state >> 7; state ^= state << 17; return state; }
    string Hex(int chars) { var sb = new StringBuilder(chars); while (sb.Length < chars) sb.Append(NextU64().ToString("x16")); return sb.ToString()[..chars]; }
    string GuidLike() => $"{Hex(8)}-{Hex(4)}-{Hex(4)}-{Hex(4)}-{Hex(12)}";
    string[] words = ["order", "payment", "customer", "shipment", "invoice", "warehouse", "inventory", "refund", "discount", "voucher", "delivery", "checkout", "basket", "catalog", "pricing", "billing", "account", "session", "profile", "address"];
    string Word() => words[(int)(NextU64() % (ulong)words.Length)];
    string Sentence(int n) { var sb = new StringBuilder(); for (var w = 0; w < n; w++) { if (w > 0) sb.Append(' '); sb.Append(Word()); } return sb.ToString(); }

    long totalChars = 0;
    var services = new[] { "OrderService", "PaymentGateway", "InventoryService", "ShippingService", "NotificationHub" };
    for (var d = 0; d < docCount; d++)
    {
        var testId = $"corpus-{d}";
        var scenarioLogs = new List<RequestResponseLog>();
        var pairs = 18 + (int)(NextU64() % 10);
        for (var p = 0; p < pairs; p++)
        {
            var svc = services[(int)(NextU64() % (ulong)services.Length)];
            var itemCount = 8 + (int)(NextU64() % 10);
            var items = new StringBuilder();
            for (var it = 0; it < itemCount; it++)
            {
                if (it > 0) items.Append(',');
                items.Append($"{{\"sku\":\"SKU-{Hex(10)}\",\"name\":\"{Sentence(3)}\",\"qty\":{1 + (int)(NextU64() % 9)},\"unitPrice\":{(int)(NextU64() % 90000) / 100.0},\"traceTag\":\"{Hex(24)}\"}}");
            }
            var needle = p == 0 ? $",\"needle\":\"NEEDLE-DOC-{d}-{Hex(6)}\"" : "";
            var reqBody = $"{{\"{Word()}Id\":\"{GuidLike()}\",\"kind\":\"{Sentence(2)}\",\"token\":\"tok-{Hex(96)}\",\"comment\":\"{Sentence(12)}\",\"items\":[{items}]{needle}}}";
            var respBody = $"{{\"status\":\"{Word()}-accepted\",\"correlation\":\"{GuidLike()}\",\"receipt\":\"rcp-{Hex(64)}\",\"summary\":\"{Sentence(18)}\"}}";
            var corpusTraceId = Guid.NewGuid();
            var corpusPairId = Guid.NewGuid();
            var reqHeaders = new (string, string?)[] { ("Content-Type", "application/json"), ("traceparent", $"00-{Hex(32)}-{Hex(16)}-01"), ("X-Request-Tag", $"tag-{Hex(40)}") };
            scenarioLogs.Add(new($"Corpus scenario {d}", testId, HttpMethod.Post, reqBody,
                new Uri($"https://api.example.test/{Word()}s/{GuidLike()}/{Word()}?filter={Hex(20)}&page={(int)(NextU64() % 40)}"),
                reqHeaders, svc, "Test", RequestResponseType.Request, corpusTraceId, corpusPairId, TrackingIgnore: false));
            scenarioLogs.Add(new($"Corpus scenario {d}", testId, HttpMethod.Post, respBody,
                new Uri($"https://api.example.test/{Word()}s/1"),
                [("Content-Type", "application/json")], svc, "Test", RequestResponseType.Response,
                corpusTraceId, corpusPairId, TrackingIgnore: false, StatusCode: HttpStatusCode.Created));
        }

        var docSb = new StringBuilder();
        foreach (var perTest in PlantUmlCreator.GetPlantUmlImageTagsPerTestId(scenarioLogs, clientSideSplitting: true))
            foreach (var (plantUml, _) in perTest.PlantUmls)
                docSb.Append(plantUml).Append('\n');
        var docText = docSb.ToString();
        totalChars += docText.Length;
        File.WriteAllText(Path.Combine(corpusDir, $"doc-{d}.puml"), docText);
        if (d % 200 == 0) Console.WriteLine($"doc {d}/{docCount} ({totalChars / 1024 / 1024} MB so far)");
    }
    Console.WriteLine($"corpus: {docCount} docs, {totalChars / 1024.0 / 1024.0:F1} MB total CodeBehind -> {corpusDir}");
    return;
}

var longToken = "eyJhbGciOiJIUzI1NiIsInR5cCI6IkpXVCJ9." + new string('A', 60)
    + "NEEDLE-RUNWRAP-7f3a9c2e" + new string('B', 60) + ".sig-" + new string('C', 60);
var longHeaderValue = "chunkstart-" + new string('h', 100) + "NEEDLE-HDRCHUNK-42" + new string('k', 100) + "-chunkend";
var minified = "{\"orderId\":\"ord-991\",\"nullField\":null,\"creole\":\"**important** and //slanted// plus __deep__\","
    + "\"turkish\":\"İstanbul\",\"blob\":\"" + longToken + "\","
    + "\"link\":\"https://example.test/api/v2/orders?filter=" + new string('q', 90) + "NEEDLE-URLRUN-9&page=2\","
    + "\"note\":\"a perfectly ordinary sentence with spaces so it wraps normally\"}";
var prettyResponse = "{\n  \"status\": \"created\",\n  \"echoBlob\": \"" + longToken + "\"\n}";

var traceId = Guid.NewGuid();
var pairId = Guid.NewGuid();
var headers = new (string, string?)[] { ("Content-Type", "application/json"), ("X-Long-Header", longHeaderValue) };

var logs = new List<RequestResponseLog>
{
    new("Stress scenario", "stress-1", HttpMethod.Post, minified,
        new Uri("https://api.example.test/orders/" + new string('p', 130) + "NEEDLE-URLPATH-3"),
        headers, "OrderService", "Test", RequestResponseType.Request, traceId, pairId, TrackingIgnore: false),
    new("Stress scenario", "stress-1", HttpMethod.Post, prettyResponse,
        new Uri("https://api.example.test/orders/1"),
        [("Content-Type", "application/json")], "OrderService", "Test", RequestResponseType.Response,
        traceId, pairId, TrackingIgnore: false, StatusCode: HttpStatusCode.Created),
};

var outDir = Path.Combine(AppContext.BaseDirectory, "../../../../formatter-output");
Directory.CreateDirectory(outDir);
var i = 0;
foreach (var perTest in PlantUmlCreator.GetPlantUmlImageTagsPerTestId(logs, clientSideSplitting: true))
{
    foreach (var (plantUml, _) in perTest.PlantUmls)
    {
        var path = Path.GetFullPath(Path.Combine(outDir, $"stress-{i++}.puml"));
        File.WriteAllText(path, plantUml);
        Console.WriteLine($"wrote {path} ({plantUml.Length} chars)");
    }
}
Console.WriteLine($"diagrams: {i}");
