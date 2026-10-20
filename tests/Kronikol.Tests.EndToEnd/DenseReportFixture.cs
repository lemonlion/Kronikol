using System.Diagnostics;
using System.Net;
using System.Text.Json;
using Kronikol.InternalFlow;
using Kronikol.Reports;
using Kronikol.Tracking;
using static Kronikol.DefaultDiagramsFetcher;

namespace Kronikol.Tests.EndToEnd;

/// <summary>
/// Reports the size of a payload-heavy suite: <c>features</c> features of <c>scenariosPerFeature</c> scenarios, 750 at
/// the defaults, the shape of a consumer's report of 750 payload-dense scenarios whose Features tab and features sort
/// froze the page in 5.0.0. Deterministic: names, results, durations and bodies come from the scenario's position.
/// </summary>
public static class DenseReportFixture
{
    public const int DefaultFeatures = 75;
    public const int DefaultScenariosPerFeature = 10;
    public const int DefaultCallsPerScenario = 4;

    /// <summary>
    /// Each scenario has three steps and a sequence diagram of <c>callsPerScenario</c> request rounds whose notes carry
    /// multi-KB JSON bodies (<see cref="LargeReportFixture.BuildDiagram"/>), and no other diagram.
    /// </summary>
    public static string Generate(string tempDir, string outputDir, string fileName, int features = DefaultFeatures,
        int scenariosPerFeature = DefaultScenariosPerFeature, int callsPerScenario = DefaultCallsPerScenario)
    {
        var diagrams = new List<DiagramAsCode>();
        var featureList = Enumerable.Range(1, features).Select(f => new Feature
        {
            DisplayName = $"Feature {f:000}",
            Scenarios = Enumerable.Range(1, scenariosPerFeature).Select(s =>
            {
                var index = (f - 1) * scenariosPerFeature + (s - 1);
                var id = $"f{f}s{s}";
                diagrams.Add(new DiagramAsCode(id, "", LargeReportFixture.BuildDiagram(index, callsPerScenario)));
                return NewScenario(id, f, s, index);
            }).ToArray()
        }).ToArray();

        var path = ReportGenerator.GenerateHtmlReport(
            diagrams.ToArray(), featureList,
            DateTime.UtcNow, DateTime.UtcNow,
            null, Path.Combine(tempDir, fileName), "Dense Report", true,
            diagramFormat: DiagramFormat.PlantUml,
            plantUmlRendering: PlantUmlRendering.BrowserJs);

        File.Copy(path, Path.Combine(outputDir, fileName), true);
        return new Uri(path).AbsoluteUri;
    }

