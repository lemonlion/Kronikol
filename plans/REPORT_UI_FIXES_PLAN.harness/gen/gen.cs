#:package Kronikol@5.8.0
#:property JsonSerializerIsReflectionEnabledByDefault=true
#:property AssemblyName=formatter-probe
// The probe pages for plans/REPORT_UI_FIXES_PLAN.md section 3, from Kronikol 5.8.0's own emitter and generator (the
// assembly is named formatter-probe because Kronikol.csproj grants that name its internals, as
// tools/render-bench/emitter-corpus does):
//   main.html        a BrowserJs report: tag and dependency chips (I1); a long diagram drawn in parts, its notes large
//                    payloads, with step bars holding tables (I2, I5, I6); database and broker calls collapsed into
//                    loops, in the Setup box and after it (I3); names with punctuation (I4); the Timeline with a
//                    warm-up (I7)
//   history-51.html, history-7.html   the History section alone, as the labs page draws it, for 51 and 7 runs (I8)
//   names.txt        what the name functions make of names with punctuation (I4)
// Usage: dotnet run --file gen.cs -- <absolute out dir>
using System.Net;
using System.Text;
using Kronikol;
using Kronikol.History;
using Kronikol.PlantUml;
using Kronikol.Reports;
using Kronikol.Tracking;

var outDir = Path.GetFullPath(args.Length > 0 ? args[0] : Directory.GetCurrentDirectory());
Directory.CreateDirectory(outDir);
var t0 = new DateTimeOffset(2026, 10, 26, 10, 0, 0, TimeSpan.Zero);

// ---- one call, a request and its response, as the trackers log them
IEnumerable<RequestResponseLog> Call(string test, string caller, string service, string method, string url, double atMs,
    double tookMs, string? body = "{}", string? reply = "{}", string? category = null,
    RequestResponseMetaType meta = RequestResponseMetaType.Default)
{
    var (trace, id) = (Guid.NewGuid(), Guid.NewGuid());
    yield return new RequestResponseLog(test, test, method, body, new Uri(url), [("Content-Type", "application/json")], service, caller,
        RequestResponseType.Request, trace, id, false, null, meta, category) { Timestamp = t0.AddMilliseconds(atMs) };
    yield return new RequestResponseLog(test, test, method, reply, new Uri(url), [("Content-Type", "application/json")], service, caller,
        RequestResponseType.Response, trace, id, false, HttpStatusCode.OK, meta, category) { Timestamp = t0.AddMilliseconds(atMs + tookMs) };
}

// a step bar, as StepCollector.StartStep splices it into the test's diagram (an override start and end)
IEnumerable<RequestResponseLog> Step(string test, string label, string[][]? table, double atMs, string? doc = null, StepBarTable[]? named = null)
{
    IReadOnlyList<StepBarTable>? tables = named ?? (table is null ? null : new[] { new StepBarTable(null, table) });
    var bar = StepBarPlantUml.Build(label, tables, doc);
    RequestResponseLog Marker(bool start) => new(test, test, "", "", new Uri("http://override.com"), [], "", "",
        RequestResponseType.Request, Guid.NewGuid(), Guid.NewGuid(), false)
    {
        IsOverrideStart = start, IsOverrideEnd = !start, MarkerKind = DiagramMarkerKind.Step,
        PlantUml = start ? "\n" + bar + "\n\n" : null, Timestamp = t0.AddMilliseconds(atMs)
    };
    yield return Marker(true);
    yield return Marker(false);
}

RequestResponseLog ActionStart(string test, double atMs) => new(test, test, "", "", new Uri("http://override.com"), [], "", "",
    RequestResponseType.Request, Guid.NewGuid(), Guid.NewGuid(), false) { IsActionStart = true, MarkerKind = DiagramMarkerKind.Phase, Timestamp = t0.AddMilliseconds(atMs) };

static string Json(int lines, int seed)
{
    var sb = new StringBuilder("{\n  \"items\": [\n");
    var items = Math.Max(1, (lines - 4) / 6);
    for (var i = 0; i < items; i++)
        sb.Append("    {\n").Append($"      \"id\": \"{seed:D3}-{i:D5}\",\n")
          .Append($"      \"name\": \"Breakfast item number {i} with a longer descriptive name\",\n")
          .Append($"      \"price\": {(i * 37 % 1000) / 100.0:0.00},\n")
          .Append($"      \"tags\": [\"warm\", \"sweet\", \"batch-{i % 7}\"]\n").Append(i < items - 1 ? "    },\n" : "    }\n");
    return sb.Append("  ],\n  \"total\": ").Append(items).Append("\n}").ToString();
}

