using System.Net;
using System.Text.Json.Nodes;
using System.Xml.Linq;
using System.Xml.Schema;
using Kronikol.Query;
using Kronikol.Reports;
using Kronikol.Reports.Merge;

namespace Kronikol.Tests.Reports;

/// <summary>
/// #148 R3 (<c>plans/MISSING_ATTACHMENTS_PLAN.md</c> section 4.3): an attachment the report does not hold says so wherever
/// it is read. The run's <c>AttachmentFailure</c> entry counted it, but every reader still showed its path as a file: the
/// page drew a broken image or a dead link, and the data files, <c>Failures.md</c>, <c>Failures.jsonl</c> and
/// <c>kronikol query</c> printed the path as if the file were there.
/// </summary>
[Collection("DiagramsFetcher")]
public class AttachmentProblemTests : IDisposable
{
    private const string NotFound = "not found when the report was drawn";

    private static readonly DateTime Start = new(2026, 1, 1, 10, 0, 0, DateTimeKind.Utc);

    private readonly string _dir = Path.Combine(Path.GetTempPath(), "kronikol-attachment-problem-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        RunRotation.ForgetForTests(Reports);
        try { Directory.Delete(_dir, true); } catch { /* best effort */ }
    }

    private string PathOf(params string[] parts) => Path.Combine([_dir, .. parts]);

    private string Reports => PathOf("Reports");

    private string Written(string relative)
    {
        var path = PathOf(relative.Split('/'));
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllBytes(path, [137, 80, 78, 71]);
        return path;
    }

    /// <summary>
    /// A failed scenario holding a present <c>shot.png</c> and a missing <c>lost.png</c> of its own, and one step holding a
    /// missing <c>trace.zip</c>, run through the whole generator. Returns the missing files' paths.
    /// </summary>
    private (string Lost, string Trace) RunWithMissingFiles(Action<ReportConfigurationOptions>? configure = null)
    {
        var shot = Written("sources/shot.png");
        var lost = PathOf("gone", "lost.png");
        var trace = PathOf("gone", "trace.zip");
        Feature[] features =
        [
            new Feature
            {
                DisplayName = "Checkout",
                Scenarios =
                [
                    new Scenario
                    {
                        Id = "checkout-test",
                        DisplayName = "Pay",
                        Result = ExecutionResult.Failed,
                        ErrorMessage = "Pay failed",
                        Attachments = [new FileAttachment("shot.png", shot, "image/png"), new FileAttachment("lost.png", lost, "image/png")],
                        Steps =
                        [
                            new ScenarioStep
                            {
                                Keyword = "When", Text = "the card is charged", Status = ExecutionResult.Failed,
                                Attachments = [new FileAttachment("trace.zip", trace, "application/zip")],
                            },
                        ],
                    },
                ],
            },
        ];
        var options = new ReportConfigurationOptions
        {
            ReportsFolderPath = Reports,
            PlantUmlRendering = PlantUmlRendering.BrowserJs,
            GenerateComponentDiagram = false,
            WriteRunSummaryToConsole = false,
            // the environment below answers nothing, which switches history on: the ledger is the run's own
            HistoryFilePath = PathOf("history.ndjson"),
        };
        configure?.Invoke(options);
        var collector = new ReportDiagnosticsCollector();
        DefaultDiagramsFetcher.Reset();
        using (ReportDiagnosticsScope.Begin(collector))
            ReportGenerator.CreateStandardReportsWithDiagramsInEnvironment(features, Start, Start.AddMinutes(1), options, RunEnvironment.Unrecorded, _ => null);
        DefaultDiagramsFetcher.Reset();
        return (lost, trace);
    }

    private static string Query(params string[] args)
    {
        var output = new StringWriter();
        var error = new StringWriter();
        Assert.True(QueryCommand.Run(args, output, error) == 0, error.ToString());
        return output.ToString();
    }

    /// <summary>Every object in a JSON tree whose <paramref name="nameKey"/> is <paramref name="name"/>.</summary>
    private static JsonObject[] Named(JsonNode? node, string nameKey, string name) => node switch
    {
        JsonObject o => [.. (o[nameKey] is JsonValue v && v.TryGetValue<string>(out var s) && s == name ? [o] : Array.Empty<JsonObject>()),
            .. o.SelectMany(p => Named(p.Value, nameKey, name))],
        JsonArray a => [.. a.SelectMany(item => Named(item, nameKey, name))],
        _ => [],
    };

