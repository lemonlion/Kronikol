using Kronikol.Reports;
using static Kronikol.DefaultDiagramsFetcher;

namespace Kronikol.Tests.Reports;

public class AttachmentCopyTests : IDisposable
{
    private readonly string _tempDir;

    public AttachmentCopyTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), $"kronikol-att-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempDir);
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempDir))
            Directory.Delete(_tempDir, recursive: true);
    }

    [Fact]
    public void Copies_attachment_file_to_reports_attachments_folder()
    {
        var sourceFile = Path.Combine(_tempDir, "screenshot.png");
        File.WriteAllText(sourceFile, "fake image data");
        var reportsDir = Path.Combine(_tempDir, "Reports");
        Directory.CreateDirectory(reportsDir);

        var features = CreateFeatures(new FileAttachment("screenshot.png", sourceFile));

        ReportGenerator.CopyAttachmentsToReportsFolder(features, reportsDir);

        Assert.True(File.Exists(Path.Combine(reportsDir, "attachments", "screenshot.png")));
    }

    [Fact]
    public void Rewrites_RelativePath_to_attachments_subfolder()
    {
        var sourceFile = Path.Combine(_tempDir, "screenshot.png");
        File.WriteAllText(sourceFile, "fake image data");
        var reportsDir = Path.Combine(_tempDir, "Reports");
        Directory.CreateDirectory(reportsDir);

        var features = CreateFeatures(new FileAttachment("screenshot.png", sourceFile));

        ReportGenerator.CopyAttachmentsToReportsFolder(features, reportsDir);

        Assert.Equal("attachments/screenshot.png", features[0].Scenarios[0].Steps[0].Attachments![0].RelativePath);
    }

    [Fact]
    public void Missing_source_file_is_kept_without_error_and_said_in_one_entry()
    {
        // Until #148 the file was kept and nothing was recorded, so the report linked a path that existed nowhere and no
        // reader said why (plans/MISSING_ATTACHMENTS_PLAN.md F1).
        var reportsDir = Path.Combine(_tempDir, "Reports");
        Directory.CreateDirectory(reportsDir);
        var missing = Path.Combine(_tempDir, "nonexistent", "missing.png");

        var features = CreateFeatures(new FileAttachment("missing.png", missing));

        var collector = new ReportDiagnosticsCollector();
        using (ReportDiagnosticsScope.Begin(collector))
            ReportGenerator.CopyAttachmentsToReportsFolder(features, reportsDir);

        // RelativePath unchanged because file doesn't exist
        Assert.Equal(missing, features[0].Scenarios[0].Steps[0].Attachments![0].RelativePath);
        Assert.False(Directory.Exists(Path.Combine(reportsDir, "attachments")));
        var entry = Assert.Single(collector.Entries);
        Assert.Equal(DiagnosticKind.AttachmentFailure, entry.Kind);
        Assert.Equal($"1 attachment(s) are not in the report: 1 not found when the report was drawn: {missing}.", entry.Message);
    }

    [Fact]
    public void Duplicate_filenames_are_deduplicated()
    {
        var sourceA = Path.Combine(_tempDir, "a", "report.txt");
        var sourceB = Path.Combine(_tempDir, "b", "report.txt");
        Directory.CreateDirectory(Path.Combine(_tempDir, "a"));
        Directory.CreateDirectory(Path.Combine(_tempDir, "b"));
        File.WriteAllText(sourceA, "contents A");
        File.WriteAllText(sourceB, "contents B");
        var reportsDir = Path.Combine(_tempDir, "Reports");
        Directory.CreateDirectory(reportsDir);

        var features = new[]
        {
            new Feature
            {
                DisplayName = "F1",
                Scenarios =
                [
                    new Scenario
                    {
                        Id = "s1", DisplayName = "S1", Result = ExecutionResult.Passed,
                        Steps =
                        [
                            new ScenarioStep
                            {
                                Keyword = "When", Text = "step A", Status = ExecutionResult.Passed,
                                Attachments = [new FileAttachment("report.txt", sourceA)]
                            },
                            new ScenarioStep
                            {
                                Keyword = "Then", Text = "step B", Status = ExecutionResult.Passed,
                                Attachments = [new FileAttachment("report.txt", sourceB)]
                            }
                        ]
                    }
                ]
            }
        };

        ReportGenerator.CopyAttachmentsToReportsFolder(features, reportsDir);

        var attachmentsDir = Path.Combine(reportsDir, "attachments");
        var files = Directory.GetFiles(attachmentsDir).Select(Path.GetFileName).OrderBy(x => x).ToArray();
        Assert.Equal(2, files.Length);
        Assert.Contains("report.txt", files);
        // Second should be deduplicated with a suffix
        Assert.Contains(files, f => f != "report.txt" && f!.StartsWith("report") && f.EndsWith(".txt"));
    }

    [Fact]
    public void Copies_attachments_from_sub_steps()
    {
        var sourceFile = Path.Combine(_tempDir, "nested.log");
        File.WriteAllText(sourceFile, "nested log data");
        var reportsDir = Path.Combine(_tempDir, "Reports");
        Directory.CreateDirectory(reportsDir);

        var features = new[]
        {
            new Feature
            {
                DisplayName = "F1",
                Scenarios =
                [
                    new Scenario
                    {
                        Id = "s1", DisplayName = "S1", Result = ExecutionResult.Passed,
                        Steps =
                        [
                            new ScenarioStep
                            {
                                Keyword = "Given", Text = "outer", Status = ExecutionResult.Passed,
                                SubSteps =
                                [
                                    new ScenarioStep
                                    {
                                        Text = "inner", Status = ExecutionResult.Passed,
                                        Attachments = [new FileAttachment("nested.log", sourceFile)]
                                    }
                                ]
                            }
                        ]
                    }
                ]
            }
        };

        ReportGenerator.CopyAttachmentsToReportsFolder(features, reportsDir);

        Assert.True(File.Exists(Path.Combine(reportsDir, "attachments", "nested.log")));
        Assert.Equal("attachments/nested.log", features[0].Scenarios[0].Steps[0].SubSteps![0].Attachments![0].RelativePath);
    }

    [Fact]
    public void Copies_attachments_from_background_steps()
    {
        var sourceFile = Path.Combine(_tempDir, "bg.txt");
        File.WriteAllText(sourceFile, "background data");
        var reportsDir = Path.Combine(_tempDir, "Reports");
        Directory.CreateDirectory(reportsDir);

        var features = new[]
        {
            new Feature
            {
                DisplayName = "F1",
                Scenarios =
                [
                    new Scenario
                    {
                        Id = "s1", DisplayName = "S1", Result = ExecutionResult.Passed,
                        BackgroundSteps =
                        [
                            new ScenarioStep
                            {
                                Keyword = "Given", Text = "bg step", Status = ExecutionResult.Passed,
                                Attachments = [new FileAttachment("bg.txt", sourceFile)]
                            }
                        ],
                        Steps =
                        [
                            new ScenarioStep { Keyword = "When", Text = "action", Status = ExecutionResult.Passed }
                        ]
                    }
                ]
            }
        };

        ReportGenerator.CopyAttachmentsToReportsFolder(features, reportsDir);

        Assert.True(File.Exists(Path.Combine(reportsDir, "attachments", "bg.txt")));
        Assert.Equal("attachments/bg.txt", features[0].Scenarios[0].BackgroundSteps![0].Attachments![0].RelativePath);
    }

    [Fact]
    public void Relative_source_path_is_resolved_from_base_directory()
    {
        // Create a file relative to the current directory
        var relativeDir = Path.Combine(_tempDir, "rel");
        Directory.CreateDirectory(relativeDir);
        var sourceFile = Path.Combine(relativeDir, "data.json");
        File.WriteAllText(sourceFile, "{}");
        var reportsDir = Path.Combine(_tempDir, "Reports");
        Directory.CreateDirectory(reportsDir);

        // Use the absolute path but pretend it's what the user provides
        var features = CreateFeatures(new FileAttachment("data.json", sourceFile));

        ReportGenerator.CopyAttachmentsToReportsFolder(features, reportsDir);

        Assert.True(File.Exists(Path.Combine(reportsDir, "attachments", "data.json")));
        Assert.Equal("attachments/data.json", features[0].Scenarios[0].Steps[0].Attachments![0].RelativePath);
    }

    [Fact]
    public void Same_source_file_is_not_copied_twice()
    {
        var sourceFile = Path.Combine(_tempDir, "shared.txt");
        File.WriteAllText(sourceFile, "shared content");
        var reportsDir = Path.Combine(_tempDir, "Reports");
        Directory.CreateDirectory(reportsDir);

        var features = new[]
        {
            new Feature
            {
                DisplayName = "F1",
                Scenarios =
                [
                    new Scenario
                    {
                        Id = "s1", DisplayName = "S1", Result = ExecutionResult.Passed,
                        Steps =
                        [
                            new ScenarioStep
                            {
                                Keyword = "When", Text = "step A", Status = ExecutionResult.Passed,
                                Attachments = [new FileAttachment("shared.txt", sourceFile)]
                            },
                            new ScenarioStep
                            {
                                Keyword = "Then", Text = "step B", Status = ExecutionResult.Passed,
                                Attachments = [new FileAttachment("shared.txt", sourceFile)]
                            }
                        ]
                    }
                ]
            }
        };

        ReportGenerator.CopyAttachmentsToReportsFolder(features, reportsDir);

        // Both should point to the same destination
        var stepA = features[0].Scenarios[0].Steps[0].Attachments![0];
        var stepB = features[0].Scenarios[0].Steps[1].Attachments![0];
        Assert.Equal("attachments/shared.txt", stepA.RelativePath);
        Assert.Equal("attachments/shared.txt", stepB.RelativePath);

        // Only one file should exist
        var files = Directory.GetFiles(Path.Combine(reportsDir, "attachments"));
        Assert.Single(files);
    }

    [Fact]
    public void Already_relative_attachments_path_is_not_rewritten()
    {
        var reportsDir = Path.Combine(_tempDir, "Reports");
        Directory.CreateDirectory(Path.Combine(reportsDir, "attachments"));
        File.WriteAllText(Path.Combine(reportsDir, "attachments", "shot.png"), "fake image data");

        // A path an earlier call over the same model already rewrote (LightBDD's formatter, then the host's own): this
        // run's copy, there already
        var features = CreateFeatures(new FileAttachment("shot.png", "attachments/shot.png"));

        ReportGenerator.CopyAttachmentsToReportsFolder(features, reportsDir);

        // Should remain unchanged — no copying attempted for already-relative paths
        Assert.Equal("attachments/shot.png", features[0].Scenarios[0].Steps[0].Attachments![0].RelativePath);
    }

    [Fact]
    public void Rendered_html_contains_rewritten_attachment_href()
    {
        var sourceFile = Path.Combine(_tempDir, "openapi.json");
        File.WriteAllText(sourceFile, "{\"openapi\":\"3.0\"}");
        var reportsDir = Path.Combine(_tempDir, "Reports");
        Directory.CreateDirectory(reportsDir);

        var features = CreateFeatures(new FileAttachment("OpenAPI Spec", sourceFile));

        ReportGenerator.CopyAttachmentsToReportsFolder(features, reportsDir);

        var html = ReportGenerator.GenerateHtmlReport(
            Array.Empty<DiagramAsCode>(), features,
            DateTime.UtcNow, DateTime.UtcNow,
            null, Path.Combine(_tempDir, "Report.html"), "Test", true,
            diagramFormat: DiagramFormat.PlantUml,
            plantUmlRendering: PlantUmlRendering.BrowserJs);

        var content = File.ReadAllText(html);
        Assert.Contains("href=\"attachments/openapi.json\"", content);
        Assert.Contains(">OpenAPI Spec</a>", content);
    }

    [Fact]
    public void Copies_scenario_level_attachments_to_reports_folder()
    {
        var sourceFile = Path.Combine(_tempDir, "spec.json");
        File.WriteAllText(sourceFile, "{}");
        var reportsDir = Path.Combine(_tempDir, "Reports");
        Directory.CreateDirectory(reportsDir);

        var features = new[]
        {
            new Feature
            {
                DisplayName = "F1",
                Scenarios =
                [
                    new Scenario
                    {
                        Id = "s1", DisplayName = "S1", Result = ExecutionResult.Passed,
                        Attachments = [new FileAttachment("spec.json", sourceFile)],
                        Steps =
                        [
                            new ScenarioStep { Keyword = "When", Text = "a step", Status = ExecutionResult.Passed }
                        ]
                    }
                ]
            }
        };

        ReportGenerator.CopyAttachmentsToReportsFolder(features, reportsDir);

        Assert.True(File.Exists(Path.Combine(reportsDir, "attachments", "spec.json")));
        Assert.Equal("attachments/spec.json", features[0].Scenarios[0].Attachments![0].RelativePath);
    }

    private static Feature[] CreateFeatures(FileAttachment attachment)
    {
        return
        [
            new Feature
            {
                DisplayName = "F1",
                Scenarios =
                [
                    new Scenario
                    {
                        Id = "s1", DisplayName = "S1", Result = ExecutionResult.Passed,
                        Steps =
                        [
                            new ScenarioStep
                            {
                                Keyword = "When", Text = "a step", Status = ExecutionResult.Passed,
                                Attachments = [attachment]
                            }
                        ]
                    }
                ]
            }
        ];
    }
}
