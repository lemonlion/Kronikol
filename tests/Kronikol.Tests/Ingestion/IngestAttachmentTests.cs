using System.Text.Json;
using Kronikol.Ingestion;
using Kronikol.Reports;
using Kronikol.Tracking;

namespace Kronikol.Tests.Ingestion;

/// <summary>
/// The <c>attachment</c> event: screenshots, traces and links produced by an external runner, carried
/// into the report as scenario- or step-level artefacts.
/// </summary>
[Collection("DiagramsFetcher")]
public class IngestAttachmentTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "kronikol-attach-" + Guid.NewGuid().ToString("N"));
    private static readonly DateTimeOffset T0 = new(2026, 8, 22, 10, 0, 0, TimeSpan.Zero);
    private const string TestId = "44444444444444444444444444444444";

    public IngestAttachmentTests()
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

    private string WriteArtefact(string name, string content = "artefact")
    {
        var path = Path.Combine(_dir, name);
        File.WriteAllText(path, content);
        return path;
    }

    private IngestResult Run(string subdirectory, IEnumerable<TestRunRecord> testRecords, string? attachmentsBase = null, bool clean = false)
    {
        var (request, response) = InteractionRecord.Pair(TestId, "overview", "GET", "http://localhost/overview", "api", "web",
            responseContent: "{}", statusCode: "200", requestTimestamp: T0.AddSeconds(1), responseTimestamp: T0.AddSeconds(2));

        var options = IngestPipeline.DefaultOptions();
        options.ReportsFolderPath = Path.Combine(_dir, subdirectory);
        options.GenerateComponentDiagram = false;

        return IngestPipeline.Run(new IngestRequest
        {
            Interactions = [request, response],
            TestRecords = testRecords,
            Options = options,
            AttachmentsBase = attachmentsBase,
            CleanAttachments = clean,
        });
    }

    private static TestRunRecord Start() =>
        new() { Event = "start", TestId = TestId, TestName = "overview renders", Timestamp = T0 };

    private static TestRunRecord End() =>
        new() { Event = "end", TestId = TestId, Status = "passed", Timestamp = T0.AddSeconds(5) };

    [Fact]
    public void An_attachment_record_round_trips_through_ndjson()
    {
        var record = new TestRunRecord
        {
            Event = "attachment",
            TestId = TestId,
            Name = "screenshot-start.png",
            Path = "/tmp/screenshot-start.png",
            MediaType = "image/png",
            Step = 1,
            Timestamp = T0,
        };

        var round = TestRunRecord.FromJson(record.ToJson());

        Assert.Equal(record, round);
        Assert.Contains("\"mediaType\":\"image/png\"", record.ToJson());
        Assert.Contains("\"step\":1", record.ToJson());
        // Attachments are not diagram markers: they belong to the step list, not the sequence diagram.
        Assert.False(round.IsDiagramMarker);
    }

    [Fact]
    public void A_scenario_level_attachment_is_copied_into_the_report_and_rendered_inline_when_it_is_an_image()
    {
        var screenshot = WriteArtefact("screenshot-end.png");
        var trace = WriteArtefact("trace.zip");

        var result = Run("scenario-level",
        [
            Start(),
            new TestRunRecord { Event = "attachment", TestId = TestId, Name = "screenshot-end.png", Path = screenshot, MediaType = "image/png", Timestamp = T0.AddSeconds(3) },
            new TestRunRecord { Event = "attachment", TestId = TestId, Name = "trace.zip", Path = trace, MediaType = "application/zip", Timestamp = T0.AddSeconds(4) },
            End(),
        ]);

        var scenario = result.Features[0].Scenarios[0];
        Assert.Equal(2, scenario.Attachments!.Length);
        // Copied next to the report so the HTML resolves when the folder is published as a CI artefact.
        Assert.All(scenario.Attachments!, a => Assert.StartsWith("attachments/", a.RelativePath));
        Assert.True(File.Exists(Path.Combine(result.ReportsDirectory, "attachments", "screenshot-end.png")));

        var html = File.ReadAllText(result.TestRunReportHtml);
        Assert.Contains("<img class=\"attachment-image\" src=\"attachments/screenshot-end.png\"", html);
        Assert.Contains("<a class=\"step-attachment\" href=\"attachments/trace.zip\">trace.zip</a>", html);
    }

    [Fact]
    public void The_media_type_decides_inline_rendering_even_when_the_extension_disagrees()
    {
        // A screenshot written without an extension is still an image; a .png that is really a report
        // page is still a link. The producer knows, and says so.
        var image = WriteArtefact("screenshot-start");
        var notAnImage = WriteArtefact("report.png");

        var result = Run("media-type",
        [
            Start(),
            new TestRunRecord { Event = "attachment", TestId = TestId, Name = "screenshot-start", Path = image, MediaType = "image/webp", Timestamp = T0.AddSeconds(3) },
            new TestRunRecord { Event = "attachment", TestId = TestId, Name = "report.png", Path = notAnImage, MediaType = "text/html", Timestamp = T0.AddSeconds(4) },
            End(),
        ]);

        var html = File.ReadAllText(result.TestRunReportHtml);
        Assert.Contains("src=\"attachments/screenshot-start\"", html);
        Assert.Contains("<a class=\"step-attachment\" href=\"attachments/report.png\">report.png</a>", html);
    }

    [Theory]
    [InlineData("shot.png", null, true)]
    [InlineData("shot.svg", null, true)]
    [InlineData("shot.avif", null, true)]
    [InlineData("shot.webp", null, true)]
    [InlineData("trace.zip", null, false)]
    [InlineData("clip.webm", null, false)]
    [InlineData("anything", "image/avif", true)]
    [InlineData("shot.png", "application/octet-stream", false)]
    public void Inline_image_detection_prefers_the_media_type_and_falls_back_to_the_extension(string name, string? mediaType, bool inline) =>
        Assert.Equal(inline, new FileAttachment(name, "attachments/" + name, mediaType).IsInlineImage);

    [Fact]
    public void A_step_index_files_the_attachment_under_that_top_level_step()
    {
        var first = WriteArtefact("first.png");
        var second = WriteArtefact("second.png");

        var result = Run("step-level",
        [
            Start(),
            new TestRunRecord { Event = "step", TestId = TestId, Text = "the page loads", Timestamp = T0.AddSeconds(1) },
            new TestRunRecord { Event = "step", TestId = TestId, Text = "the trial is accepted", Timestamp = T0.AddSeconds(2) },
            new TestRunRecord { Event = "attachment", TestId = TestId, Name = "first.png", Path = first, MediaType = "image/png", Step = 0, Timestamp = T0.AddSeconds(3) },
            new TestRunRecord { Event = "attachment", TestId = TestId, Name = "second.png", Path = second, MediaType = "image/png", Step = 1, Timestamp = T0.AddSeconds(4) },
            End(),
        ]);

        var scenario = result.Features[0].Scenarios[0];
        Assert.Null(scenario.Attachments);
        Assert.Equal("first.png", Assert.Single(scenario.Steps![0].Attachments!).Name);
        Assert.Equal("second.png", Assert.Single(scenario.Steps![1].Attachments!).Name);
    }

    [Fact]
    public void A_step_index_that_no_longer_resolves_falls_back_to_the_scenario_rather_than_losing_the_artefact()
    {
        var orphan = WriteArtefact("orphan.png");

        var result = Run("orphan",
        [
            Start(),
            new TestRunRecord { Event = "step", TestId = TestId, Text = "the page loads", Timestamp = T0.AddSeconds(1) },
            new TestRunRecord { Event = "attachment", TestId = TestId, Name = "orphan.png", Path = orphan, Step = 7, Timestamp = T0.AddSeconds(3) },
            End(),
        ]);

        var scenario = result.Features[0].Scenarios[0];
        Assert.Equal("orphan.png", Assert.Single(scenario.Attachments!).Name);
    }

    [Fact]
    public void A_url_attachment_is_rendered_as_a_link_and_never_copied()
    {
        var result = Run("urls",
        [
            Start(),
            new TestRunRecord
            {
                Event = "attachment", TestId = TestId, Name = "Playwright report",
                Path = "http://localhost:5050/report/index.html#?testId=abc", Timestamp = T0.AddSeconds(3),
            },
            End(),
        ]);

        var attachment = Assert.Single(result.Features[0].Scenarios[0].Attachments!);
        // Untouched: a URL is not a path, and handing it to the path APIs would throw on Windows.
        Assert.Equal("http://localhost:5050/report/index.html#?testId=abc", attachment.RelativePath);
        Assert.False(Directory.Exists(Path.Combine(result.ReportsDirectory, "attachments")));
        Assert.Contains("href=\"http://localhost:5050/report/index.html#?testId=abc\"", File.ReadAllText(result.TestRunReportHtml));
    }

    [Fact]
    public void A_relative_path_is_resolved_against_the_attachments_base()
    {
        var artefacts = Path.Combine(_dir, "artefacts");
        Directory.CreateDirectory(artefacts);
        File.WriteAllText(Path.Combine(artefacts, "relative.png"), "png");

        var result = Run("relative",
        [
            Start(),
            new TestRunRecord { Event = "attachment", TestId = TestId, Name = "relative.png", Path = "relative.png", MediaType = "image/png", Timestamp = T0.AddSeconds(3) },
            End(),
        ], attachmentsBase: artefacts);

        Assert.Equal("attachments/relative.png", Assert.Single(result.Features[0].Scenarios[0].Attachments!).RelativePath);
        Assert.True(File.Exists(Path.Combine(result.ReportsDirectory, "attachments", "relative.png")));
    }

    [Fact]
    public void Cleaning_leaves_the_folder_holding_exactly_this_run_s_artefacts()
    {
        var current = WriteArtefact("current.png");
        var reportsDirectory = Path.Combine(_dir, "clean");
        var attachmentsDirectory = Path.Combine(ReportGenerator.ResolveReportsDirectory(
            new ReportConfigurationOptions { ReportsFolderPath = reportsDirectory }), "attachments");
        Directory.CreateDirectory(attachmentsDirectory);
        File.WriteAllText(Path.Combine(attachmentsDirectory, "stale.png"), "from a previous run");

        var result = Run("clean",
        [
            Start(),
            new TestRunRecord { Event = "attachment", TestId = TestId, Name = "current.png", Path = current, MediaType = "image/png", Timestamp = T0.AddSeconds(3) },
            End(),
        ], clean: true);

        var copied = Directory.GetFiles(Path.Combine(result.ReportsDirectory, "attachments")).Select(f => Path.GetFileName(f)!).ToArray();
        Assert.Equal(["current.png"], copied);
    }

    [Fact]
    public void Without_cleaning_a_previous_run_s_artefacts_survive()
    {
        var current = WriteArtefact("current.png");
        var reportsDirectory = Path.Combine(_dir, "keep");
        var attachmentsDirectory = Path.Combine(ReportGenerator.ResolveReportsDirectory(
            new ReportConfigurationOptions { ReportsFolderPath = reportsDirectory }), "attachments");
        Directory.CreateDirectory(attachmentsDirectory);
        File.WriteAllText(Path.Combine(attachmentsDirectory, "stale.png"), "from a previous run");

        var result = Run("keep",
        [
            Start(),
            new TestRunRecord { Event = "attachment", TestId = TestId, Name = "current.png", Path = current, MediaType = "image/png", Timestamp = T0.AddSeconds(3) },
            End(),
        ]);

        var copied = Directory.GetFiles(Path.Combine(result.ReportsDirectory, "attachments")).Select(f => Path.GetFileName(f)!).Order().ToArray();
        Assert.Equal(["current.png", "stale.png"], copied);
    }

    [Fact]
    public void The_media_type_reaches_the_json_xml_and_yaml_data_files()
    {
        var screenshot = WriteArtefact("data.png");
        var records = new List<TestRunRecord>
        {
            Start(),
            new() { Event = "attachment", TestId = TestId, Name = "data.png", Path = screenshot, MediaType = "image/png", Timestamp = T0.AddSeconds(3) },
            End(),
        };

        var json = Run("data-json", records);
        using var document = JsonDocument.Parse(File.ReadAllText(Path.Combine(json.ReportsDirectory, "TestRunReport.json")));
        var attachment = document.RootElement.GetProperty("features")[0].GetProperty("scenarios")[0].GetProperty("attachments")[0];
        Assert.Equal("image/png", attachment.GetProperty("mediaType").GetString());

        var schema = File.ReadAllText(Path.Combine(json.ReportsDirectory, "TestRunReport.schema.json"));
        Assert.Contains("\"mediaType\"", schema);

        var xml = RunWithFormat("data-xml", records, DataFormat.Xml);
        Assert.Contains("<MediaType>image/png</MediaType>", File.ReadAllText(Path.Combine(xml.ReportsDirectory, "TestRunReport.xml")));
        Assert.Contains("name=\"MediaType\"", File.ReadAllText(Path.Combine(xml.ReportsDirectory, "TestRunReport.schema.xsd")));

        var yaml = RunWithFormat("data-yaml", records, DataFormat.Yaml);
        Assert.Contains("MediaType: image/png", File.ReadAllText(Path.Combine(yaml.ReportsDirectory, "TestRunReport.yml")));
    }

    private IngestResult RunWithFormat(string subdirectory, IEnumerable<TestRunRecord> testRecords, DataFormat format)
    {
        var (request, response) = InteractionRecord.Pair(TestId, "overview", "GET", "http://localhost/overview", "api", "web",
            responseContent: "{}", statusCode: "200", requestTimestamp: T0.AddSeconds(1), responseTimestamp: T0.AddSeconds(2));

        var options = IngestPipeline.DefaultOptions();
        options.ReportsFolderPath = Path.Combine(_dir, subdirectory);
        options.GenerateComponentDiagram = false;
        options.TestRunReportDataFormat = format;

        return IngestPipeline.Run(new IngestRequest
        {
            Interactions = [request, response],
            TestRecords = testRecords,
            Options = options,
        });
    }
}