    /// <summary>The rich schema fixture, which holds a <c>lost.png</c> the report does not hold, written in one format.</summary>
    private static string WriteRichFixture(DataFormat format, string extension) =>
        ReportGenerator.GenerateTestRunReportData(TestRunReportSchemaContractTests.RichFeatures(), TestRunReportSchemaContractTests.Start,
            TestRunReportSchemaContractTests.End, $"AttachmentProblem_{Guid.NewGuid():N}.{extension}", format,
            TestRunReportSchemaContractTests.Diagrams(), TestRunReportSchemaContractTests.Logs(), TestRunReportSchemaContractTests.Diagnostics());

    /// <summary>A failed scenario whose one step holds a <c>trace.zip</c> the report could not copy in.</summary>
    private static Feature[] OneStepWithAProblem() =>
        [
            new Feature
            {
                DisplayName = "Checkout",
                Scenarios =
                [
                    new Scenario
                    {
                        Id = "checkout-test", DisplayName = "Pay", Result = ExecutionResult.Failed,
                        Steps =
                        [
                            new ScenarioStep
                            {
                                Keyword = "When", Text = "the card is charged", Status = ExecutionResult.Failed,
                                Attachments = [new FileAttachment("trace.zip", "C:/run/trace.zip") { Problem = AttachmentProblem.NotCopied }],
                            },
                        ],
                    },
                ],
            },
        ];

    // ─── The model ───────────────────────────────────────────────

    [Fact]
    public void A_run_keeps_its_history_in_its_own_folder()
    {
        // An environment that answers nothing switches history on, and with no path named the ledger went to the repository
        // the test ran in (.kronikol/history.jsonl at its root), where every later run of the history facts read it.
        RunWithMissingFiles();

        Assert.True(File.Exists(PathOf("history.ndjson")), "the run wrote its ledger outside its own folder");
    }

    [Fact]
    public void The_state_is_public_for_a_host_that_draws_its_own_view()
    {
        Assert.True(typeof(FileAttachment).GetProperty("Problem")?.GetMethod?.IsPublic == true, "FileAttachment.Problem is not public");
        Assert.True(typeof(AttachmentProblem).IsPublic, "AttachmentProblem is not public");
    }

    // ─── The data files ──────────────────────────────────────────

    [Fact]
    public void The_json_data_file_says_which_attachment_the_report_does_not_hold()
    {
        var (lost, _) = RunWithMissingFiles();

        var root = JsonNode.Parse(ReportFile.ReadAny(Path.Combine(Reports, "TestRunReport.json")));

        var missing = Assert.Single(Named(root, "name", "lost.png"));
        Assert.Equal(lost, (string?)missing["relativePath"]);
        Assert.True(missing.ContainsKey("problem"), missing.ToJsonString());
        Assert.Equal("NotFound", (string?)missing["problem"]);
        // An attachment the report holds is written as before: no key at all.
        Assert.False(Assert.Single(Named(root, "name", "shot.png")).ContainsKey("problem"));
    }

    [Fact]
    public void The_xml_data_file_says_it_only_on_the_attachment_it_does_not_hold()
    {
        var attachments = XDocument.Load(WriteRichFixture(DataFormat.Xml, "xml")).Descendants("Attachment").ToArray();

        Assert.Equal("NotFound", attachments.Single(a => a.Element("Name")?.Value == "lost.png").Element("Problem")?.Value);
        Assert.Null(attachments.Single(a => a.Element("Name")?.Value == "receipt.png").Element("Problem"));
    }

    [Fact]
    public void The_yaml_data_file_says_it_only_on_the_attachment_it_does_not_hold()
    {
        var root = YamlSchemaValidationTests.YamlToJson(ReportHtml.Read(WriteRichFixture(DataFormat.Yaml, "yml")));

        Assert.Equal("NotFound", (string?)Assert.Single(Named(root, "Name", "lost.png"))["Problem"]);
        Assert.False(Assert.Single(Named(root, "Name", "receipt.png")).ContainsKey("Problem"));
    }

    [Fact]
    public void The_yaml_data_file_says_it_on_a_steps_attachment_too()
    {
        // A step's attachments are written by a writer of their own.
        var features = OneStepWithAProblem();
        var path = ReportGenerator.GenerateTestRunReportData(features, Start, Start.AddMinutes(1), $"AttachmentProblem_{Guid.NewGuid():N}.yml",
            DataFormat.Yaml, [], [], []);

        var root = YamlSchemaValidationTests.YamlToJson(ReportHtml.Read(path));

        Assert.Equal("NotCopied", (string?)Assert.Single(Named(root, "Name", "trace.zip"))["Problem"]);
    }

