using System.Text;
using System.Text.Json;
using Kronikol.Reports;
using static Kronikol.DefaultDiagramsFetcher;

namespace Kronikol.Tests.EndToEnd;

/// <summary>
/// Generates the "large report" fixture used by the browser-render performance tests: a handful of
/// note-dominated sequence diagrams shaped like a real capture (request arrow + DB arrow + reply, each
/// with a multi-KB pretty-printed JSON body in a note) — the shape that made the main-thread renderer
/// freeze the page. Deterministic (seeded), so two runs compare like with like.
/// </summary>
public static class LargeReportFixture
{
    public const int DefaultDiagrams = 6;
    public const int DefaultStepsPerDiagram = 40;

    public static string Generate(string tempDir, string outputDir, string fileName,
        int diagrams = DefaultDiagrams, int stepsPerDiagram = DefaultStepsPerDiagram,
        int browserRenderWorkers = Constants.TrackingDefaults.BrowserRenderWorkers,
        int browserFragmentMaxHeight = Constants.TrackingDefaults.BrowserFragmentMaxHeight)
    {
        var (features, _) = ReportTestHelper.CreateTestData();
        // Two diagrams per scenario over the first scenarios (t1, t2, t3 …); the first scenario's
        // diagrams are rendered eagerly on load, the rest on demand / when forced.
        var diagramList = new List<DiagramAsCode>();
        for (var d = 0; d < diagrams; d++)
        {
            var testId = "t" + (d / 2 + 1);
            diagramList.Add(new DiagramAsCode(testId, "", BuildDiagram(d, stepsPerDiagram)));
        }

        var path = ReportGenerator.GenerateHtmlReport(
            diagramList.ToArray(), features,
            DateTime.UtcNow, DateTime.UtcNow,
            null, Path.Combine(tempDir, fileName), "Large Report", true,
            diagramFormat: DiagramFormat.PlantUml,
            plantUmlRendering: PlantUmlRendering.BrowserJs,
            browserRenderWorkers: browserRenderWorkers,
            browserFragmentMaxHeight: browserFragmentMaxHeight);

        File.Copy(path, Path.Combine(outputDir, fileName), true);
        return new Uri(path).AbsoluteUri;
    }

    /// <summary>One diagram: 4 participants, <paramref name="steps"/> request/DB/reply triples with JSON notes.</summary>
    public static string BuildDiagram(int index, int steps)
    {
        var rng = new Random(1000 + index);
        var sb = new StringBuilder();
        sb.Append("@startuml\n");
        sb.Append("autonumber 1\n");
        sb.Append("actor \"Caller\" as caller\n");
        sb.Append("participant \"OrderService\" as svc\n");
        sb.Append("participant \"InventoryService\" as inv\n");
        sb.Append("database \"OrdersDb\" as db\n");
        sb.Append('\n');
        for (var s = 0; s < steps; s++)
        {
            sb.Append($"caller -> svc : POST /api/orders/{index}-{s}\n");
            sb.Append("note right\n");
            sb.Append("Content-Type: application/json\n");
            sb.Append(JsonBody(rng, s, "request"));
            sb.Append("\nend note\n");
            sb.Append($"svc -> inv : GET /inventory/sku-{s}\n");
            sb.Append("inv --> svc : 200 OK\n");
            sb.Append("note left\n");
            sb.Append(JsonBody(rng, s, "inventory", small: true));
            sb.Append("\nend note\n");
            sb.Append("svc -> db : INSERT INTO Orders\n");
            sb.Append("db --> svc : OK\n");
            sb.Append("svc --> caller : 201 Created\n");
            sb.Append("note left\n");
            sb.Append(JsonBody(rng, s, "response"));
            sb.Append("\nend note\n");
        }
        sb.Append("@enduml\n");
        return sb.ToString();
    }

    /// <summary>
    /// The render ladder's arrow-heavy shape (<c>tools/render-bench/gen.js</c>, <c>gen-200</c> at 200): four
    /// participants, <paramref name="steps"/> request/query/reply rounds with a short note on every third and an
    /// audit call on every fifth. Layout, not text measurement, is its cost, which is what an engine regression
    /// of the Teoz kind moves (plans/ENGINE_PIN_PLAN.md §1.8, S4).
    /// </summary>
    public static string BuildArrowHeavyDiagram(int steps)
    {
        var lines = new List<string>
        {
            "@startuml", "skinparam responseMessageBelowArrow true", "participant \"Test\" as T", "participant \"Example.Api\" as A",
            "database \"OrdersDb\" as D", "participant \"External\" as X"
        };
        for (var i = 0; i < steps; i++)
        {
            lines.Add($"T -> A: GET /orders/{i}?include=lines");
            lines.Add($"A -> D: SELECT o.id, o.total FROM orders o WHERE o.id = {i}");
            lines.Add($"D --> A: 1 row (id={i})");
            if (i % 3 == 0)
            {
                lines.Add("note right of A");
                lines.Add($"{{ \"id\": {i}, \"customer\": \"cust-{i}\", \"total\": {(i * 3.5).ToString("F2", System.Globalization.CultureInfo.InvariantCulture)} }}");
                lines.Add("end note");
            }
            if (i % 5 == 0)
            {
                lines.Add($"A -> X: POST /audit/{i}");
                lines.Add("X --> A: 202 Accepted");
            }
            lines.Add($"A --> T: 200 OK ({i * 7 % 40} ms)");
        }
        lines.Add("@enduml");
        return string.Join("\n", lines);
    }

    /// <summary>
    /// A pretty-printed JSON body of 2–8 KB (or ~0.5 KB when <paramref name="small"/>), nested objects and
    /// arrays, every line well under the engine's wrap width.
    /// </summary>
    private static string JsonBody(Random rng, int step, string kind, bool small = false)
    {
        var items = small ? 2 : 6 + rng.Next(0, 18);
        var lines = new List<object>();
        for (var i = 0; i < items; i++)
        {
            lines.Add(new
            {
                sku = $"SKU-{step:D3}-{i:D2}",
                name = $"Widget {kind} {i}",
                qty = rng.Next(1, 9),
                price = Math.Round(rng.NextDouble() * 100, 2),
                attributes = new { colour = i % 2 == 0 ? "blue" : "red", size = i % 3 == 0 ? "L" : "M", warehouse = $"wh-{rng.Next(1, 4)}" },
                tags = new[] { "a" + i, "b" + rng.Next(0, 99), "c" }
            });
        }
        var body = new
        {
            orderId = $"{kind}-{step}-{rng.Next(1000, 9999)}",
            customer = new { name = "Ada Lovelace", email = "ada@example.test", address = new { line1 = "1 Analytical Way", city = "London", postcode = "N1 1AA" } },
            items = lines,
            status = kind == "response" ? "created" : "pending"
        };
        return JsonSerializer.Serialize(body, new JsonSerializerOptions { WriteIndented = true });
    }
}
