using System.Text.RegularExpressions;
using Kronikol.Ingestion;
using Kronikol.Query;
using Kronikol.Reports;
using Kronikol.Tracking;

namespace Kronikol.Tests.Reports;

/// <summary>
/// The failures digest and <c>kronikol query failures</c> read a failure's attachments wherever they sit (#175,
/// <c>plans/ATTACHMENT_MERGE_PLAN.md</c> section 4.7). They read the scenario's own list alone, so a screenshot taken
/// inside a step never reached the files an agent reads first, in any lane: the tests file's <c>step</c> index, the
/// Cucumber messages, LightBDD's step attachments, a report <c>kronikol merge</c> read back.
/// </summary>
[Collection("DiagramsFetcher")]
public class DigestStepAttachmentTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "kronikol-digest-attachments-" + Guid.NewGuid().ToString("N"));

    public DigestStepAttachmentTests()
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

    /// <summary>
    /// A failing scenario with an attachment of its own, one on its background step, one on an earlier step, one on the
    /// failing step and one on that step's sub-step.
    /// </summary>
    private static Feature[] Model() =>
    [
        new Feature
        {
            DisplayName = "Checkout",
            Scenarios =
            [
                new Scenario
                {
                    Id = "t0", DisplayName = "Pay", Result = ExecutionResult.Failed, ErrorMessage = "declined",
                    Attachments = [new FileAttachment("page.html", "attachments/page.html", "text/html")],
                    BackgroundSteps =
                    [
                        new ScenarioStep
                        {
                            Keyword = "Given", Text = "a signed-in customer", Status = ExecutionResult.Passed,
                            Attachments = [new FileAttachment("login.png", "attachments/login.png", "image/png")]
                        }
                    ],
                    Steps =
                    [
                        new ScenarioStep
                        {
                            Keyword = "Given", Text = "a basket", Status = ExecutionResult.Passed,
                            Attachments = [new FileAttachment("basket.png", "attachments/basket.png", "image/png")]
                        },
                        new ScenarioStep
                        {
                            Keyword = "When", Text = "the card is charged", Status = ExecutionResult.Failed, FailureMessage = "declined",
                            Attachments = [new FileAttachment("declined.png", "attachments/declined.png", "image/png")],
                            SubSteps =
                            [
                                new ScenarioStep
                                {
                                    Keyword = "And", Text = "the receipt is printed", Status = ExecutionResult.Passed,
                                    Attachments = [new FileAttachment("receipt.pdf", "attachments/receipt.pdf", "application/pdf")]
                                }
                            ]
                        }
                    ]
                }
            ]
        }
    ];

    private static FailuresDigest Generate(Feature[] features) =>
        FailuresDigestGenerator.Generate(features, null, "TestRunReport", "4.24.0");

    [Fact]
    public void Failures_md_lists_the_attachments_made_inside_steps_with_their_step()
    {
        var markdown = Generate(Model()).Markdown;

        var section = markdown[markdown.IndexOf("Attachments:\n\n", StringComparison.Ordinal)..];
        Assert.StartsWith(string.Join("\n",
            "Attachments:",
            "",
            // The scenario's own, as the digest has always written it.
            "- page.html — `attachments/page.html`",
            "- login.png — `attachments/login.png` (step `s0/b0`)",
            "- basket.png — `attachments/basket.png` (step `s0/0`)",
            "- declined.png — `attachments/declined.png` (step `s0/1`)",
            "- receipt.pdf — `attachments/receipt.pdf` (step `s0/1.0`)",
            ""), section, StringComparison.Ordinal);
    }

    [Fact]
    public void Failures_jsonl_attachments_carry_their_step()
    {
        var jsonl = Generate(Model()).Jsonl;

        var attachments = FailuresJsonl.Failures(jsonl).Single().GetProperty("attachments").EnumerateArray().ToArray();
        Assert.Equal(["page.html", "login.png", "basket.png", "declined.png", "receipt.pdf"], attachments.Select(a => a.GetProperty("name").GetString()));
        Assert.Equal([null, "s0/b0", "s0/0", "s0/1", "s0/1.0"],
            attachments.Select(a => a.TryGetProperty("step", out var step) ? step.GetString() : null));
        // The scenario's own object is the one the file has always carried, byte for byte.
        Assert.Contains("\"attachments\":[{\"name\":\"page.html\",\"path\":\"attachments/page.html\"},", jsonl, StringComparison.Ordinal);
    }

    [Fact]
    public void Query_failures_prints_the_failing_steps_attachments_under_it()
    {
        // The tests-file lane: the reporter put the screenshot on the failing step with the step index.
        var shot = Path.Combine(_dir, "declined.png");
        File.WriteAllBytes(shot, [137, 80, 78, 71]);
        var basket = Path.Combine(_dir, "basket.png");
        File.WriteAllBytes(basket, [137, 80, 78, 71, 1]);
        var trace = Path.Combine(_dir, "trace.zip");
        File.WriteAllBytes(trace, [80, 75, 3, 4]);
        var at = DateTimeOffset.Parse("2026-10-15T10:00:00Z");
        var tests = Path.Combine(_dir, "tests.ndjson");
        File.WriteAllLines(tests,
        [
            new TestRunRecord { Event = "start", TestId = "t0", TestName = "Pay", Feature = "Checkout", Timestamp = at }.ToJson(),
            new TestRunRecord { Event = "step", TestId = "t0", Keyword = "Given", Text = "a basket", Status = "passed", Timestamp = at.AddSeconds(1) }.ToJson(),
            new TestRunRecord { Event = "attachment", TestId = "t0", Name = "basket.png", Path = basket, MediaType = "image/png", Step = 0, Timestamp = at.AddSeconds(1) }.ToJson(),
            new TestRunRecord
            {
                Event = "step", TestId = "t0", Keyword = "When", Text = "the card is charged", Status = "failed", Error = "declined",
                SourceFile = "Checkout.feature", SourceLine = 12, Timestamp = at.AddSeconds(2),
            }.ToJson(),
            new TestRunRecord { Event = "attachment", TestId = "t0", Name = "declined.png", Path = shot, MediaType = "image/png", Step = 1, Timestamp = at.AddSeconds(3) }.ToJson(),
            new TestRunRecord { Event = "attachment", TestId = "t0", Name = "trace", Path = trace, MediaType = "application/zip", Timestamp = at.AddSeconds(3) }.ToJson(),
            new TestRunRecord { Event = "end", TestId = "t0", Status = "failed", Error = "declined", Timestamp = at.AddSeconds(4) }.ToJson(),
        ]);
        var options = IngestPipeline.DefaultOptions();
        options.ReportsFolderPath = Path.Combine(_dir, "Reports");
        options.HistoryFilePath = Path.Combine(_dir, "history.ndjson");
        options.WriteRunSummaryToConsole = false;
        IngestPipeline.Run(new IngestRequest { TestsFile = tests, Options = options });

        var output = new StringWriter();
        Assert.Equal(0, QueryCommand.Run(["failures", options.ReportsFolderPath], output, new StringWriter()));

        var lines = output.ToString().Split('\n').Select(l => l.TrimEnd('\r')).ToArray();
        var failing = Array.FindIndex(lines, l => l.StartsWith("  ✗ s0/1 ", StringComparison.Ordinal));
        Assert.True(failing >= 0, output.ToString());
        // Under the step's error and its location, then its attachment, where `steps` prints it.
        Assert.Equal("    declined", lines[failing + 1]);
        Assert.Equal("    at Checkout.feature:12", lines[failing + 2]);
        var copied = Path.GetFullPath(Path.Combine(options.ReportsFolderPath, "attachments", "declined.png"));
        Assert.Equal($"    attachment: declined.png → {copied}", lines[failing + 3]);
        // Only the failing step's: the passing step's screenshot is in `steps`, not here.
        Assert.DoesNotContain(lines, l => l.Contains("basket.png", StringComparison.Ordinal));
        // The scenario's own attachment is printed as it was, and once.
        Assert.Single(lines, l => Regex.IsMatch(l, @"^  attachment: trace → .+trace\.zip$"));
        Assert.DoesNotContain(lines, l => l.StartsWith("  attachment: declined.png", StringComparison.Ordinal));

        // The object form nests them the same way.
        var json = new StringWriter();
        Assert.Equal(0, QueryCommand.Run(["failures", options.ReportsFolderPath, "--json"], json, new StringWriter()));
        var failure = System.Text.Json.JsonDocument.Parse(json.ToString()).RootElement.GetProperty("items").EnumerateArray().Single();
        var step = failure.GetProperty("failingSteps").EnumerateArray().Single();
        Assert.Equal(["declined.png"], step.GetProperty("attachments").EnumerateArray().Select(a => a.GetProperty("name").GetString()));
        Assert.Equal([copied], step.GetProperty("attachments").EnumerateArray().Select(a => a.GetProperty("path").GetString()));
        Assert.Equal(["trace"], failure.GetProperty("attachments").EnumerateArray().Select(a => a.GetProperty("name").GetString()));
    }

    // ---- found by the #175 audit: what `failures` prints under the failing step ----------------------------------

    /// <summary>
    /// Ingests one failing test whose failing step holds <paramref name="onTheStep"/>, and a second failing test after it,
    /// and returns the reports folder.
    /// </summary>
    private string FailingStepWith(params TestRunRecord[] onTheStep)
    {
        var at = DateTimeOffset.Parse("2026-10-15T10:00:00Z");
        var tests = Path.Combine(_dir, "tests.ndjson");
        File.WriteAllLines(tests,
        [
            new TestRunRecord { Event = "start", TestId = "t0", TestName = "Pay", Feature = "Checkout", Timestamp = at }.ToJson(),
            new TestRunRecord { Event = "step", TestId = "t0", Keyword = "When", Text = "the card is charged", Status = "failed", Error = "declined", Timestamp = at.AddSeconds(1) }.ToJson(),
            .. onTheStep.Select(record => (record with { TestId = "t0", Step = 0, Timestamp = at.AddSeconds(2) }).ToJson()),
            new TestRunRecord { Event = "end", TestId = "t0", Status = "failed", Error = "declined", Timestamp = at.AddSeconds(3) }.ToJson(),
            new TestRunRecord { Event = "start", TestId = "t1", TestName = "Refund", Feature = "Checkout", Timestamp = at.AddSeconds(4) }.ToJson(),
            new TestRunRecord { Event = "end", TestId = "t1", Status = "failed", Error = "refund refused", Timestamp = at.AddSeconds(5) }.ToJson(),
        ]);
        var options = IngestPipeline.DefaultOptions();
        options.ReportsFolderPath = Path.Combine(_dir, "Reports");
        options.HistoryFilePath = Path.Combine(_dir, "history.ndjson");
        options.WriteRunSummaryToConsole = false;
        IngestPipeline.Run(new IngestRequest { TestsFile = tests, Options = options });
        return options.ReportsFolderPath;
    }

    private static (string[] Lines, System.Text.Json.JsonElement[] Items) Failures(string reports)
    {
        var output = new StringWriter();
        Assert.Equal(0, QueryCommand.Run(["failures", reports], output, new StringWriter()));
        var json = new StringWriter();
        Assert.Equal(0, QueryCommand.Run(["failures", reports, "--json"], json, new StringWriter()));
        return (output.ToString().Split('\n').Select(l => l.TrimEnd('\r')).ToArray(),
            System.Text.Json.JsonDocument.Parse(json.ToString()).RootElement.GetProperty("items").EnumerateArray().ToArray());
    }

    private static string[] FailingStepPaths(System.Text.Json.JsonElement[] items) =>
        items.Single(i => i.GetProperty("failingSteps").GetArrayLength() > 0).GetProperty("failingSteps")[0]
            .GetProperty("attachments").EnumerateArray().Select(a => a.GetProperty("path").GetString()!).ToArray();

    [Fact]
    public void Query_failures_prints_a_link_on_the_failing_step_as_it_is()
    {
        // It was joined to the reports folder like a file's path: an address no one can open.
        const string link = "http://localhost:3900/explore?traceId=abc";
        var reports = FailingStepWith(new TestRunRecord { Event = "attachment", TestId = "t0", Name = "Grafana trace", Path = link, MediaType = "text/uri-list" });

        var (lines, items) = Failures(reports);

        Assert.Contains($"    attachment: Grafana trace → {link}", lines);
        Assert.Equal([link], FailingStepPaths(items));
    }

    [Fact]
    public void Query_failures_prints_the_first_six_of_a_failing_steps_attachments_and_every_failure()
    {
        // A step that attached many files was printed whole, and one row past the byte budget hid every other failure: the
        // text said nothing fits, the object form had no items.
        var shots = Enumerable.Range(0, 60).Select(i =>
        {
            var file = Path.Combine(_dir, $"screenshot-taken-while-the-card-was-being-declined-{i:D2}.png");
            File.WriteAllBytes(file, [137, 80, 78, 71, (byte)i]);
            return new TestRunRecord { Event = "attachment", TestId = "t0", Name = Path.GetFileName(file), Path = file, MediaType = "image/png" };
        }).ToArray();
        var reports = FailingStepWith(shots);

        var (lines, items) = Failures(reports);

        Assert.Equal(6, lines.Count(l => l.StartsWith("    attachment: ", StringComparison.Ordinal)));
        Assert.Contains("    … 54 more attachment(s): steps s0/0", lines);
        Assert.Contains(lines, l => l.Contains("refund refused", StringComparison.Ordinal));
        Assert.Equal(2, items.Length);
        Assert.Equal(6, FailingStepPaths(items).Length);

        // `steps`, which the line names, prints past the sixth.
        var steps = new StringWriter();
        Assert.Equal(0, QueryCommand.Run(["steps", reports, "s0/0"], steps, new StringWriter()));
        Assert.Contains("declined-06.png", steps.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public void A_path_no_file_system_takes_is_printed_as_written()
    {
        // A reporter's path the report keeps as written (a file it could not copy) stopped `failures` and `steps` with an
        // exception, and every failure with them.
        var odd = Path.Combine(_dir, "nowhere", "a\0b.png");
        var reports = FailingStepWith(new TestRunRecord { Event = "attachment", TestId = "t0", Name = "odd.png", Path = odd, MediaType = "image/png" });

        var (lines, items) = Failures(reports);

        Assert.Contains($"    attachment: odd.png → {odd}", lines);
        Assert.Equal([odd], FailingStepPaths(items));
        Assert.Contains(lines, l => l.Contains("refund refused", StringComparison.Ordinal));
        Assert.Equal(0, QueryCommand.Run(["steps", reports, "s0"], new StringWriter(), new StringWriter()));
    }
}