    [Fact]
    public void An_xml_data_file_with_a_steps_problem_validates_against_its_own_xsd()
    {
        // A step's attachments are declared by a type of their own in the XSD.
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var data = ReportGenerator.GenerateTestRunReportData(OneStepWithAProblem(), Start, Start.AddMinutes(1), $"AttachmentProblem_{suffix}.xml",
            DataFormat.Xml, [], [], []);
        var schema = ReportGenerator.GenerateTestRunReportSchema($"AttachmentProblem_{suffix}.schema.xsd", DataFormat.Xml);
        var set = new XmlSchemaSet();
        set.Add("", System.Xml.XmlReader.Create(new StringReader(ReportHtml.Read(schema))));

        var errors = new List<string>();
        var document = XDocument.Load(data);
        document.Validate(set, (_, e) => errors.Add(e.Message));

        Assert.Equal("NotCopied", document.Descendants("Attachment").Single().Element("Problem")?.Value);
        Assert.True(errors.Count == 0, string.Join("\n  ", errors));
    }

    [Fact]
    public void A_mergeable_data_file_keeps_the_state_for_the_merge()
    {
        RunWithMissingFiles(o => o.GenerateMergeableData = true);

        var report = MergeableReportReader.ReadFile(ReportFile.Resolve(Path.Combine(Reports, "TestRunReport.json")));

        var attachments = report.Features.SelectMany(f => f.Scenarios)
            .SelectMany(s => (s.Attachments ?? []).Concat((s.Steps ?? []).SelectMany(step => step.Attachments ?? []))).ToArray();
        Assert.Equal(AttachmentProblem.NotFound, attachments.Single(a => a.Name == "lost.png").Problem);
        Assert.Equal(AttachmentProblem.NotFound, attachments.Single(a => a.Name == "trace.zip").Problem);
        Assert.Null(attachments.Single(a => a.Name == "shot.png").Problem);
    }

    [Fact]
    public void A_mergeable_data_file_with_no_problem_key_reads_as_no_problem()
    {
        // A file written before the key existed: the same run with every problem key taken out. Every release before the
        // key wrote the JSON, so this run does too.
        RunWithMissingFiles(o =>
        {
            o.GenerateMergeableData = true;
            o.CompressTestRunReport = false;
        });
        var path = Path.Combine(Reports, "TestRunReport.json");
        File.WriteAllText(path, System.Text.RegularExpressions.Regex.Replace(ReportHtml.Read(path), @",\s*""problem""\s*:\s*""[A-Za-z]+""", ""));

        var report = MergeableReportReader.ReadFile(path);

        Assert.All(report.Features.SelectMany(f => f.Scenarios).SelectMany(s => s.Attachments ?? []), a => Assert.Null(a.Problem));
    }

    // ─── The digest ──────────────────────────────────────────────

    [Fact]
    public void Failures_md_says_the_state_after_the_path()
    {
        var (lost, trace) = RunWithMissingFiles();

        var digest = ReportHtml.Read(Path.Combine(Reports, "Failures.md"));

        Assert.Contains("- shot.png — `attachments/shot.png`\n", digest, StringComparison.Ordinal);
        Assert.Contains($"- lost.png — `{lost}` ({NotFound})\n", digest, StringComparison.Ordinal);
        Assert.Contains($"- trace.zip — `{trace}` ({NotFound}; step `s0/0`)\n", digest, StringComparison.Ordinal);
    }

    [Fact]
    public void Failures_jsonl_carries_the_state_only_where_there_is_one()
    {
        var (lost, trace) = RunWithMissingFiles();

        var line = Assert.Single(File.ReadAllLines(Path.Combine(Reports, "Failures.jsonl")), l => l.Contains("\"attachments\"", StringComparison.Ordinal));
        var attachments = (JsonArray)JsonNode.Parse(line)!["attachments"]!;

        Assert.Equal("NotFound", (string?)Assert.Single(Named(attachments, "name", "lost.png"))["problem"]);
        Assert.Equal("NotFound", (string?)Assert.Single(Named(attachments, "name", "trace.zip"))["problem"]);
        Assert.False(Assert.Single(Named(attachments, "name", "shot.png")).ContainsKey("problem"));
        Assert.Equal(lost, (string?)Assert.Single(Named(attachments, "name", "lost.png"))["path"]);
        Assert.Equal(trace, (string?)Assert.Single(Named(attachments, "name", "trace.zip"))["path"]);
    }

    // ─── kronikol query ──────────────────────────────────────────