string SourceOf(IEnumerable<RequestResponseLog> logs, bool separateSetup) =>
    PlantUmlCreator.GetPlantUmlImageTagsPerTestId(logs, separateSetup: separateSetup, collapseConsecutiveIdenticalCalls: true,
        clientSideSplitting: true, systemUiFont: true).Single().PlantUmls.Single().PlainText;

string[][] Ingredients = [["Ingredient", "Quantity", "Unit"], ["Flour", "500", "g"], ["Milk", "300", "ml"], ["Eggs", "2", "whole"]];
StepParameter IngredientsParameter() => new()
{
    Name = "table", Kind = StepParameterKind.Tabular,
    TabularValue = new TabularParameterValue(
        [new TabularColumn("Ingredient", false), new TabularColumn("Quantity", false), new TabularColumn("Unit", false)],
        Ingredients.Skip(1).Select(r => new TabularRow(TableRowType.Matching, r.Select(v => new TabularCell(v, null, VerificationStatus.NotApplicable)).ToArray())).ToArray())
};

// ---- I3: loops of database and broker calls, in the Setup box and after it. The Setup box holds a Given bar and a loop
// of database calls only, so Databases off leaves the bar in it, and Databases and Steps off leave it empty.
var milk = new List<RequestResponseLog>();
milk.AddRange(Step("milk", "Given the herd is ready", null, 0));
for (var i = 0; i < 3; i++) milk.AddRange(Call("milk", "Test", "MilkDb", "Execute", "sql://milk-db/Cows", 10 + i * 10, 5, category: "SQL"));
milk.Add(ActionStart("milk", 50));
milk.AddRange(Step("milk", "When the milk is collected", null, 51));
milk.AddRange(Call("milk", "Test", "CowService", "PUT", "http://cows.internal/herd", 55, 4));
milk.AddRange(Call("milk", "Test", "CowService", "POST", "http://cows.internal/milk", 60, 5));
for (var i = 0; i < 3; i++) milk.AddRange(Call("milk", "CowService", "MilkDb", "Query", "sql://milk-db/Cows", 70 + i * 10, 5, category: "SQL"));
for (var i = 0; i < 3; i++) milk.AddRange(Call("milk", "CowService", "MilkEvents", "Publish", "amqp://milk-events/collected", 110 + i * 10, 5, category: "MessageQueue", meta: RequestResponseMetaType.Event));
milk.AddRange(Step("milk", "Then the milk is in the tank", null, 150));
var milkSource = SourceOf(milk, separateSetup: true);

// ---- I2, I5, I6: a long diagram, drawn in parts, its notes large payloads, step bars with tables
var big = new List<RequestResponseLog>();
big.AddRange(Step("big", "Given the kitchen has these ingredients", Ingredients, 0));
for (var i = 0; i < 40; i++)
{
    if (i == 20) big.AddRange(Step("big", "When the order is placed for these ingredients", Ingredients, 1000));
    var lines = i % 10 == 3 ? 400 : i % 10 is 1 or 5 or 8 ? 150 : 24;
    big.AddRange(Call("big", "Test", i % 2 == 0 ? "OrdersApi" : "MenuApi", "GET", $"http://breakfast.internal/api/items?page={i}", 10 + i * 40, 20,
        body: "{ \"page\": " + i + " }", reply: Json(lines, i)));
}
big.AddRange(Step("big", "Then the order is confirmed", null, 2000));
var bigSource = SourceOf(big, separateSetup: false);

// ---- a short one, so the rows have more than one name each
var menu = new List<RequestResponseLog>();
menu.AddRange(Call("menu", "Test", "MenuApi", "GET", "http://breakfast.internal/api/menu", 0, 12, reply: Json(12, 1)));
var menuSource = SourceOf(menu, separateSetup: false);

// ---- I7: a run whose first call paid a warm-up (WarmUpMarkTests.WriteOrders' shape)
var warm = new List<RequestResponseLog>();
warm.AddRange(Call("place", "Test", "orders", "POST", "http://orders/orders", 0, 1500));
warm.AddRange(Call("again", "Test", "orders", "POST", "http://orders/orders", 3000, 5));
warm.AddRange(Call("again", "Test", "orders", "POST", "http://orders/orders", 3100, 5));

Scenario S(string id, string name, double ms, string[]? tags = null, ScenarioStep[]? steps = null) =>
    new() { Id = id, DisplayName = name, Result = ExecutionResult.Passed, Duration = TimeSpan.FromMilliseconds(ms), Tags = tags, Steps = steps };