    /// <summary>
    /// The same suite written by the whole pipeline with internal-flow tracking on: each scenario makes
    /// <c>callsPerScenario</c> calls whose request and response carry JSON bodies of several KB, with spans under each,
    /// so every scenario has three diagram views (Sequence, Activity, Flame) on a tab bar, as in a consumer's report
    /// with internal flow. With <paramref name="everyFilter"/> each scenario also draws its Given, When and Then step
    /// bars, reads its basket from a database before the When step (its setup), publishes an event and reads the order
    /// back, so the report has every filter toggle a consumer's has: Steps, Databases, Events and Setup. Without
    /// <paramref name="internalFlow"/> no spans are recorded and tracking is off, so each scenario has its sequence
    /// diagram alone: half the diagrams to draw where a test draws every one.
    /// </summary>
    public static string GenerateWithFlows(string tempDir, string outputDir, string fileName, int features = DefaultFeatures,
        int scenariosPerFeature = DefaultScenariosPerFeature, int callsPerScenario = DefaultCallsPerScenario,
        bool everyFilter = false, bool internalFlow = true)
    {
        var reportsDir = Path.Combine(tempDir, "dense-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(reportsDir);
        var sourceName = "Kronikol.Tests.Dense.E2E." + Guid.NewGuid().ToString("N");
        using var source = new ActivitySource(sourceName);
        using var listener = new ActivityListener
        {
            ShouldListenTo = s => s.Name == sourceName,
            Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllDataAndRecorded
        };
        ActivitySource.AddActivityListener(listener);
        var spans = new List<Activity>();
        var at = DateTimeOffset.UtcNow.AddHours(-2);

        void Span(string name, ActivityTraceId trace, DateTimeOffset start, int ms)
        {
            Activity.Current = null;
            var span = source.StartActivity(name, ActivityKind.Internal,
                new ActivityContext(trace, ActivitySpanId.CreateRandom(), ActivityTraceFlags.Recorded))!;
            span.SetStartTime(start.UtcDateTime);
            span.SetEndTime(start.UtcDateTime.AddMilliseconds(ms));
            spans.Add(span);
        }

        lock (ReportTestHelper.WholePipeline)
        {
            Reset();
            var featureList = new List<Feature>();
            for (var f = 1; f <= features; f++)
            {
                var scenarios = new List<Scenario>();
                for (var s = 1; s <= scenariosPerFeature; s++)
                {
                    var index = (f - 1) * scenariosPerFeature + (s - 1);
                    var scenario = NewScenario($"dense-{f}-{s}-" + Guid.NewGuid().ToString("N")[..8], f, s, index);
                    var rng = new Random(1000 + index);
                    var start = at.AddSeconds(index);
                    if (everyFilter)
                    {
                        Step(scenario, "Given a customer with a basket");
                        Database(scenario, rng, index, "basket", start.AddMilliseconds(-150));
                        Step(scenario, "When the order is placed");
                    }
                    for (var c = 0; c < callsPerScenario; c++)
                    {
                        var path = $"/orders/{index}-{c}";
                        var id = Guid.NewGuid();
                        var trace = ActivityTraceId.CreateRandom();
                        var t = start.AddMilliseconds(c * 200);
                        RequestResponseLogger.Log(new RequestResponseLog(scenario.DisplayName, scenario.Id, HttpMethod.Post,
                            JsonBody(rng, index, c, "request"), new Uri("http://orders" + path), [], "Orders", "Caller",
                            RequestResponseType.Request, Guid.NewGuid(), id, false) { Timestamp = t, ActivityTraceId = trace.ToString() });
                        RequestResponseLogger.Log(new RequestResponseLog(scenario.DisplayName, scenario.Id, HttpMethod.Post,
                            JsonBody(rng, index, c, "response"), new Uri("http://orders" + path), [], "Orders", "Caller",
                            RequestResponseType.Response, Guid.NewGuid(), id, false, HttpStatusCode.Created)
                            { Timestamp = t.AddMilliseconds(100), ActivityTraceId = trace.ToString() });
                        if (internalFlow)
                        {
                            Span("POST " + path, trace, t.AddMilliseconds(1), 60);
                            Span("INSERT orders " + index + "-" + c, trace, t.AddMilliseconds(10), 20);
                        }
                    }
                    if (everyFilter)
                    {
                        var published = start.AddMilliseconds(callsPerScenario * 200);
                        var eventId = Guid.NewGuid();
                        RequestResponseLogger.Log(new RequestResponseLog(scenario.DisplayName, scenario.Id, "Publish OrderPlaced",
                            JsonBody(rng, index, callsPerScenario, "event"), new Uri("amqp:///order-events"), [], "Order Events", "Orders",
                            RequestResponseType.Request, Guid.NewGuid(), eventId, false, MetaType: RequestResponseMetaType.Event,
                            DependencyCategory: Constants.DependencyCategories.MessageQueue) { Timestamp = published });
                        RequestResponseLogger.Log(new RequestResponseLog(scenario.DisplayName, scenario.Id, "Publish OrderPlaced", null,
                            new Uri("amqp:///order-events"), [], "Order Events", "Orders", RequestResponseType.Response, Guid.NewGuid(),
                            eventId, false, MetaType: RequestResponseMetaType.Event,
                            DependencyCategory: Constants.DependencyCategories.MessageQueue) { Timestamp = published.AddMilliseconds(5) });
                        Step(scenario, "Then the order is created");
                        Database(scenario, rng, index, "order", published.AddMilliseconds(50));
                    }
                    scenarios.Add(scenario);
                }
                featureList.Add(new Feature { DisplayName = $"Feature {f:000}", Scenarios = scenarios.ToArray() });
            }
            foreach (var span in spans)
                InternalFlowSpanStore.Add(span);

            ReportGenerator.CreateStandardReportsWithDiagrams(featureList.ToArray(), at.UtcDateTime.AddSeconds(-1), DateTime.UtcNow,
                new ReportConfigurationOptions
                {
                    ReportsFolderPath = reportsDir,
                    PlantUmlRendering = PlantUmlRendering.BrowserJs,
                    InternalFlowTracking = internalFlow,
                    InternalFlowSpanGranularity = InternalFlowSpanGranularity.Full,
                    GenerateComponentDiagram = false,
                    GenerateSpecificationsReport = false,
                    GenerateSpecificationsData = false,
                });
            Reset();
        }
        foreach (var span in spans) span.Dispose();

        var html = Path.Combine(reportsDir, "TestRunReport.html");
        File.Copy(html, Path.Combine(outputDir, fileName), true);
        return new Uri(html).AbsoluteUri;
    }

    /// <summary>A step bar in the scenario's sequence diagram, as Kronikol's step tracking draws one.</summary>
    private static void Step(Scenario scenario, string text) =>
        DefaultTrackingDiagramOverride.InsertPlantUml(scenario.Id, Kronikol.PlantUml.StepBarPlantUml.Build(text), DiagramMarkerKind.Step);

    /// <summary>A query of the orders database whose rows come back as a JSON body of several KB.</summary>
    private static void Database(Scenario scenario, Random rng, int index, string what, DateTimeOffset at)
    {
        var id = Guid.NewGuid();
        var query = $"SELECT * FROM {what}s WHERE customer_id = {index}";
        var uri = new Uri($"postgres://orders-db/{what}s");
        RequestResponseLogger.Log(new RequestResponseLog(scenario.DisplayName, scenario.Id, "SELECT", query, uri, [], "Orders DB", "Orders",
            RequestResponseType.Request, Guid.NewGuid(), id, false, DependencyCategory: Constants.DependencyCategories.SQL) { Timestamp = at });
        RequestResponseLogger.Log(new RequestResponseLog(scenario.DisplayName, scenario.Id, "SELECT", JsonBody(rng, index, 9, what), uri, [],
            "Orders DB", "Orders", RequestResponseType.Response, Guid.NewGuid(), id, false,
            DependencyCategory: Constants.DependencyCategories.SQL) { Timestamp = at.AddMilliseconds(20) });
    }

    private static Scenario NewScenario(string id, int feature, int number, int index)
    {
        var failed = index % 37 == 0;
        return new Scenario
        {
            Id = id,
            DisplayName = $"Scenario {feature:000}.{number:00} places an order of {index % 9 + 1} lines",
            Result = failed ? ExecutionResult.Failed : ExecutionResult.Passed,
            ErrorMessage = failed ? "Expected status 201 but was 500" : null,
            Duration = TimeSpan.FromMilliseconds(40 + index * 7919 % 1900),
            IsHappyPath = number == 1,
            Steps =
            [
                new ScenarioStep { Keyword = "Given", Text = "a customer with a basket", Status = ExecutionResult.Passed },
                new ScenarioStep { Keyword = "When", Text = "the order is placed", Status = failed ? ExecutionResult.Failed : ExecutionResult.Passed },
                new ScenarioStep { Keyword = "Then", Text = "the order is created", Status = failed ? ExecutionResult.Skipped : ExecutionResult.Passed }
            ]
        };
    }

    /// <summary>A pretty-printed JSON body of 2 to 8 KB: an order of nested lines, as a payload-heavy API carries.</summary>
    private static string JsonBody(Random rng, int index, int call, string kind)
    {
        var lines = Enumerable.Range(0, 6 + rng.Next(0, 18)).Select(i => new
        {
            sku = $"SKU-{index:D4}-{call}-{i:D2}",
            name = $"Widget {kind} {i}",
            qty = rng.Next(1, 9),
            price = Math.Round(rng.NextDouble() * 100, 2),
            attributes = new { colour = i % 2 == 0 ? "blue" : "red", size = i % 3 == 0 ? "L" : "M", warehouse = $"wh-{rng.Next(1, 4)}" },
            tags = new[] { "a" + i, "b" + rng.Next(0, 99), "c" }
        }).ToArray();
        return JsonSerializer.Serialize(new
        {
            orderId = $"{kind}-{index}-{call}-{rng.Next(1000, 9999)}",
            customer = new { name = "Ada Lovelace", email = "ada@example.test", address = new { line1 = "1 Analytical Way", city = "London", postcode = "N1 1AA" } },
            lines
        }, new JsonSerializerOptions { WriteIndented = true });
    }
}