    [Fact]
    public void Query_failures_marks_the_attachment_and_prints_its_path_as_recorded()
    {
        var (lost, trace) = RunWithMissingFiles();

        var output = Query("failures", Reports);

        Assert.Contains($"attachment: lost.png → {lost} ({NotFound})", output, StringComparison.Ordinal);
        Assert.Contains($"attachment: trace.zip → {trace} ({NotFound})", output, StringComparison.Ordinal);
        Assert.Contains($"attachment: shot.png → {Path.Combine(Reports, "attachments", "shot.png")}\n", output.Replace("\r\n", "\n"), StringComparison.Ordinal);
    }

    [Fact]
    public void Query_never_resolves_a_relative_path_the_report_does_not_hold()
    {
        // Resolved against the report's folder, a relative path the report does not hold named a file that never was, and
        // on another machine a path invented from that machine's folder (F13): the recorded text is the evidence.
        var features = new[]
        {
            new Feature
            {
                DisplayName = "Checkout",
                Scenarios =
                [
                    new Scenario
                    {
                        Id = "checkout-test", DisplayName = "Pay", Result = ExecutionResult.Failed, ErrorMessage = "Pay failed",
                        Attachments = [new FileAttachment("nowhere.png", "nowhere/at-all.png", "image/png")],
                    },
                ],
            },
        };
        DefaultDiagramsFetcher.Reset();
        using (ReportDiagnosticsScope.Begin(new ReportDiagnosticsCollector()))
            ReportGenerator.CreateStandardReportsWithDiagramsInEnvironment(features, Start, Start.AddMinutes(1),
                new ReportConfigurationOptions
                {
                    ReportsFolderPath = Reports, PlantUmlRendering = PlantUmlRendering.BrowserJs, GenerateComponentDiagram = false,
                    WriteRunSummaryToConsole = false, HistoryFilePath = PathOf("history.ndjson"),
                }, RunEnvironment.Unrecorded, _ => null);
        DefaultDiagramsFetcher.Reset();

        var output = Query("failures", Reports);

        Assert.Contains($"attachment: nowhere.png → nowhere/at-all.png ({NotFound})", output, StringComparison.Ordinal);
        Assert.DoesNotContain(Path.Combine(Reports, "nowhere"), output, StringComparison.Ordinal);
    }

    [Fact]
    public void Query_steps_and_attachments_mark_it_too()
    {
        var (lost, trace) = RunWithMissingFiles();

        Assert.Contains($"attachment: trace.zip → {trace} ({NotFound})", Query("steps", Reports, "s0"), StringComparison.Ordinal);
        var listed = Query("attachments", Reports, "s0");
        Assert.Contains($"lost.png → {lost} ({NotFound})", listed, StringComparison.Ordinal);
        Assert.Contains($"trace.zip → {trace} ({NotFound})", listed, StringComparison.Ordinal);
    }

    [Fact]
    public void Query_json_carries_the_state_only_where_there_is_one()
    {
        var (lost, _) = RunWithMissingFiles();

        var root = JsonNode.Parse(Query("failures", Reports, "--json"));

        var missing = Assert.Single(Named(root, "name", "lost.png"));
        Assert.Equal("NotFound", (string?)missing["problem"]);
        Assert.Equal(lost, (string?)missing["path"]);
        Assert.False(Assert.Single(Named(root, "name", "shot.png")).ContainsKey("problem"));
    }

    // ─── The page ────────────────────────────────────────────────

    [Fact]
    public void The_page_draws_an_attachment_it_does_not_hold_as_its_name_and_state_with_no_link()
    {
        var (lost, trace) = RunWithMissingFiles();

        var html = ReportHtml.Read(Path.Combine(Reports, "TestRunReport.html"));

        // The path in a title only (Q3), so a published page shows no machine path in its text.
        Assert.Contains($"<span class=\"attachment-missing\" title=\"{WebUtility.HtmlEncode(lost)}\">lost.png ({NotFound})</span>", html, StringComparison.Ordinal);
        Assert.Contains($"<span class=\"attachment-missing\" title=\"{WebUtility.HtmlEncode(trace)}\">trace.zip ({NotFound})</span>", html, StringComparison.Ordinal);
        foreach (var path in new[] { lost, trace })
        {
            var href = ReportGenerator.AttachmentHref(new FileAttachment("x", path));
            Assert.DoesNotContain($"href=\"{href}\"", html, StringComparison.Ordinal);
            Assert.DoesNotContain($"src=\"{href}\"", html, StringComparison.Ordinal);
        }
        // The one it holds is drawn as before.
        Assert.Contains("src=\"attachments/shot.png\"", html, StringComparison.Ordinal);
    }
}
