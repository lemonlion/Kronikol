using System.Text.Json.Nodes;
using Kronikol.Query;
using Kronikol.Reports;
using Kronikol.Reports.Merge;
using Kronikol.Tracking;
using static Kronikol.DefaultDiagramsFetcher;

namespace Kronikol.Tests.Reports;

/// <summary>
/// #148 (<c>plans/MISSING_ATTACHMENTS_PLAN.md</c> R1 and R2): an attachment whose file the report does not hold kept the
/// path it was given and nothing said so, so a reviewer opened a report of broken images with no word anywhere about
/// why. Every attachment the report does not hold is now said in one <see cref="DiagnosticKind.AttachmentFailure"/> entry
/// per run, which leads with the count because <c>kronikol query</c>'s <c>!</c> line shows only the first message of a
/// kind, cut to 160 characters; and the run's copier links what it was given where it can find it.
/// </summary>
[Collection("DiagramsFetcher")]
public class MissingAttachmentTests : IDisposable
{
    private static readonly DateTime Start = new(2026, 1, 1, 10, 0, 0, DateTimeKind.Utc);

    private readonly string _dir = Path.Combine(Path.GetTempPath(), "kronikol-missing-attachments-" + Guid.NewGuid().ToString("N"));

    public MissingAttachmentTests() => Directory.CreateDirectory(_dir);

    public void Dispose()
    {
        DefaultDiagramsFetcher.Reset();
        try { Directory.Delete(_dir, true); } catch { /* best effort */ }
    }

    private string PathOf(params string[] parts) => Path.Combine([_dir, .. parts]);

    private string Reports => PathOf("Reports");