Feature[] features =
[
    new()
    {
        DisplayName = "Dairy", Tags = ["dairy"],
        Scenarios =
        [
            S("milk", "Collect milk", 160, ["smoke"],
            [
                new() { Keyword = "Given", Text = "the herd is ready", Status = ExecutionResult.Passed },
                new() { Keyword = "When", Text = "the milk is collected", Status = ExecutionResult.Passed },
                new() { Keyword = "Then", Text = "the milk is in the tank", Status = ExecutionResult.Passed }
            ]),
            S("big", "Bake a large breakfast order", 2100, ["slow"],
            [
                new() { Keyword = "Given", Text = "the kitchen has these ingredients", Status = ExecutionResult.Passed, Parameters = [IngredientsParameter()] },
                new() { Keyword = "When", Text = "the order is placed for these ingredients", Status = ExecutionResult.Passed, Parameters = [IngredientsParameter()] },
                new() { Keyword = "Then", Text = "the order is confirmed", Status = ExecutionResult.Passed }
            ]),
            S("menu", "Browse the menu", 12, ["smoke", "fast"])
        ]
    },
    new()
    {
        // I4: the heading ReqNRoll writes for "Feature: Month's reports via gRPC" (ScenarioInfoEnumerableExtensions.cs:82)
        DisplayName = StringCasing.Titleize("Month's reports via gRPC"),
        Scenarios = [S("names1", "A cake is baked on the month's birthday", 5), S("names2", "Returns 404 (not found)", 5)]
    },
    new() { DisplayName = "Orders", Scenarios = [S("place", "Place", 2000), S("again", "Place again", 1200), S("browse", "Browse", 300)] }
];

var main = ReportGenerator.GenerateHtmlReport(
    [new DefaultDiagramsFetcher.DiagramAsCode("milk", "", milkSource), new DefaultDiagramsFetcher.DiagramAsCode("big", "", bigSource),
     new DefaultDiagramsFetcher.DiagramAsCode("menu", "", menuSource)],
    features, t0.UtcDateTime, t0.UtcDateTime.AddMinutes(1), null, Path.Combine(outDir, "main.html"), "UI fixes probe", true,
    diagramFormat: DiagramFormat.PlantUml, plantUmlRendering: PlantUmlRendering.BrowserJs, trackedLogs: warm.ToArray());
File.WriteAllText(Path.Combine(outDir, "milk.puml"), milkSource);
File.WriteAllText(Path.Combine(outDir, "big.puml"), bigSource);
Console.WriteLine($"{main}  (milk {milkSource.Length:N0}, big {bigSource.Length:N0} chars of source)");

// ---- I6: step bars, the same diagram in five forms, one scenario each
const string Rich = "hnote across <<stepDelimiter>><<stepBody>>: ";
var doc = "{ \"order\": 42,\n  \"items\": [\"toast\", \"eggs\"],\n  \"paid\": true }";
var bars = new List<RequestResponseLog>();
bars.AddRange(Step("bars", "Given the kitchen has these ingredients", Ingredients, 0));
bars.AddRange(Call("bars", "Test", "MenuApi", "GET", "http://breakfast.internal/api/menu", 5, 10));
bars.AddRange(Step("bars", "When the request body is sent", null, 20, doc: doc));
bars.AddRange(Call("bars", "Test", "OrdersApi", "POST", "http://breakfast.internal/api/orders", 25, 10));
bars.AddRange(Step("bars", "Then these were used", null, 40, named:
    [new StepBarTable("inputs", [["Ingredient", "Quantity"], ["Flour", "500"], ["Milk", "300"]]),
     new StepBarTable("outputs", [["Item", "Count", "Price per item"], ["Pancake", "12", "1.50"]])]));
bars.AddRange(Step("bars", "And the order is confirmed for table number twelve by the waiter on duty", null, 50));
var barsSource = SourceOf(bars, separateSetup: false);

// a row's cells with their single spaces replaced by `left` and `right` (cell pipes are escaped as <U+007C>, so a bare
// pipe only separates)
static string PadRow(string row, string left, string right)
{
    var sb = new StringBuilder();
    foreach (var c in row.Split('|')[1..^1])
    {
        var header = c.StartsWith('=');
        sb.Append(header ? "|=" : "|").Append(left).Append((header ? c[1..] : c).Trim()).Append(right);
    }
    return sb.Append('|').ToString();
}
static string Variant(string source, bool centre, string? left = null, string? right = null, string? docBar = null) =>
    string.Join('\n', source.Split('\n').Select(line =>
    {
        if (centre && line.Trim() == ".stepBody {") return line + "\n     HorizontalAlignment center";
        if (!line.StartsWith(Rich)) return line;
        if (docBar is not null && line.Contains("When the request body is sent")) return docBar;
        if (left is null) return line;
        var segs = line[Rich.Length..].Split(@"\n");
        return Rich + string.Join(@"\n", segs.Select(s => s.StartsWith('|') && s.EndsWith('|') ? PadRow(s, left, right!) : s));
    }));
