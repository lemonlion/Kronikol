using System.Net;
using System.Text;
using Kronikol.PlantUml;
using Kronikol.Reports;
using Kronikol.Tracking;
using static Kronikol.DefaultDiagramsFetcher;

namespace Kronikol.Tests.EndToEnd;

/// <summary>
/// The 2,000-scenario report the search's budgets run on (plans/SEARCH_PLAN.md 4.12): twenty features of a hundred
/// scenarios, each with steps, tags, a status and its own time, and a diagram the real <see cref="PlantUmlCreator"/> draws
/// from request and response pairs whose JSON bodies have the shape of the index plan's corpus
/// (tools/search-bench/formatter-probe <c>--corpus</c>): ids, GUIDs, hex tokens and a basket of items. Three pairs a
/// scenario where the corpus has 18 to 27, so the page loads in a test; one needle a scenario, <c>NEEDLE-{n}</c>, that only
/// deep search finds. Deterministic (xorshift), and compressed as every page is since 5.6.0.
/// </summary>
public static class SearchScaleFixture
{
    public const int Scenarios = 2000;
    public const int Features = 20;

    private static readonly string[] Words =
    [
        "order", "payment", "customer", "shipment", "invoice", "warehouse", "inventory", "refund", "discount", "voucher",
        "delivery", "checkout", "basket", "catalog", "pricing", "billing", "account", "ledger", "parcel", "courier",
    ];

    private static readonly string[] Services = ["OrderService", "PaymentGateway", "InventoryService", "ShippingService", "NotificationHub"];

    private static readonly string[] TagPool = ["smoke", "regression", "api", "slow", "nightly", "billing", "checkout", "edge-case"];

    public static string Generate(string tempDir, string outputDir, string fileName, int scenarios = Scenarios)
    {
        ulong state = 0x9E3779B97F4A7C15UL;
        ulong Next() { state ^= state << 13; state ^= state >> 7; state ^= state << 17; return state; }
        int Below(int n) => (int)(Next() % (ulong)n);
        string Hex(int chars) { var sb = new StringBuilder(chars); while (sb.Length < chars) sb.Append(Next().ToString("x16")); return sb.ToString()[..chars]; }
        string GuidLike() => $"{Hex(8)}-{Hex(4)}-{Hex(4)}-{Hex(4)}-{Hex(12)}";
        string Word() => Words[Below(Words.Length)];
        string Sentence(int n) => string.Join(' ', Enumerable.Range(0, n).Select(_ => Word()));

        var t0 = new DateTime(2026, 10, 26, 9, 0, 0, DateTimeKind.Utc);
        var perFeature = Math.Max(1, scenarios / Features);
        var features = new List<Feature>();
        var logs = new List<RequestResponseLog>();
        for (var f = 0; f * perFeature < scenarios; f++)
        {
            var list = new List<Scenario>();
            for (var s = f * perFeature; s < Math.Min(scenarios, (f + 1) * perFeature); s++)
            {
                var id = $"scale-{s}";
                var failed = s % 23 == 7;
                var words = Sentence(4);
                var name = $"{char.ToUpperInvariant(words[0])}{words[1..]} {s}";
                list.Add(new Scenario
                {
                    Id = id, DisplayName = name,
                    Result = failed ? ExecutionResult.Failed : s % 31 == 5 ? ExecutionResult.Skipped : ExecutionResult.Passed,
                    Duration = TimeSpan.FromMilliseconds(20 + Below(4000)),
                    Tags = [TagPool[Below(TagPool.Length)], TagPool[Below(TagPool.Length)]],
                    ErrorMessage = failed ? $"Expected the {Word()} to be {Word()}-accepted\nbut the {Word()} service answered 409" : null,
                    Steps =
                    [
                        new ScenarioStep { Keyword = "Given", Text = $"a {Word()} with {Below(9) + 1} {Word()} items", Status = ExecutionResult.Passed },
                        new ScenarioStep { Keyword = "When", Text = $"the {Word()} is sent to the {Word()} service", Status = ExecutionResult.Passed },
                        new ScenarioStep { Keyword = "Then", Text = $"the {Word()} is {Word()}-accepted", Status = failed ? ExecutionResult.Failed : ExecutionResult.Passed },
                    ],
                });

                for (var p = 0; p < 3; p++)
                {
                    var svc = Services[Below(Services.Length)];
                    var items = string.Join(',', Enumerable.Range(0, 4 + Below(5)).Select(_ =>
                        $"{{\"sku\":\"SKU-{Hex(10)}\",\"name\":\"{Sentence(3)}\",\"qty\":{1 + Below(9)},\"traceTag\":\"{Hex(24)}\"}}"));
                    var needle = p == 0 ? $",\"needle\":\"NEEDLE-{s}\"" : "";
                    var request = $"{{\"{Word()}Id\":\"{GuidLike()}\",\"kind\":\"{Sentence(2)}\",\"token\":\"tok-{Hex(48)}\",\"comment\":\"{Sentence(10)}\",\"items\":[{items}]{needle}}}";
                    var response = $"{{\"status\":\"{Word()}-accepted\",\"correlation\":\"{GuidLike()}\",\"summary\":\"{Sentence(12)}\"}}";
                    var trace = new Guid(Hex(32));
                    var pair = new Guid(Hex(32));
                    logs.Add(new(name, id, HttpMethod.Post, request,
                        new Uri($"https://api.example.test/{Word()}s/{GuidLike()}?page={Below(40)}"),
                        [("Content-Type", "application/json"), ("traceparent", $"00-{Hex(32)}-{Hex(16)}-01")],
                        svc, "Test", RequestResponseType.Request, trace, pair, TrackingIgnore: false));
                    logs.Add(new(name, id, HttpMethod.Post, response,
                        new Uri($"https://api.example.test/{Word()}s/1"),
                        [("Content-Type", "application/json")],
                        svc, "Test", RequestResponseType.Response, trace, pair, TrackingIgnore: false, StatusCode: HttpStatusCode.Created));
                }
            }
            features.Add(new Feature { DisplayName = $"{char.ToUpperInvariant(Words[f % Words.Length][0])}{Words[f % Words.Length][1..]} feature {f + 1}", Scenarios = list.ToArray() });
        }

        var diagrams = PlantUmlCreator.GetPlantUmlImageTagsPerTestId(logs, clientSideSplitting: true)
            .SelectMany(t => t.PlantUmls.Select(p => new DiagramAsCode(t.TestId, "", p.PlainText)))
            .ToArray();
        var path = ReportGenerator.GenerateHtmlReport(
            diagrams, features.ToArray(), t0, t0.AddMinutes(20),
            null, Path.Combine(tempDir, fileName), "Search Scale", true,
            diagramFormat: DiagramFormat.PlantUml,
            plantUmlRendering: PlantUmlRendering.BrowserJs);
        File.Copy(path, Path.Combine(outputDir, fileName), true);
        return new Uri(path).AbsoluteUri;
    }
}