    /// <summary>A file with <paramref name="content"/> under the fact's own folder, its parent made.</summary>
    private string Written(string relative, string content = "bytes")
    {
        var path = PathOf(relative.Split('/'));
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, content);
        return path;
    }

    /// <summary>One failed scenario with one step per array, each step holding that array's attachments.</summary>
    private static Feature[] OneScenario(FileAttachment[]? scenarioAttachments, params FileAttachment[][] steps) =>
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
                    Attachments = scenarioAttachments,
                    Steps = steps.Select((attachments, i) => new ScenarioStep
                    {
                        Keyword = "When", Text = $"step {i + 1}", Status = ExecutionResult.Failed, Attachments = attachments,
                    }).ToArray(),
                },
            ],
        },
    ];

    private static IEnumerable<FileAttachment> AllAttachments(Feature[] features) =>
        features.SelectMany(f => f.Scenarios).SelectMany(s => (s.Attachments ?? []).Concat((s.Steps ?? []).SelectMany(step => step.Attachments ?? [])));

    /// <summary>The copier alone, inside a diagnostics scope as a run calls it: what it recorded.</summary>
    private IReadOnlyList<DiagnosticEntry> Copy(Feature[] features)
    {
        var collector = new ReportDiagnosticsCollector();
        using (ReportDiagnosticsScope.Begin(collector))
            ReportGenerator.CopyAttachmentsToReportsFolder(features, Reports);
        return collector.Entries;
    }

    private static string TheEntry(IEnumerable<DiagnosticEntry> entries) =>
        Assert.Single(entries, e => e.Kind == DiagnosticKind.AttachmentFailure).Message;

    private ReportConfigurationOptions Options(Action<ReportConfigurationOptions>? configure = null)
    {
        var options = new ReportConfigurationOptions
        {
            ReportsFolderPath = Reports,
            PlantUmlRendering = PlantUmlRendering.BrowserJs,
            GenerateComponentDiagram = false,
            WriteRunSummaryToConsole = false,
            HistoryFilePath = PathOf("history.ndjson"),
            KeepRuns = 3,
        };
        configure?.Invoke(options);
        return options;
    }

    /// <summary>A whole run inside a host's diagnostics scope, as <c>kronikol ingest</c> runs one: what it recorded.</summary>
    private IReadOnlyList<DiagnosticEntry> Run(Feature[] features, Action<ReportConfigurationOptions>? configure = null)
    {
        var collector = new ReportDiagnosticsCollector();
        DefaultDiagramsFetcher.Reset();
        using (ReportDiagnosticsScope.Begin(collector))
            ReportGenerator.CreateStandardReportsWithDiagramsInEnvironment(features, Start, Start.AddMinutes(1), Options(configure), RunEnvironment.Unrecorded, _ => null);
        DefaultDiagramsFetcher.Reset();
        return collector.Entries;
    }

    private static string Query(params string[] args)
    {
        var output = new StringWriter();
        var error = new StringWriter();
        Assert.True(QueryCommand.Run(args, output, error) == 0, error.ToString());
        return output.ToString();
    }

    // ─── The entry ───────────────────────────────────────────────

    [Fact]
    public void A_missing_file_is_said_in_one_entry_that_leads_with_the_count()
    {
        var gone = PathOf("gone", "missing.png");
        var features = OneScenario(null, [new FileAttachment("missing.png", gone)]);

        var message = TheEntry(Copy(features));

        Assert.StartsWith("1 attachment(s) are not in the report: 1 not found when the report was drawn: ", message, StringComparison.Ordinal);
        Assert.Contains(gone, message, StringComparison.Ordinal);
        // Still kept as given in this release: the page changes only when the state is kept on the attachment.
        Assert.Equal(gone, AllAttachments(features).Single().RelativePath);
    }

    [Fact]
    public void Three_attachments_of_two_missing_files_count_the_files_and_the_present_one_is_copied()
    {
        var a = PathOf("gone", "a.png");
        var b = PathOf("gone", "b.png");
        var present = Written("sources/present.png");
        var features = OneScenario(null,
            [new FileAttachment("a.png", a), new FileAttachment("b.png", b)],
            [new FileAttachment("a.png", a), new FileAttachment("present.png", present)]);

        var message = TheEntry(Copy(features));

        Assert.StartsWith("3 attachment(s) are not in the report: 3 not found when the report was drawn (2 files): ", message, StringComparison.Ordinal);
        // Each file once, however many attachments name it.
        Assert.Equal(1, Occurrences(message, a));
        Assert.Equal(1, Occurrences(message, b));
        Assert.DoesNotContain(present, message, StringComparison.Ordinal);
        Assert.Equal("attachments/present.png", AllAttachments(features).Last().RelativePath);
        Assert.True(File.Exists(Path.Combine(Reports, "attachments", "present.png")));
    }

    [Fact]
    public void Seven_missing_files_name_the_first_five_in_report_order_and_say_how_many_more()
    {
        var paths = Enumerable.Range(1, 7).Select(i => PathOf("gone", $"shot-{i}.png")).ToArray();
        var features = OneScenario(null, paths.Select(p => new FileAttachment(Path.GetFileName(p), p)).ToArray());

        var message = TheEntry(Copy(features));

        Assert.StartsWith("7 attachment(s) are not in the report: 7 not found when the report was drawn: ", message, StringComparison.Ordinal);
        Assert.All(paths.Take(5), p => Assert.Contains(p, message, StringComparison.Ordinal));
        Assert.All(paths.Skip(5), p => Assert.DoesNotContain(p, message, StringComparison.Ordinal));
        Assert.Contains(paths[4] + ", and 2 more.", message, StringComparison.Ordinal);
    }

    [Fact]
    public void A_failed_copy_and_three_missing_files_are_one_entry_and_one_bang_line_that_leads_with_the_count()
    {
        // A copy that cannot be made, on every platform: a folder holds the name the copy would take.
        var held = Written("sources/held.png");
        Directory.CreateDirectory(Path.Combine(Reports, "attachments", "held.png"));
        var missing = Enumerable.Range(1, 3).Select(i => PathOf("gone", $"m{i}.png")).ToArray();
        var features = OneScenario(null,
            [new FileAttachment("held.png", held)],
            missing.Select(p => new FileAttachment(Path.GetFileName(p), p)).ToArray());

        var message = TheEntry(Run(features));

        Assert.StartsWith("4 attachment(s) are not in the report: 3 not found when the report was drawn, 1 could not be copied (", message, StringComparison.Ordinal);

        // query's ! line shows the first message of a kind, cut to 160 characters, with (×N) when there are more:
        // one entry means the count is the first thing read, and nothing is hidden behind a suffix.
        foreach (var verb in new[] { "summary", "failures" })
        {
            var line = Assert.Single(Query(verb, Reports).Split('\n'), l => l.StartsWith("! ", StringComparison.Ordinal) && l.Contains("attachment(s) are not in the report", StringComparison.Ordinal)).TrimEnd('\r');
            Assert.StartsWith("! 4 attachment(s) are not in the report: ", line, StringComparison.Ordinal);
            Assert.DoesNotContain("(×", line, StringComparison.Ordinal);
            // "! ", the message cut to 160 characters, and the ellipsis that says it was cut.
            Assert.True(line.Length <= "! ".Length + 160 + "…".Length, line);
        }
    }

    [Fact]
    public void An_attachments_path_with_no_such_file_in_the_reports_folder_is_said()
    {
        // Taken for this run's own copy, and kept, though no such file was ever there (F2).
        var features = OneScenario(null, [new FileAttachment("x.png", "attachments/x.png")]);

        var message = TheEntry(Copy(features));

        Assert.StartsWith("1 attachment(s) are not in the report: 1 not found when the report was drawn: ", message, StringComparison.Ordinal);
        Assert.Contains(Path.Combine(Reports, "attachments", "x.png"), message, StringComparison.Ordinal);
    }

    [Fact]
    public void An_in_process_run_prints_the_entry_once_as_a_warning()
    {
        var features = OneScenario(null, [new FileAttachment("missing.png", PathOf("gone", "missing.png"))]);

        // No host scope: the run scopes its own collector, and nothing else prints what it recorded. The line names no
        // directory, so it is read from this thread's own output.
        string console;
        using (var scoped = new ThreadScopedConsole())
        {
            DefaultDiagramsFetcher.Reset();
            ReportGenerator.CreateStandardReportsWithDiagramsInEnvironment(features, Start, Start.AddMinutes(1), Options(), RunEnvironment.Unrecorded, _ => null);
            DefaultDiagramsFetcher.Reset();
            console = scoped.Text;
        }

        var line = Assert.Single(console.Split('\n'), l => l.Contains("attachment(s) are not in the report", StringComparison.Ordinal));
        Assert.StartsWith("⚠ WARNING: 1 attachment(s) are not in the report: 1 not found when the report was drawn: ", line, StringComparison.Ordinal);
        // And the run's data file carries the same entry.
        var data = JsonNode.Parse(ReportFile.ReadAny(Path.Combine(Reports, "TestRunReport.json")))!;
        Assert.Single(data["diagnostics"]!.AsArray(), d => d!["message"]!.GetValue<string>().StartsWith("1 attachment(s) are not in the report", StringComparison.Ordinal));
    }

    [Fact]
    public void A_host_that_scoped_the_diagnostics_prints_them_itself_so_the_run_prints_no_warning()
    {
        var features = OneScenario(null, [new FileAttachment("missing.png", PathOf("gone", "missing.png"))]);

        string console;
        IReadOnlyList<DiagnosticEntry> entries;
        using (var scoped = new ThreadScopedConsole())
        {
            entries = Run(features);
            console = scoped.Text;
        }

        Assert.StartsWith("1 attachment(s) are not in the report", TheEntry(entries), StringComparison.Ordinal);
        Assert.DoesNotContain("attachment(s) are not in the report", console, StringComparison.Ordinal);
    }

    // ─── Changed or gone after it was attached (F23) ──────────────────

    [Fact]
    public void A_file_deleted_after_it_was_attached_is_said_to_have_been_there_when_it_was_attached()
    {
        var testId = "missing-attachment-" + Guid.NewGuid().ToString("N");
        var file = Written("sources/teardown.png");
        try
        {
            StepCollector.AddAttachment(testId, file, null);
            File.Delete(file);
            var features = OneScenario(StepCollector.GetScenarioAttachments(testId));

            var message = TheEntry(Copy(features));

            Assert.StartsWith("1 attachment(s) are not in the report: 1 was there when it was attached and gone when the report was drawn: ", message, StringComparison.Ordinal);
            Assert.Contains(file, message, StringComparison.Ordinal);
        }
        finally
        {
            StepCollector.ClearSteps(testId);
        }
    }

    [Fact]
    public void A_path_rewritten_between_two_attaches_is_said_to_have_changed_and_the_report_holds_what_it_found()
    {
        var testId = "missing-attachment-" + Guid.NewGuid().ToString("N");
        var file = Written("sources/screenshot.png", "one");
        try
        {
            StepCollector.AddAttachment(testId, file, "first");
            File.WriteAllText(file, "second, longer");
            StepCollector.AddAttachment(testId, file, "second");
            var features = OneScenario(StepCollector.GetScenarioAttachments(testId));

            var message = TheEntry(Copy(features));

            Assert.StartsWith("1 attachment(s) changed after it was attached, and the report holds the version found when it was drawn: ", message, StringComparison.Ordinal);
            Assert.Contains(file, message, StringComparison.Ordinal);
            Assert.All(AllAttachments(features), a => Assert.Equal("attachments/screenshot.png", a.RelativePath));
            Assert.Equal("second, longer", File.ReadAllText(Path.Combine(Reports, "attachments", "screenshot.png")));
        }
        finally
        {
            StepCollector.ClearSteps(testId);
        }
    }

    // ─── What stopped a report, or said the wrong thing (F9, F10) ───────────

    [Fact]
    public void A_file_named_attachments_in_the_reports_folder_does_not_stop_the_report()
    {
        Directory.CreateDirectory(Reports);
        File.WriteAllText(Path.Combine(Reports, "attachments"), "not a folder");
        var source = Written("sources/shot.png");
        var features = OneScenario(null, [new FileAttachment("shot.png", source)]);

        IReadOnlyList<DiagnosticEntry> entries = [];
        var exception = Record.Exception(() => entries = Run(features));

        Assert.Null(exception);
        Assert.True(File.Exists(Path.Combine(Reports, "TestRunReport.html")));
        Assert.StartsWith("1 attachment(s) are not in the report: 1 could not be copied (", TheEntry(entries), StringComparison.Ordinal);
    }

    [Fact]
    public void A_file_already_in_the_reports_attachments_folder_is_linked_where_it_is()
    {
        // A test that wrote its screenshot straight into the report's folder: copied onto itself, the copy failed with
        // "being used by another process", the page linked the absolute path, and Run.json left the file out.
        var inPlace = Written("Reports/attachments/in-place.png");
        var features = OneScenario(null, [new FileAttachment("in-place.png", inPlace)]);

        var entries = Run(features);

        Assert.DoesNotContain(entries, e => e.Kind == DiagnosticKind.AttachmentFailure);
        Assert.Equal("attachments/in-place.png", AllAttachments(features).Single().RelativePath);
        Assert.True(File.Exists(inPlace));
        Assert.Contains("attachments/in-place.png", RunManifest.TryRead(Path.Combine(Reports, RunManifest.FileName))!.Attachments);
    }

    [Fact]
    public void A_file_already_in_the_folder_keeps_its_name_when_a_file_of_the_same_name_is_copied_in()
    {
        // Every path is read before anything is copied: copied in first, the other shot.png was written over this one.
        var inPlace = Written("Reports/attachments/shot.png", "in place");
        var elsewhere = Written("sources/shot.png", "from elsewhere");
        var features = OneScenario(null, [new FileAttachment("elsewhere", elsewhere), new FileAttachment("in place", inPlace)]);

        Assert.DoesNotContain(Copy(features), e => e.Kind == DiagnosticKind.AttachmentFailure);

        var links = AllAttachments(features).Select(a => a.RelativePath).ToArray();
        Assert.Equal("attachments/shot.png", links[1]);
        Assert.Equal("in place", File.ReadAllText(inPlace));
        Assert.NotEqual(links[0], links[1]);
        Assert.Equal("from elsewhere", File.ReadAllText(Path.Combine(Reports, links[0])));
    }

    // ─── How it is linked (F11, F21) ──────────────────────────────

    [Fact]
    public void An_image_attached_under_a_display_name_draws_as_an_image()
    {
        // Track.Attachment(path, "Page Screenshot"), the wiki's own example, and every LightBDD image: the name carries
        // no extension and no media type was given, so the file's own extension decides.
        Assert.True(new FileAttachment("Page Screenshot", PathOf("shots", "page.png")).IsInlineImage);
        Assert.True(new FileAttachment("Page Screenshot", "attachments/page.PNG").IsInlineImage);
        // A link's path says nothing about what it serves, and a media type still wins.
        Assert.False(new FileAttachment("Trace", "https://example.com/trace.png").IsInlineImage);
        Assert.False(new FileAttachment("Page Screenshot", "attachments/page.png", "application/octet-stream").IsInlineImage);
        Assert.False(new FileAttachment("Log", "attachments/run.log").IsInlineImage);
    }

    [Fact]
    public void A_file_name_with_a_hash_a_question_mark_or_a_percent_sign_is_linked_percent_encoded()
    {
        // Copied under the same name, the link was HTML-encoded only, so attachments/shot#1.png requested attachments/shot.
        Feature[] features =
        [
            new Feature
            {
                DisplayName = "Checkout",
                Scenarios =
                [
                    new Scenario
                    {
                        Id = "own", DisplayName = "Own attachments", Result = ExecutionResult.Passed,
                        Attachments = [new FileAttachment("shot#1.png", "attachments/shot#1.png", "image/png"), new FileAttachment("Trace", "https://example.com/trace?id=1#top")],
                        Steps =
                        [
                            new ScenarioStep
                            {
                                Keyword = "When", Text = "it pays", Status = ExecutionResult.Passed,
                                Attachments = [new FileAttachment("50%.png", "attachments/50%.png"), new FileAttachment("what?.txt", "attachments/what?.txt"), new FileAttachment("a%#.log", "attachments/a%#.log")],
                            },
                        ],
                    },
                    new Scenario
                    {
                        Id = "row-1", DisplayName = "Row 1", Result = ExecutionResult.Passed, OutlineId = "outline", ExampleValues = new() { ["n"] = "1" },
                        Attachments = [new FileAttachment("row#1.png", "attachments/row#1.png")],
                    },
                    new Scenario
                    {
                        Id = "row-2", DisplayName = "Row 2", Result = ExecutionResult.Passed, OutlineId = "outline", ExampleValues = new() { ["n"] = "2" },
                        Steps = [new ScenarioStep { Keyword = "Then", Text = "it is paid", Status = ExecutionResult.Passed }],
                    },
                ],
            },
        ];

        var html = File.ReadAllText(ReportGenerator.GenerateHtmlReport(
            Array.Empty<DiagramAsCode>(), features, Start, Start.AddMinutes(1), null, PathOf("Encoded.html"), "Test", true,
            diagramFormat: DiagramFormat.PlantUml, plantUmlRendering: PlantUmlRendering.BrowserJs));

        // The scenario's own, a step's, and a parameterized row's panel: % first, then # and ?, so nothing is encoded twice.
        Assert.Contains("href=\"attachments/shot%231.png\" onclick=\"openLightbox(event, this)\" title=\"Show shot#1.png, an image this scenario attached, at full size\"><img class=\"attachment-image\" src=\"attachments/shot%231.png\"", html, StringComparison.Ordinal);
        Assert.Contains("src=\"attachments/50%25.png\"", html, StringComparison.Ordinal);
        Assert.Contains("<a class=\"step-attachment\" href=\"attachments/what%3F.txt\" title=\"Open what?.txt, a file this step attached\">what?.txt</a>", html, StringComparison.Ordinal);
        Assert.Contains("<a class=\"step-attachment\" href=\"attachments/a%25%23.log\" title=\"Open a%#.log, a file this step attached\">a%#.log</a>", html, StringComparison.Ordinal);
        Assert.Contains("src=\"attachments/row%231.png\"", html, StringComparison.Ordinal);
        // A link elsewhere is a URL already: its query and fragment are its own.
        Assert.Contains("<a class=\"step-attachment\" href=\"https://example.com/trace?id=1#top\" title=\"Open Trace, a link this scenario attached\">Trace</a>", html, StringComparison.Ordinal);
        Assert.DoesNotContain("attachments/shot#1.png\"", html, StringComparison.Ordinal);
    }

    // ─── The Azure DevOps upload (R4, F7) ──────────────────────────

    [Fact]
    public void An_azure_devops_upload_carries_the_runs_attachments_under_their_folder()
    {
        // An upload names every file, and it named the top-level outputs and the kept runs but not attachments/, so every
        // copied screenshot was a dead link in the artifact. GitHub Actions uploads the whole folder.
        var shot = Written("sources/shot.png");
        var features = OneScenario(null, [new FileAttachment("shot.png", shot)]);

        string console;
        using (var scoped = new ThreadScopedConsole())
        {
            DefaultDiagramsFetcher.Reset();
            ReportGenerator.CreateStandardReportsWithDiagramsInEnvironment(features, Start, Start.AddMinutes(1),
                Options(o => o.PublishCiArtifacts = true), RunEnvironment.Unrecorded, name => name == "TF_BUILD" ? "True" : null);
            DefaultDiagramsFetcher.Reset();
            console = scoped.Text;
        }

        Assert.Contains($"##vso[artifact.upload containerfolder=TestReports/attachments;artifactname=TestReports]{Path.Combine(Reports, "attachments", "shot.png")}",
            console, StringComparison.Ordinal);
    }

    // ─── Guards ────────────────────────────────────────────

    [Fact]
    public void A_relative_attachment_beside_the_binaries_records_no_attachment_failure()
    {
        // The default in every runner measured (plans/MISSING_ATTACHMENTS_PLAN.md section 2.4): the working directory is
        // the binary folder, and a file a test writes by a relative name is found where it was attached.
        var name = "missing-attachment-guard-" + Guid.NewGuid().ToString("N") + ".json";
        var file = Path.Combine(AppContext.BaseDirectory, name);
        var testId = "missing-attachment-" + Guid.NewGuid().ToString("N");
        File.WriteAllText(file, "{}");
        try
        {
            StepCollector.AddAttachment(testId, name, null);
            var features = OneScenario(StepCollector.GetScenarioAttachments(testId));

            Assert.DoesNotContain(Copy(features), e => e.Kind == DiagnosticKind.AttachmentFailure);
            Assert.Equal("attachments/" + name, AllAttachments(features).Single().RelativePath);
        }
        finally
        {
            StepCollector.ClearSteps(testId);
            File.Delete(file);
        }
    }

    // ─── The run's copier and the merge's carrier decide alike (section 4.1) ────────

    [Fact]
    public void The_run_and_the_merge_give_each_attachment_the_same_state()
    {
        // The same eight inputs through both writers, each against its own folder. Two implementations of one rule get a
        // parity fact (the 3.30.1 lesson); the naming rules for a clash stay each writer's own.
        var merged = PathOf("merged");
        var run = OneScenario(null, Inputs(Reports));
        var merge = new MergeableReport { Features = OneScenario(null, Inputs(merged)), StartTime = Start, EndTime = Start.AddMinutes(1) };

        ReportGenerator.CopyAttachmentsToReportsFolder(run, Reports);
        DefaultDiagramsFetcher.Reset();
        MergeableReportRenderer.Render(merge, Path.Combine(merged, "Combined.html"));

        AttachmentProblem?[] expected = [null, AttachmentProblem.NotFound, AttachmentProblem.NotCopied, AttachmentProblem.NotUsable, null, null, AttachmentProblem.NotFound, AttachmentProblem.NotFound];
        Assert.Equal(expected, AllAttachments(run).Select(a => a.Problem).ToArray());
        Assert.Equal(expected, AllAttachments(merge.Features).Select(a => a.Problem).ToArray());
        // Where each writer linked the ones it holds.
        Assert.Equal(["attachments/present.png", "attachments/in-place.png"],
            AllAttachments(run).Where(a => a.Problem is null && !a.IsUrl).Select(a => a.RelativePath));
        Assert.Equal(["attachments/present.png", "attachments/in-place.png"],
            AllAttachments(merge.Features).Where(a => a.Problem is null && !a.IsUrl).Select(a => a.RelativePath));

        FileAttachment[] Inputs(string folder)
        {
            var name = Path.GetFileName(folder);
            Directory.CreateDirectory(Path.Combine(folder, "attachments", "held.png"));
            Directory.CreateDirectory(PathOf("a-folder-" + name));
            return
            [
                new FileAttachment("present.png", Written($"sources-{name}/present.png")),
                new FileAttachment("missing.png", PathOf("gone", "missing.png")),
                new FileAttachment("held.png", Written($"sources-{name}/held.png")),
                new FileAttachment("nul.png", "shot\0.png"),
                new FileAttachment("Trace", "https://example.com/trace"),
                new FileAttachment("in-place.png", Written($"{name}/attachments/in-place.png")),
                new FileAttachment("x.png", "attachments/x.png"),
                new FileAttachment("a folder", PathOf("a-folder-" + name)),
            ];
        }
    }

    private static int Occurrences(string text, string value)
    {
        var count = 0;
        for (var at = text.IndexOf(value, StringComparison.Ordinal); at >= 0; at = text.IndexOf(value, at + value.Length, StringComparison.Ordinal))
            count++;
        return count;
    }
}
