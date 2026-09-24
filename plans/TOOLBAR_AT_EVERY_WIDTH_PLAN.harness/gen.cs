#:project ../../src/Kronikol/Kronikol.csproj
#:property PublishAot=false
#:property JsonSerializerIsReflectionEnabledByDefault=true
// Generates the report shapes the P2 plan measures, against the working tree at HEAD.
// Output: ./out/*.html (five shapes). `dotnet run gen.cs -- stress` writes ./out-stress/*.html instead:
// the shapes that vary what the fixture held fixed (long CI strings). `-- full` writes ./out-full/*.html:
// every control the scenario toolbar can carry (diagram tabs from whole-test flow views, Assertions and
// Steps toggles from <<assertionNote>> and <<stepDelimiter>>, Databases, the opt-in note font select) and
// a component diagram in the top bar, short and long CI strings. OUT_DIR overrides the folder.
using Kronikol;
using Kronikol.InternalFlow;
using Kronikol.Reports;
using Kronikol.Tracking;
using static Kronikol.DefaultDiagramsFetcher;

Environment.SetEnvironmentVariable("KRONIKOL_HISTORY", "off");
Environment.SetEnvironmentVariable("KRONIKOL_KEEP_RUNS", "off");

var mode = args.Length > 0 ? args[0] : "";
var stress = mode == "stress";
var full = mode == "full";
var outDir = Path.Combine(Directory.GetCurrentDirectory(),
    Environment.GetEnvironmentVariable("OUT_DIR") ?? (stress ? "out-stress" : full ? "out-full" : "out"));
Directory.CreateDirectory(outDir);

// Seventeen dependencies shaped like the published BreakfastProvider report's vocabulary
// (memory: 17 names, lengths 6-31, median 12, 254 label chars in total). Names are synthetic.
string[] deps =
[
    "Azure Event Hub", "Reporting Database (SQL Server)", "Redis Cache", "Kafka Broker", "Orders API",
    "Payment Gateway", "MongoDB Atlas", "Cosmos DB", "Blob Storage", "Service Bus", "Elasticsearch",
    "Postgres Database", "ClickHouse", "Inventory Service", "Notification Service (SMTP)",
    "gRPC Pricing Service", "Storage Queue"
];
Console.WriteLine($"deps={deps.Length} chars={deps.Sum(d => d.Length)} min={deps.Min(d => d.Length)} max={deps.Max(d => d.Length)}");

var puml = new System.Text.StringBuilder("@startuml\nactor \"Caller\" as caller\n");
for (var i = 0; i < deps.Length; i++)
    puml.Append((i % 5 == 1 ? "database" : "participant") + $" \"{deps[i]}\" as p{i}\n");
puml.Append("caller -> p0 : POST /api/orders\nnote left\n{\"item\":\"Widget\",\"qty\":2}\nend note\np0 -> p1 : INSERT INTO Orders\np1 --> p0 : OK\np0 --> caller : 201 Created\n@enduml\n");
var source = puml.ToString();

string[][] tags = [["Happy Path", "Smoke"], ["Happy Path", "Regression"], ["Azure"], ["Happy Path"], [], ["Slow", "Nightly"]];
var features = new[]
{
    new Feature
    {
        DisplayName = "Order Feature",
        Scenarios =
        [
            Sc("t1", "Create order successfully", true, ExecutionResult.Passed, 2000, tags[0]),
            Sc("t2", "Delete order fails gracefully", false, ExecutionResult.Failed, 5000, tags[1]),
            Sc("t3", "List orders returns paginated results", true, ExecutionResult.Passed, 1000, tags[2]),
        ]
    },
    new Feature
    {
        DisplayName = "Payment Feature",
        Scenarios =
        [
            Sc("t4", "Process payment", true, ExecutionResult.Passed, 500, tags[3]),
            Sc("t5", "Refund payment", false, ExecutionResult.Skipped, 100, tags[4]),
            Sc("t6", "Reconcile nightly settlement batch", false, ExecutionResult.Passed, 9000, tags[5]),
        ]
    }
};
var diagrams = new[] { new DiagramAsCode("t1", "", source), new DiagramAsCode("t2", "", source), new DiagramAsCode("t4", "", source) };
var sha = "0123456789abcdef0123456789abcdef01234567";
var ci = new CiMetadata(CiEnvironment.GitHubActions, "1234", "main", sha,
    "https://github.com/example/BreakfastProvider/actions/runs/1", "example/BreakfastProvider", "1");
var now = DateTime.UtcNow;

