using Kronikol.Ingestion;
using Kronikol.Reports;
using Kronikol.Tracking;

namespace Kronikol.Tests.Reports;

/// <summary>
/// Part D of the report contract: <b>one broken diagram must never cost the reader the rest of the
/// report</b>. A render that throws, a formatting processor that throws, an output file that cannot be
/// written — each is isolated, recorded as a diagnostic, and everything else is still produced.
/// </summary>
[Collection("DiagramsFetcher")]
public class DiagramFailureIsolationTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "kronikol-isolation-" + Guid.NewGuid().ToString("N"));
    private static readonly DateTimeOffset T0 = new(2026, 8, 22, 10, 0, 0, TimeSpan.Zero);

    private const string GoodTest = "11111111111111111111111111111111";
    private const string BadTest = "22222222222222222222222222222222";

    public DiagramFailureIsolationTests()
    {
        Directory.CreateDirectory(_dir);
        RequestResponseLogger.Redaction = null;
    }

    public void Dispose()
    {
        RequestResponseLogger.Redaction = null;
        DefaultDiagramsFetcher.Reset();
        try { Directory.Delete(_dir, true); } catch { /* best effort */ }
    }

    /// <summary>Two scenarios, each with one identifiable call.</summary>
    private IngestRequest BuildRequest(ReportConfigurationOptions options)
    {
        var records = new List<InteractionRecord>();
        foreach (var (testId, marker) in new[] { (GoodTest, "healthy"), (BadTest, "poisoned") })
        {
            var (request, response) = InteractionRecord.Pair(testId, $"scenario {marker}", "GET", $"http://localhost/{marker}",
                "api", "web", responseContent: marker, statusCode: "200",
                requestTimestamp: T0.AddSeconds(1), responseTimestamp: T0.AddSeconds(2));
            records.Add(request);
            records.Add(response);
        }

        return new IngestRequest
        {
            Interactions = records,
            TestRecords =
            [
                new TestRunRecord { Event = "start", TestId = GoodTest, TestName = "scenario healthy", Timestamp = T0 },
                new TestRunRecord { Event = "end", TestId = GoodTest, Status = "passed", Timestamp = T0.AddSeconds(3) },
                new TestRunRecord { Event = "start", TestId = BadTest, TestName = "scenario poisoned", Timestamp = T0 },
                new TestRunRecord { Event = "end", TestId = BadTest, Status = "passed", Timestamp = T0.AddSeconds(3) },
            ],
            Options = options,
        };
    }

    private ReportConfigurationOptions Options(string subdirectory)
    {
        var options = IngestPipeline.DefaultOptions();
        options.ReportsFolderPath = Path.Combine(_dir, subdirectory);
        options.GenerateComponentDiagram = false;
        return options;
    }

    [Fact]
    public void A_renderer_that_throws_for_one_scenario_leaves_every_other_diagram_intact()
    {
        var options = Options("render");
        options.PlantUmlRendering = PlantUmlRendering.Local;
        options.PlantUmlImageFormat = PlantUmlImageFormat.Base64Svg;
        // The renderer only knows the PlantUML it is handed, so it identifies the doomed diagram by the
        // call that is unique to that scenario — exactly how a real renderer would blow up on one input.
        options.LocalDiagramRenderer = (plantUml, _) => plantUml.Contains("poisoned")
            ? throw new TimeoutException("the render process did not answer")
            : System.Text.Encoding.UTF8.GetBytes($"<svg>{plantUml.GetHashCode()}</svg>");

        var result = IngestPipeline.Run(BuildRequest(options));

        Assert.True(result.Generated);
        Assert.True(File.Exists(result.TestRunReportHtml));

        var html = File.ReadAllText(result.TestRunReportHtml);
        // The healthy scenario still has its rendered image…
        Assert.Contains("data:image/svg+xml;base64,", html);

        // …and the broken one shows the note instead of its diagram. Locally rendered reports embed the
        // source verbatim, so the marker is legible in the HTML as well as in the data file.
        Assert.Contains("diagram could not be generated", html);
        Assert.Contains("TimeoutException", html);
        Assert.Contains("hnote across <<renderError>>", DiagramsFromDataFile(result.ReportsDirectory)[BadTest]);

        var failure = Assert.Single(result.Diagnostics, d => d.Kind == DiagnosticKind.RenderFailure);
        Assert.Equal(BadTest, failure.ScenarioId);
        Assert.Contains("TimeoutException", failure.Message);
    }

    [Fact]
    public void A_formatting_processor_that_throws_for_one_scenario_leaves_every_other_diagram_intact()
    {
        // The processor runs while the PlantUML is being built — before any renderer is involved — and
        // used to take every diagram in the report with it.
        var options = Options("format");
        options.RequestResponsePostProcessor = text => text.Contains("poisoned")
            ? throw new InvalidDataException("this body confused the formatter")
            : text;

        var result = IngestPipeline.Run(BuildRequest(options));

        Assert.True(result.Generated);
        Assert.True(File.Exists(result.TestRunReportHtml));

        // The browser-rendered HTML carries its PlantUML compressed, so the data file is where the
        // diagram source is legible — and it is the same source the report renders.
        var diagrams = DiagramsFromDataFile(result.ReportsDirectory);
        Assert.Contains("healthy", diagrams[GoodTest]);
        Assert.DoesNotContain("could not be generated", diagrams[GoodTest]);
        Assert.Contains("hnote across <<renderError>>", diagrams[BadTest]);
        Assert.Contains("InvalidDataException: this body confused the formatter", diagrams[BadTest]);

        var failure = Assert.Single(result.Diagnostics, d => d.Kind == DiagnosticKind.RenderFailure && d.ScenarioId == BadTest);
        // The real cause, not the "One or more errors occurred" wrapper the parallel build hands back.
        Assert.Contains("InvalidDataException: this body confused the formatter", failure.Message);
    }

    /// <summary>The PlantUML source the report published, per scenario id.</summary>
    private static Dictionary<string, string> DiagramsFromDataFile(string reportsDirectory)
    {
        using var document = System.Text.Json.JsonDocument.Parse(
            File.ReadAllText(Path.Combine(reportsDirectory, "TestRunReport.json")));

        return document.RootElement.GetProperty("features").EnumerateArray()
            .SelectMany(f => f.GetProperty("scenarios").EnumerateArray())
            .ToDictionary(
                s => s.GetProperty("id").GetString()!,
                s => string.Join("\n", s.GetProperty("diagrams").EnumerateArray().Select(d => d.GetString())));
    }

    [Fact]
    public void An_output_that_cannot_be_written_does_not_stop_the_others()
    {
        var options = Options("outputs");
        var reportsDirectory = ReportGenerator.ResolveReportsDirectory(options);
        Directory.CreateDirectory(reportsDirectory);

        // A directory where the data file wants to be: every write to that path fails, nothing else does.
        Directory.CreateDirectory(Path.Combine(reportsDirectory, "TestRunReport.json"));

        var result = IngestPipeline.Run(BuildRequest(options));

        Assert.True(result.Generated);
        Assert.True(File.Exists(result.TestRunReportHtml));
        Assert.True(File.Exists(Path.Combine(reportsDirectory, "Specifications.html")));

        var failure = Assert.Single(result.Diagnostics, d => d.Kind == DiagnosticKind.OutputFailure);
        Assert.Contains("TestRunReport.json", failure.Message);
    }

    [Fact]
    public void The_placeholder_is_valid_plant_uml_that_names_the_failure()
    {
        var plantUml = DefaultDiagramsFetcher.RenderErrorPlantUml(new TimeoutException("no answer"));

        Assert.StartsWith("@startuml", plantUml);
        Assert.EndsWith("@enduml", plantUml);
        Assert.Contains("hnote across <<renderError>> #ffdddd", plantUml);
        Assert.Contains("⚠ diagram could not be generated: TimeoutException: no answer", plantUml);
        Assert.Contains("end note", plantUml);
    }

    [Fact]
    public void A_multi_line_exception_message_stays_on_one_note_line()
    {
        // A note body that spills onto its own lines produces PlantUML the renderer cannot parse — the
        // placeholder would then be as useless as the diagram it replaced.
        var plantUml = DefaultDiagramsFetcher.RenderErrorPlantUml(new InvalidOperationException("first\r\nsecond"));

        var noteBody = plantUml.Split('\n')[2];
        Assert.Contains("first second", noteBody);
        Assert.Equal("end note", plantUml.Split('\n')[3]);
    }
}