const string Strut = "<sup><U+200B></sup>", StrutBelow = "<sub><U+200B></sub>";
var docBox = Rich + @"When the request body is sent\n\n| " + doc.Replace("\n", @"\\n") + " |";
(string Id, string Name, string Source)[] barForms =
[
    ("bars0", "Step bars 0, as today", barsSource),
    ("bars1", "Step bars 1, centred", Variant(barsSource, centre: true)),
    ("bars2", "Step bars 2, centred, cells padded 8 px", Variant(barsSource, centre: true, "<space:8>", "<space:8>")),
    ("bars3", "Step bars 3, centred, cells padded 8 px with struts", Variant(barsSource, centre: true, "<space:8>" + Strut, StrutBelow + "<space:8>")),
    ("bars4", "Step bars 4, centred, the doc string a boxed cell", Variant(barsSource, centre: true, docBar: docBox)),
];
Feature[] barFeatures =
[
    new()
    {
        DisplayName = "Step bars",
        Scenarios = barForms.Select(f => S(f.Id, f.Name, 50, null,
        [
            new() { Keyword = "Given", Text = "the kitchen has these ingredients", Status = ExecutionResult.Passed, Parameters = [IngredientsParameter()] },
            new() { Keyword = "When", Text = "the request body is sent", Status = ExecutionResult.Passed, DocString = doc },
            new() { Keyword = "Then", Text = "these were used", Status = ExecutionResult.Passed },
            new() { Keyword = "And", Text = "the order is confirmed for table number twelve by the waiter on duty", Status = ExecutionResult.Passed }
        ])).ToArray()
    }
];
var barsReport = ReportGenerator.GenerateHtmlReport(barForms.Select(f => new DefaultDiagramsFetcher.DiagramAsCode(f.Id, "", f.Source)).ToArray(),
    barFeatures, t0.UtcDateTime, t0.UtcDateTime.AddMinutes(1), null, Path.Combine(outDir, "stepbars.html"), "Step bars probe", true,
    diagramFormat: DiagramFormat.PlantUml, plantUmlRendering: PlantUmlRendering.BrowserJs);
foreach (var f in barForms) File.WriteAllText(Path.Combine(outDir, f.Id + ".puml"), f.Source);
Console.WriteLine(barsReport);

// ---- I8: the History section alone, as HistoryRingsTests draws it
foreach (var n in new[] { 51, 7 })
{
    var runs = Enumerable.Range(1, n)
        .Select(i => new RunPoint($"t:{i}:1", t0.AddHours(i), null, 200, i % 9 == 0 ? 1 : 0, i % 9 == 0 ? 201 : 200, 60_000L + i * 1_000L, false))
        .ToArray();
    var verdicts = new HistoryVerdicts
    {
        Suite = "Rings", Stream = "local", RunId = runs[^1].RunId, RunsRecorded = n - 1, MinRuns = 5, ColdStart = false, ColdStartMessage = null,
        Partial = false, Scenarios = [], Absent = [], Counts = new Dictionary<HistoryVerdictKind, int>(), Runs = runs, NewDependencies = [],
        FirstSeen = 0, Compare = null
    };
    var page = "<!DOCTYPE html><html><head><meta charset=\"utf-8\"><meta name=\"viewport\" content=\"width=device-width\"><style>"
        + Stylesheets.ReportTokensStyleSheet + Stylesheets.HtmlReportStyleSheet + Stylesheets.HistoryStyleSheet
        + "</style></head><body>" + HistoryHtml.Section(verdicts, null) + "</body></html>";
    File.WriteAllText(Path.Combine(outDir, $"history-{n}.html"), page);
}

// ---- I4: the name functions on names with punctuation
string[] names =
[
    "Month's birthday", "Month’s birthday", "O'Brien's order", "Don't bake twice", "Order Cross-Field Validation",
    "Order Status via gRPC", "Reporting EventGrid Webhook", "Orders: retry on 503", "Returns 404 (not found)",
    "Order \"VIP\" lane", "Salt & pepper", "Is it ready?", "Order #42", "v1.2 release", "HTTP request returns 200",
    "Café orders", "Pancakes 🥞 flip", "the month's report"
];
var sbNames = new StringBuilder("input\tTitleize\tFormatScenarioDisplayName\n");
foreach (var n in names)
    sbNames.Append(n).Append('\t').Append(StringCasing.Titleize(n)).Append('\t').Append(ScenarioTitleResolver.FormatScenarioDisplayName(n)).Append('\n');
File.WriteAllText(Path.Combine(outDir, "names.txt"), sbNames.ToString());
Console.WriteLine(sbNames);