if (full)
{
    // Step bars and assertion notes, so the Steps and Assertions toggles exist (ReportGenerator gates
    // them on the markers in the source), on the same seventeen participants.
    var fullSource = source.Replace("caller -> p0 : POST /api/orders",
        """
        hnote across <<stepDelimiter>> #black:<color:white>When the request is made
        caller -> p0 : POST /api/orders
        """)
        .Replace("p0 --> caller : 201 Created",
        """
        hnote across <<assertionNote>> #d4edda
        ✓ status code should be created
        end note
        p0 --> caller : 201 Created
        """);
    var fullDiagrams = new[] { new DiagramAsCode("t1", "", fullSource), new DiagramAsCode("t2", "", fullSource), new DiagramAsCode("t4", "", fullSource) };
    using var activitySource = new System.Diagnostics.ActivitySource("P2.Harness");
    using var listener = new System.Diagnostics.ActivityListener
    {
        ShouldListenTo = _ => true,
        Sample = (ref System.Diagnostics.ActivityCreationOptions<System.Diagnostics.ActivityContext> _) => System.Diagnostics.ActivitySamplingResult.AllDataAndRecorded
    };
    System.Diagnostics.ActivitySource.AddActivityListener(listener);
    var segments = new Dictionary<string, InternalFlowSegment>();
    foreach (var id in new[] { "t1", "t2", "t4" })
    {
        System.Diagnostics.Activity.Current = null;
        var root = activitySource.StartActivity("HTTP POST /api/orders", System.Diagnostics.ActivityKind.Server)!;
        root.SetStartTime(now); root.SetEndTime(now.AddMilliseconds(500));
        var ctx = new System.Diagnostics.ActivityContext(root.TraceId, root.SpanId, System.Diagnostics.ActivityTraceFlags.Recorded);
        var child = activitySource.StartActivity("SQL INSERT Orders", System.Diagnostics.ActivityKind.Internal, ctx)!;
        child.SetStartTime(now.AddMilliseconds(20)); child.SetEndTime(now.AddMilliseconds(400));
        segments["iflow-test-" + id] = new(Guid.Empty, RequestResponseType.Request, id, now, now.AddMilliseconds(500), [root, child]);
        child.Stop(); root.Stop();
    }
    var fontControls = ReportToggleDefaultsResolver.Resolve(new ReportConfigurationOptions { ShowNoteFontControls = true }, specifications: false);
    var fontControlsSpec = ReportToggleDefaultsResolver.Resolve(new ReportConfigurationOptions { ShowNoteFontControls = true }, specifications: true);
    const string component = """
        @startuml
        rectangle "Caller" as caller
        rectangle "Orders API" as svc
        caller --> svc
        @enduml
        """;
    var longCiFull = new CiMetadata(CiEnvironment.GitHubActions, "20260922.17",
        "feature/KRON-1234-reconcile-nightly-settlement-batches-across-regions", sha,
        "https://github.com/my-organisation/breakfast-provider-integration-tests/actions/runs/17",
        "my-organisation/breakfast-provider-integration-tests", "17");
    void GenFull(string name, string? stylesheet, bool includeRunData, CiMetadata? meta, ResolvedToggleDefaults toggles)
    {
        var path = ReportGenerator.GenerateHtmlReport(
            fullDiagrams, features, now, now.AddMinutes(3), stylesheet, Path.Combine(outDir, name),
            includeRunData ? "Test Run Report" : "Service Specifications", includeRunData,
            diagramFormat: DiagramFormat.PlantUml, plantUmlRendering: PlantUmlRendering.BrowserJs,
            internalFlowTracking: true, wholeTestSegments: segments, wholeTestVisualization: WholeTestFlowVisualization.Both,
            ciMetadata: meta, componentDiagramPlantUml: includeRunData ? component : null, toggleDefaults: toggles);
        Console.WriteLine($"wrote {path} ({new FileInfo(path).Length} bytes)");
    }
    GenFull("TestRunReport_full.html", null, true, ci, fontControls);
    GenFull("TestRunReport_full_longci.html", null, true, longCiFull, fontControls);
    GenFull("Specifications_full.html", Stylesheets.VioletThemeStyleSheet, false, null, fontControlsSpec);
}
else if (stress)
{
    // The CI box is flex-shrink: 0 and its cells never wrap, so the branch and repository names set
    // the first header row's width. 69 and 52 characters: long, and both real shapes of name.
    var longCi = new CiMetadata(CiEnvironment.GitHubActions, "20260922.17",
        "feature/KRON-1234-reconcile-nightly-settlement-batches-across-regions", sha,
        "https://github.com/my-organisation/breakfast-provider-integration-tests/actions/runs/17",
        "my-organisation/breakfast-provider-integration-tests", "17");
    Gen("TestRunReport_longci.html", null, true, longCi, false);
}
else
{
    Gen("TestRunReport.html", null, true, ci, false);
    Gen("TestRunReport_noci.html", null, true, null, false);
    Gen("TestRunReport_iflow.html", null, true, ci, true);
    Gen("Specifications.html", Stylesheets.VioletThemeStyleSheet, false, null, false);
    Gen("Specifications_iflow.html", Stylesheets.VioletThemeStyleSheet, false, null, true);
}
Console.WriteLine("done " + outDir);

void Gen(string name, string? stylesheet, bool includeRunData, CiMetadata? meta, bool iflow)
{
    var path = ReportGenerator.GenerateHtmlReport(
        diagrams, features, now, now.AddMinutes(3), stylesheet, Path.Combine(outDir, name),
        includeRunData ? "Test Run Report" : "Service Specifications", includeRunData,
        diagramFormat: DiagramFormat.PlantUml, plantUmlRendering: PlantUmlRendering.BrowserJs,
        internalFlowTracking: iflow, ciMetadata: meta);
    Console.WriteLine($"wrote {path} ({new FileInfo(path).Length} bytes)");
}

static Scenario Sc(string id, string name, bool happy, ExecutionResult result, int ms, string[] labels) => new()
{
    Id = id, DisplayName = name, IsHappyPath = happy, Result = result, Duration = TimeSpan.FromMilliseconds(ms), Labels = labels,
    Steps =
    [
        new ScenarioStep { Keyword = "Given", Text = "the system is running", Status = ExecutionResult.Passed },
        new ScenarioStep { Keyword = "When", Text = "the request is made", Status = result == ExecutionResult.Failed ? ExecutionResult.Failed : ExecutionResult.Passed },
        new ScenarioStep { Keyword = "Then", Text = "the response is checked", Status = result == ExecutionResult.Failed ? ExecutionResult.Skipped : ExecutionResult.Passed }
    ]
};
