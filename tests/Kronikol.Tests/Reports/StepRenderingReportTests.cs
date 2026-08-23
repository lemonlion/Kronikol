using Kronikol.Reports;

namespace Kronikol.Tests.Reports;

public class StepRenderingReportTests
{
    private static Feature[] MakeFeatures(Scenario scenario) =>
    [
        new Feature
        {
            DisplayName = "Test Feature",
            Scenarios = [scenario]
        }
    ];

    private static string GenerateReport(Feature[] features, string fileName)
    {
        var path = ReportGenerator.GenerateHtmlReport(
            [], features,
            DateTime.UtcNow, DateTime.UtcNow,
            null, fileName, "Test", true,
            diagramFormat: DiagramFormat.PlantUml, plantUmlRendering: PlantUmlRendering.BrowserJs);
        return File.ReadAllText(path);
    }

    private static string GenerateReportSeparateBackground(Feature[] features, string fileName)
    {
        var path = ReportGenerator.GenerateHtmlReport(
            [], features,
            DateTime.UtcNow, DateTime.UtcNow,
            null, fileName, "Test", true,
            diagramFormat: DiagramFormat.PlantUml, plantUmlRendering: PlantUmlRendering.BrowserJs,
            separateBackgroundSteps: true);
        return File.ReadAllText(path);
    }

    private static string GenerateReportNumbered(Feature[] features, string fileName, bool separateBackgroundSteps = false)
    {
        var path = ReportGenerator.GenerateHtmlReport(
            [], features,
            DateTime.UtcNow, DateTime.UtcNow,
            null, fileName, "Test", true,
            diagramFormat: DiagramFormat.PlantUml, plantUmlRendering: PlantUmlRendering.BrowserJs,
            showStepNumbers: true, separateBackgroundSteps: separateBackgroundSteps);
        return File.ReadAllText(path);
    }

    private static string GenerateReportWithoutKeywordCollapsing(Feature[] features, string fileName)
    {
        var path = ReportGenerator.GenerateHtmlReport(
            [], features,
            DateTime.UtcNow, DateTime.UtcNow,
            null, fileName, "Test", true,
            diagramFormat: DiagramFormat.PlantUml, plantUmlRendering: PlantUmlRendering.BrowserJs,
            collapseRepeatedStepKeywords: false);
        return File.ReadAllText(path);
    }

    [Fact]
    public void Report_renders_steps_when_present()
    {
        var scenario = new Scenario
        {
            Id = "s1", DisplayName = "Test scenario",
            Steps =
            [
                new ScenarioStep { Keyword = "Given", Text = "a valid request" },
                new ScenarioStep { Keyword = "When", Text = "the request is sent" },
                new ScenarioStep { Keyword = "Then", Text = "the response is successful" }
            ]
        };
        var content = GenerateReport(MakeFeatures(scenario), "StepRender.html");
        Assert.Contains("scenario-steps", content);
        Assert.Contains("step-keyword", content);
        Assert.Contains("Given", content);
        Assert.Contains("a valid request", content);
    }

    [Fact]
    public void Report_does_not_render_steps_section_when_no_steps()
    {
        var scenario = new Scenario { Id = "s1", DisplayName = "No steps" };
        var content = GenerateReport(MakeFeatures(scenario), "NoSteps.html");
        Assert.DoesNotContain("<details class=\"scenario-steps\"", content);
    }

    [Fact]
    public void Report_wraps_steps_in_collapsible_details_element()
    {
        var scenario = new Scenario
        {
            Id = "s1", DisplayName = "Test scenario",
            Steps =
            [
                new ScenarioStep { Keyword = "Given", Text = "a valid request" },
                new ScenarioStep { Keyword = "Then", Text = "the response is successful" }
            ]
        };
        var content = GenerateReport(MakeFeatures(scenario), "CollapsibleSteps.html");
        Assert.Contains("<details class=\"scenario-steps\"", content);
        Assert.Contains("<summary class=\"h4\">Steps</summary>", content);
    }

    [Fact]
    public void Report_steps_details_element_is_open_by_default()
    {
        var scenario = new Scenario
        {
            Id = "s1", DisplayName = "Test scenario",
            Steps =
            [
                new ScenarioStep { Keyword = "Given", Text = "something" }
            ]
        };
        var content = GenerateReport(MakeFeatures(scenario), "StepsOpen.html");
        Assert.Contains("<details class=\"scenario-steps\" open>", content);
    }

    [Fact]
    public void Report_scenario_steps_css_has_no_border_left()
    {
        var scenario = new Scenario
        {
            Id = "s1", DisplayName = "Test",
            Steps = [new ScenarioStep { Keyword = "Given", Text = "something" }]
        };
        var content = GenerateReport(MakeFeatures(scenario), "StepNoBorder.html");
        // The .scenario-steps CSS should not include a border-left
        var cssStart = content.IndexOf(".scenario-steps,");
        Assert.True(cssStart >= 0, ".scenario-steps CSS class should exist");
        var cssEnd = content.IndexOf("}", cssStart);
        var cssBlock = content[cssStart..cssEnd];
        Assert.DoesNotContain("border-left", cssBlock);
    }

    [Fact]
    public void Report_renders_step_status_icon()
    {
        var scenario = new Scenario
        {
            Id = "s1", DisplayName = "Test",
            Steps =
            [
                new ScenarioStep { Keyword = "Given", Text = "something", Status = ExecutionResult.Passed },
                new ScenarioStep { Keyword = "When", Text = "action", Status = ExecutionResult.Failed }
            ]
        };
        var content = GenerateReport(MakeFeatures(scenario), "StepStatus.html");
        Assert.Contains("step-status passed", content);
        Assert.Contains("step-status failed", content);
    }

    [Fact]
    public void Report_renders_step_duration()
    {
        var scenario = new Scenario
        {
            Id = "s1", DisplayName = "Test",
            Steps =
            [
                new ScenarioStep { Keyword = "Given", Text = "something", Duration = TimeSpan.FromMilliseconds(1234) }
            ]
        };
        var content = GenerateReport(MakeFeatures(scenario), "StepDuration.html");
        Assert.Contains("step-duration", content);
        Assert.Contains("1.2s", content);
    }

    [Fact]
    public void Report_renders_nested_substeps()
    {
        var scenario = new Scenario
        {
            Id = "s1", DisplayName = "Test",
            Steps =
            [
                new ScenarioStep
                {
                    Keyword = "Given", Text = "a valid request body",
                    SubSteps =
                    [
                        new ScenarioStep { Keyword = "And", Text = "the body specifies milk" },
                        new ScenarioStep { Keyword = "And", Text = "the body specifies eggs" }
                    ]
                }
            ]
        };
        var content = GenerateReport(MakeFeatures(scenario), "SubSteps.html");
        Assert.Contains("sub-steps", content);
        Assert.Contains("step-collapsible", content);
        Assert.Contains("<summary>", content);
        Assert.Contains("the body specifies milk", content);
    }

    [Fact]
    public void Report_renders_step_comments()
    {
        var scenario = new Scenario
        {
            Id = "s1", DisplayName = "Test",
            Steps =
            [
                new ScenarioStep
                {
                    Keyword = "Then", Text = "the result is valid",
                    Comments = ["Verifying business rule XYZ"]
                }
            ]
        };
        var content = GenerateReport(MakeFeatures(scenario), "StepComments.html");
        Assert.Contains("step-comment", content);
        Assert.Contains("Verifying business rule XYZ", content);
    }

    [Fact]
    public void Report_renders_file_attachments()
    {
        var scenario = new Scenario
        {
            Id = "s1", DisplayName = "Test",
            Steps =
            [
                new ScenarioStep
                {
                    Keyword = "Then", Text = "the page looks right",
                    Attachments = [new FileAttachment("screenshot.png", "Reports/screenshot.png")]
                }
            ]
        };
        var content = GenerateReport(MakeFeatures(scenario), "Attachments.html");
        Assert.Contains("step-attachment", content);
        Assert.Contains("screenshot.png", content);
    }

    [Theory]
    [InlineData("screenshot.png")]
    [InlineData("result.jpg")]
    [InlineData("diff.jpeg")]
    [InlineData("animation.gif")]
    [InlineData("photo.webp")]
    public void Report_renders_image_attachments_as_inline_img(string fileName)
    {
        var scenario = new Scenario
        {
            Id = "s1", DisplayName = "Test",
            Steps =
            [
                new ScenarioStep
                {
                    Keyword = "Then", Text = "the page looks right",
                    Attachments = [new FileAttachment(fileName, $"attachments/{fileName}")]
                }
            ]
        };
        var content = GenerateReport(MakeFeatures(scenario), $"ImgAttach_{fileName}.html");
        Assert.Contains("<img", content);
        Assert.Contains($"attachments/{fileName}", content);
        Assert.Contains("attachment-image", content);
    }

    [Fact]
    public void Report_renders_non_image_attachments_as_links()
    {
        var scenario = new Scenario
        {
            Id = "s1", DisplayName = "Test",
            Steps =
            [
                new ScenarioStep
                {
                    Keyword = "Then", Text = "the log is saved",
                    Attachments = [new FileAttachment("output.txt", "attachments/output.txt")]
                }
            ]
        };
        var content = GenerateReport(MakeFeatures(scenario), "NonImgAttach.html");
        Assert.DoesNotContain("<img", content);
        Assert.Contains("<a class=\"step-attachment\"", content);
        Assert.Contains("output.txt", content);
    }

    [Fact]
    public void Report_image_attachment_is_wrapped_in_clickable_link()
    {
        var scenario = new Scenario
        {
            Id = "s1", DisplayName = "Test",
            Steps =
            [
                new ScenarioStep
                {
                    Keyword = "Then", Text = "the page looks right",
                    Attachments = [new FileAttachment("screenshot.png", "attachments/screenshot.png")]
                }
            ]
        };
        var content = GenerateReport(MakeFeatures(scenario), "ImgAttachLink.html");
        // Image should be inside an anchor that opens lightbox
        Assert.Contains("attachment-image-link", content);
        Assert.Contains("attachments/screenshot.png", content);
    }

    [Fact]
    public void Report_renders_feature_description()
    {
        var features = new[]
        {
            new Feature
            {
                DisplayName = "Cake",
                Description = "As a dessert provider I want to create cakes",
                Scenarios = [new Scenario { Id = "s1", DisplayName = "Test" }]
            }
        };
        var content = GenerateReport(features, "FeatureDesc.html");
        Assert.Contains("feature-description", content);
        Assert.Contains("As a dessert provider I want to create cakes", content);
    }

    [Fact]
    public void Report_renders_scenario_labels()
    {
        var scenario = new Scenario
        {
            Id = "s1", DisplayName = "Test",
            Labels = ["smoke", "regression"]
        };
        var content = GenerateReport(MakeFeatures(scenario), "ScenarioLabels.html");
        Assert.Contains("smoke", content);
        Assert.Contains("regression", content);
    }

    [Fact]
    public void Report_renders_feature_labels()
    {
        var features = new[]
        {
            new Feature
            {
                DisplayName = "Cake",
                Labels = ["api", "v2"],
                Scenarios = [new Scenario { Id = "s1", DisplayName = "Test" }]
            }
        };
        var content = GenerateReport(features, "FeatureLabels.html");
        Assert.Contains("api", content);
        Assert.Contains("v2", content);
    }

    [Fact]
    public void Report_renders_bypassed_status()
    {
        var scenario = new Scenario
        {
            Id = "s1", DisplayName = "Bypassed test",
            Result = ExecutionResult.Bypassed
        };
        var content = GenerateReport(MakeFeatures(scenario), "Bypassed.html");
        Assert.Contains("data-status=\"Bypassed\"", content);
    }

    [Fact]
    public void Report_renders_skipped_after_failure_status()
    {
        var scenario = new Scenario
        {
            Id = "s1", DisplayName = "SkippedAfterFailure test",
            Result = ExecutionResult.SkippedAfterFailure
        };
        var content = GenerateReport(MakeFeatures(scenario), "SkippedAfterFailure.html");
        Assert.Contains("data-status=\"SkippedAfterFailure\"", content);
    }

    [Fact]
    public void Report_renders_passed_bypassed_gradient_for_parent_with_bypassed_substeps()
    {
        var scenario = new Scenario
        {
            Id = "s1", DisplayName = "Gradient test",
            Steps =
            [
                new ScenarioStep
                {
                    Keyword = "Given", Text = "a composite step",
                    Status = ExecutionResult.Passed,
                    SubSteps =
                    [
                        new ScenarioStep { Keyword = "And", Text = "passed child", Status = ExecutionResult.Passed },
                        new ScenarioStep { Keyword = "And", Text = "bypassed child", Status = ExecutionResult.Bypassed }
                    ]
                }
            ]
        };
        var content = GenerateReport(MakeFeatures(scenario), "PassedBypassed.html");
        Assert.Contains("step-status passed-bypassed", content);
    }

    [Fact]
    public void Report_does_not_render_gradient_when_parent_passed_without_bypassed_substeps()
    {
        var scenario = new Scenario
        {
            Id = "s1", DisplayName = "No gradient test",
            Steps =
            [
                new ScenarioStep
                {
                    Keyword = "Given", Text = "a composite step",
                    Status = ExecutionResult.Passed,
                    SubSteps =
                    [
                        new ScenarioStep { Keyword = "And", Text = "passed child", Status = ExecutionResult.Passed },
                        new ScenarioStep { Keyword = "And", Text = "another passed child", Status = ExecutionResult.Passed }
                    ]
                }
            ]
        };
        var content = GenerateReport(MakeFeatures(scenario), "NoGradient.html");
        Assert.DoesNotContain("class=\"step-status passed-bypassed\"", content);
        Assert.Contains("class=\"step-status passed\"", content);
    }

    [Fact]
    public void Report_renders_passed_skipped_for_parent_with_skipped_substeps()
    {
        var scenario = new Scenario
        {
            Id = "s1", DisplayName = "Passed-skipped test",
            Steps =
            [
                new ScenarioStep
                {
                    Keyword = "Given", Text = "a composite step",
                    Status = ExecutionResult.Passed,
                    SubSteps =
                    [
                        new ScenarioStep { Keyword = "And", Text = "passed child", Status = ExecutionResult.Passed },
                        new ScenarioStep { Keyword = "And", Text = "skipped child", Status = ExecutionResult.Skipped }
                    ]
                }
            ]
        };
        var content = GenerateReport(MakeFeatures(scenario), "PassedSkipped.html");
        Assert.Contains("step-status passed-skipped", content);
        Assert.Contains("&#10003;", content); // tick icon
    }

    [Fact]
    public void Report_renders_passed_skipped_for_deeply_nested_skipped_descendant()
    {
        var scenario = new Scenario
        {
            Id = "s1", DisplayName = "Deep skipped test",
            Steps =
            [
                new ScenarioStep
                {
                    Keyword = "Given", Text = "outer step",
                    Status = ExecutionResult.Passed,
                    SubSteps =
                    [
                        new ScenarioStep
                        {
                            Keyword = "And", Text = "mid step",
                            Status = ExecutionResult.Passed,
                            SubSteps =
                            [
                                new ScenarioStep { Keyword = "And", Text = "deep skipped", Status = ExecutionResult.Skipped }
                            ]
                        }
                    ]
                }
            ]
        };
        var content = GenerateReport(MakeFeatures(scenario), "DeepPassedSkipped.html");
        Assert.Contains("step-status passed-skipped", content);
    }

    [Fact]
    public void Report_renders_passed_skipped_when_both_bypassed_and_skipped_substeps()
    {
        var scenario = new Scenario
        {
            Id = "s1", DisplayName = "Mixed bypassed+skipped test",
            Steps =
            [
                new ScenarioStep
                {
                    Keyword = "Given", Text = "a composite step",
                    Status = ExecutionResult.Passed,
                    SubSteps =
                    [
                        new ScenarioStep { Keyword = "And", Text = "passed child", Status = ExecutionResult.Passed },
                        new ScenarioStep { Keyword = "And", Text = "bypassed child", Status = ExecutionResult.Bypassed },
                        new ScenarioStep { Keyword = "And", Text = "skipped child", Status = ExecutionResult.Skipped }
                    ]
                }
            ]
        };
        var content = GenerateReport(MakeFeatures(scenario), "MixedBypassedSkipped.html");
        // Skipped takes priority over bypassed
        Assert.Contains("step-status passed-skipped", content);
        Assert.DoesNotContain("step-status passed-bypassed", content);
    }

    [Fact]
    public void Report_does_not_render_passed_skipped_for_skipped_after_failure_substeps()
    {
        var scenario = new Scenario
        {
            Id = "s1", DisplayName = "SkippedAfterFailure not treated as skipped",
            Steps =
            [
                new ScenarioStep
                {
                    Keyword = "Given", Text = "a composite step",
                    Status = ExecutionResult.Passed,
                    SubSteps =
                    [
                        new ScenarioStep { Keyword = "And", Text = "passed child", Status = ExecutionResult.Passed },
                        new ScenarioStep { Keyword = "And", Text = "skipped-after-failure child", Status = ExecutionResult.SkippedAfterFailure }
                    ]
                }
            ]
        };
        var content = GenerateReport(MakeFeatures(scenario), "NotPassedSkipped.html");
        Assert.DoesNotContain("step-status passed-skipped", content);
    }

    [Fact]
    public void Report_passed_skipped_tooltip_mentions_skipped_substeps()
    {
        var scenario = new Scenario
        {
            Id = "s1", DisplayName = "Tooltip test",
            Steps =
            [
                new ScenarioStep
                {
                    Keyword = "Given", Text = "a composite step",
                    Status = ExecutionResult.Passed,
                    SubSteps =
                    [
                        new ScenarioStep { Keyword = "And", Text = "passed child", Status = ExecutionResult.Passed },
                        new ScenarioStep { Keyword = "And", Text = "skipped child", Status = ExecutionResult.Skipped }
                    ]
                }
            ]
        };
        var content = GenerateReport(MakeFeatures(scenario), "SkippedTooltip.html");
        Assert.Contains("with skipped sub-steps", content);
    }

    [Fact]
    public void Report_passed_skipped_css_class_uses_grey_background()
    {
        var scenario = new Scenario
        {
            Id = "s1", DisplayName = "CSS test",
            Steps = [new ScenarioStep { Keyword = "Given", Text = "something" }]
        };
        var content = GenerateReport(MakeFeatures(scenario), "PassedSkippedCss.html");
        Assert.Contains(".step-status.passed-skipped", content);
        Assert.Contains("#949494", content); // grey — same as skipped
    }

    [Fact]
    public void Report_step_css_classes_exist()
    {
        var scenario = new Scenario
        {
            Id = "s1", DisplayName = "Test",
            Steps = [new ScenarioStep { Keyword = "Given", Text = "something" }]
        };
        var content = GenerateReport(MakeFeatures(scenario), "StepCss.html");
        Assert.Contains(".scenario-steps", content);
        Assert.Contains(".step-keyword", content);
    }

    [Fact]
    public void Report_step_status_css_has_user_select_none_and_margin_left()
    {
        var scenario = new Scenario
        {
            Id = "s1", DisplayName = "Test",
            Steps = [new ScenarioStep { Keyword = "Given", Text = "something" }]
        };
        var content = GenerateReport(MakeFeatures(scenario), "StepStatusCss.html");
        Assert.Contains("user-select: none", content);
        Assert.Contains("margin-left: 0.5em", content);
    }

    [Fact]
    public void Report_renders_category_filter_when_categories_present()
    {
        var scenario = new Scenario
        {
            Id = "s1", DisplayName = "Test",
            Categories = ["Orders"]
        };
        var content = GenerateReport(MakeFeatures(scenario), "CategoryFilter.html");
        Assert.Contains("category-filters", content);
        Assert.Contains("Orders", content);
    }

    [Fact]
    public void Report_does_not_render_category_filter_when_no_categories()
    {
        var scenario = new Scenario { Id = "s1", DisplayName = "Test" };
        var content = GenerateReport(MakeFeatures(scenario), "NoCategoryFilter.html");
        Assert.DoesNotContain("<div class=\"category-filters\">", content);
    }

    [Fact]
    public void Report_scenario_steps_css_has_rounded_border()
    {
        var scenario = new Scenario
        {
            Id = "s1", DisplayName = "Test",
            Steps = [new ScenarioStep { Keyword = "Given", Text = "something" }]
        };
        var content = GenerateReport(MakeFeatures(scenario), "StepBorder.html");
        var cssStart = content.IndexOf(".scenario-steps,");
        Assert.True(cssStart >= 0, ".scenario-steps CSS class should exist");
        var cssEnd = content.IndexOf("}", cssStart);
        var cssBlock = content[cssStart..cssEnd];
        Assert.Contains("border-radius: 1em", cssBlock);
        Assert.Contains("border: 1px solid", cssBlock);
        Assert.Contains("border-color: rgb(224, 224, 224)", cssBlock);
    }

    [Fact]
    public void Report_substeps_are_collapsed_by_default()
    {
        var scenario = new Scenario
        {
            Id = "s1", DisplayName = "Test",
            Steps =
            [
                new ScenarioStep
                {
                    Keyword = "Given", Text = "setup", Status = ExecutionResult.Passed,
                    SubSteps =
                    [
                        new ScenarioStep { Keyword = "And", Text = "sub1", Status = ExecutionResult.Passed },
                        new ScenarioStep { Keyword = "And", Text = "sub2", Status = ExecutionResult.Passed }
                    ]
                }
            ]
        };
        var content = GenerateReport(MakeFeatures(scenario), "SubStepsCollapsed.html");
        Assert.Contains("step-collapsible", content);
        Assert.DoesNotContain("<details class=\"step step-collapsible\" open>", content);
    }

    [Fact]
    public void Report_substeps_auto_expand_when_child_failed()
    {
        var scenario = new Scenario
        {
            Id = "s1", DisplayName = "Test",
            Steps =
            [
                new ScenarioStep
                {
                    Keyword = "Then", Text = "assertions", Status = ExecutionResult.Failed,
                    SubSteps =
                    [
                        new ScenarioStep { Keyword = null, Text = "\u2713 x == 1", Status = ExecutionResult.Passed },
                        new ScenarioStep { Keyword = null, Text = "\u2717 y == 2", Status = ExecutionResult.Failed }
                    ]
                }
            ]
        };
        var content = GenerateReport(MakeFeatures(scenario), "SubStepsExpandFailed.html");
        Assert.Contains("<details class=\"step step-collapsible\" open>", content);
    }

    [Fact]
    public void Report_substeps_auto_expand_when_nested_descendant_failed()
    {
        var scenario = new Scenario
        {
            Id = "s1", DisplayName = "Test",
            Steps =
            [
                new ScenarioStep
                {
                    Keyword = "Then", Text = "outer", Status = ExecutionResult.Failed,
                    SubSteps =
                    [
                        new ScenarioStep
                        {
                            Keyword = "And", Text = "inner", Status = ExecutionResult.Failed,
                            SubSteps =
                            [
                                new ScenarioStep { Keyword = null, Text = "\u2717 deep fail", Status = ExecutionResult.Failed }
                            ]
                        }
                    ]
                }
            ]
        };
        var content = GenerateReport(MakeFeatures(scenario), "SubStepsExpandNested.html");
        // Both outer and inner should be expanded because they have failed descendants
        Assert.DoesNotContain("<details class=\"step step-collapsible\">", content);
    }

    [Fact]
    public void Report_inline_table_renders_without_collapsible_wrapper_when_no_substeps()
    {
        var scenario = new Scenario
        {
            Id = "s1", DisplayName = "Test",
            Steps =
            [
                new ScenarioStep
                {
                    Keyword = "Given", Text = "data", Status = ExecutionResult.Passed,
                    Parameters =
                    [
                        new StepParameter
                        {
                            Name = "data",
                            Kind = StepParameterKind.Tabular,
                            TabularValue = new TabularParameterValue(
                                [new TabularColumn("Name", false)],
                                [new TabularRow(TableRowType.Matching,
                                    [new TabularCell("Alice", null, VerificationStatus.NotApplicable)])])
                        }
                    ]
                }
            ]
        };
        var content = GenerateReport(MakeFeatures(scenario), "InlineTableOpen.html");
        // Inline tables without sub-steps should render as a plain div, not a collapsible details
        Assert.DoesNotContain("<details class=\"step step-collapsible\"", content);
        Assert.Contains("<div class=\"step\">", content);
        Assert.Contains("step-param-table", content);
    }

    [Fact]
    public void Report_inline_table_with_substeps_renders_table_inside_summary()
    {
        var scenario = new Scenario
        {
            Id = "s1", DisplayName = "Test",
            Steps =
            [
                new ScenarioStep
                {
                    Keyword = "Given", Text = "data", Status = ExecutionResult.Passed,
                    Parameters =
                    [
                        new StepParameter
                        {
                            Name = "data",
                            Kind = StepParameterKind.Tabular,
                            TabularValue = new TabularParameterValue(
                                [new TabularColumn("Name", false)],
                                [new TabularRow(TableRowType.Matching,
                                    [new TabularCell("Alice", null, VerificationStatus.NotApplicable)])])
                        }
                    ],
                    SubSteps =
                    [
                        new ScenarioStep
                        {
                            Keyword = "Then", Text = "sub-step", Status = ExecutionResult.Passed
                        }
                    ]
                }
            ]
        };
        var content = GenerateReport(MakeFeatures(scenario), "InlineTableWithSubSteps.html");
        // Step should be collapsible (has sub-steps)
        Assert.Contains("<details class=\"step step-collapsible\"", content);
        // Table should appear before </summary> (inside summary), sub-steps after
        var summaryEnd = content.IndexOf("</summary>");
        var tablePos = content.IndexOf("step-param-table");
        var subStepsPos = content.IndexOf("<div class=\"sub-steps\">");
        Assert.True(summaryEnd > 0, "Should have a </summary> tag");
        Assert.True(tablePos > 0, "Should have a step-param-table");
        Assert.True(subStepsPos > 0, "Should have sub-steps div");
        Assert.True(tablePos < summaryEnd, "Table should be inside <summary>");
        Assert.True(subStepsPos > summaryEnd, "Sub-steps should be after </summary>");
    }

    [Fact]
    public void Report_renders_scenario_level_attachments()
    {
        var scenario = new Scenario
        {
            Id = "s1", DisplayName = "Upload spec",
            Result = ExecutionResult.Passed,
            Attachments = [new FileAttachment("openapi.json", "attachments/openapi.json")],
            Steps =
            [
                new ScenarioStep { Keyword = "Then", Text = "the spec is valid", Status = ExecutionResult.Passed }
            ]
        };
        var content = GenerateReport(MakeFeatures(scenario), "ScenarioAttach.html");
        Assert.Contains("scenario-attachments", content);
        Assert.Contains("openapi.json", content);
    }

    [Fact]
    public void Report_renders_scenario_level_image_attachment_as_img()
    {
        var scenario = new Scenario
        {
            Id = "s1", DisplayName = "Screenshot test",
            Result = ExecutionResult.Passed,
            Attachments = [new FileAttachment("screenshot.png", "attachments/screenshot.png")],
            Steps =
            [
                new ScenarioStep { Keyword = "Then", Text = "done", Status = ExecutionResult.Passed }
            ]
        };
        var content = GenerateReport(MakeFeatures(scenario), "ScenarioImgAttach.html");
        Assert.Contains("<img", content);
        Assert.Contains("attachment-image", content);
        Assert.Contains("attachments/screenshot.png", content);
    }

    [Fact]
    public void Report_renders_scenario_level_non_image_attachment_as_link()
    {
        var scenario = new Scenario
        {
            Id = "s1", DisplayName = "Log test",
            Result = ExecutionResult.Passed,
            Attachments = [new FileAttachment("output.txt", "attachments/output.txt")],
            Steps =
            [
                new ScenarioStep { Keyword = "Then", Text = "done", Status = ExecutionResult.Passed }
            ]
        };
        var content = GenerateReport(MakeFeatures(scenario), "ScenarioLinkAttach.html");
        Assert.DoesNotContain("<img", content);
        Assert.Contains("step-attachment", content);
        Assert.Contains("output.txt", content);
    }


    [Fact]
    public void Report_combines_background_steps_with_regular_steps_by_default()
    {
        var scenario = new Scenario
        {
            Id = "s1", DisplayName = "Test",
            BackgroundSteps = [new ScenarioStep { Keyword = "Given", Text = "the system is running" }],
            Steps = [new ScenarioStep { Keyword = "When", Text = "something happens" }]
        };
        var content = GenerateReport(MakeFeatures(scenario), "BgCombined.html");
        Assert.DoesNotContain("<details class=\"scenario-background\">", content);
        Assert.DoesNotContain("Background Steps</summary>", content);
        var steps = ReportMarkup.ElementsWithClass(content, "step");
        Assert.Equal(2, steps.Count);
        Assert.Contains("step-background", steps[0]);
        Assert.DoesNotContain("step-background", steps[1]);
        Assert.Contains("the system is running", content);
        Assert.Contains("something happens", content);
    }

    [Fact]
    public void Report_separates_background_steps_when_option_enabled()
    {
        var scenario = new Scenario
        {
            Id = "s1", DisplayName = "Test",
            BackgroundSteps = [new ScenarioStep { Keyword = "Given", Text = "the system is running" }],
            Steps = [new ScenarioStep { Keyword = "When", Text = "something happens" }]
        };
        var content = GenerateReportSeparateBackground(MakeFeatures(scenario), "BgSeparate.html");
        Assert.Contains("<details class=\"scenario-background\">", content);
        Assert.Contains("<summary class=\"h4\">Background Steps</summary>", content);
    }

    [Fact]
    public void Report_renders_and_keyword_for_background_given_followed_by_scenario_given()
    {
        var scenario = new Scenario
        {
            Id = "s1", DisplayName = "Test",
            BackgroundSteps = [new ScenarioStep { Keyword = "Given", Text = "the system is running" }],
            Steps =
            [
                new ScenarioStep { Keyword = "Given", Text = "a valid request" },
                new ScenarioStep { Keyword = "When", Text = "it is sent" }
            ]
        };
        var content = GenerateReport(MakeFeatures(scenario), "BgCollapse.html");
        var keywords = ReportMarkup.InnerTextsOfClass(content, "step-keyword");
        Assert.Equal(["Given", "And", "When"], keywords);
    }

    [Fact]
    public void Report_leaves_repeated_keywords_alone_when_collapsing_is_disabled()
    {
        var scenario = new Scenario
        {
            Id = "s1", DisplayName = "Test",
            BackgroundSteps = [new ScenarioStep { Keyword = "Given", Text = "the system is running" }],
            Steps = [new ScenarioStep { Keyword = "Given", Text = "a valid request" }]
        };
        var content = GenerateReportWithoutKeywordCollapsing(MakeFeatures(scenario), "BgNoCollapse.html");
        var keywords = ReportMarkup.InnerTextsOfClass(content, "step-keyword");
        Assert.Equal(["Given", "Given"], keywords);
    }

    [Fact]
    public void Report_separated_mode_does_not_collapse_across_sections()
    {
        var scenario = new Scenario
        {
            Id = "s1", DisplayName = "Test",
            BackgroundSteps = [new ScenarioStep { Keyword = "Given", Text = "the system is running" }],
            Steps = [new ScenarioStep { Keyword = "Given", Text = "a valid request" }]
        };
        var content = GenerateReportSeparateBackground(MakeFeatures(scenario), "BgSeparateNoCollapse.html");
        var keywords = ReportMarkup.InnerTextsOfClass(content, "step-keyword");
        Assert.Equal(["Given", "Given"], keywords);
    }

    [Fact]
    public void Report_numbers_combined_steps_continuously()
    {
        var content = GenerateReportNumbered(MakeFeatures(TwoAndTwo()), "BgNumbersCombined.html");
        var numbers = ReportMarkup.InnerTextsOfClass(content, "step-number");
        Assert.Equal(["1.", "2.", "3.", "4."], numbers);
    }

    [Fact]
    public void Report_numbers_separated_steps_continuously()
    {
        var content = GenerateReportNumbered(MakeFeatures(TwoAndTwo()), "BgNumbersSeparated.html", separateBackgroundSteps: true);
        var numbers = ReportMarkup.InnerTextsOfClass(content, "step-number");
        Assert.Equal(["1.", "2.", "3.", "4."], numbers);
    }

    [Fact]
    public void Report_renders_combined_list_when_only_background_steps_exist()
    {
        var scenario = new Scenario
        {
            Id = "s1", DisplayName = "Test",
            BackgroundSteps = [new ScenarioStep { Keyword = "Given", Text = "bg only" }],
            Steps = null
        };
        var content = GenerateReport(MakeFeatures(scenario), "BgOnly.html");
        Assert.Contains("<details class=\"scenario-steps\" open>", content);
        Assert.Contains("bg only", content);
        Assert.Single(ReportMarkup.ElementsWithClass(content, "step"));
    }

    [Fact]
    public void Report_renders_no_steps_section_when_both_lists_are_empty()
    {
        var scenario = new Scenario { Id = "s1", DisplayName = "Test", Steps = [], BackgroundSteps = [] };
        var content = GenerateReport(MakeFeatures(scenario), "BgNeither.html");
        Assert.DoesNotContain("<details class=\"scenario-steps\" open>", content);
        Assert.DoesNotContain("<details class=\"scenario-background\">", content);
    }

    [Fact]
    public void Report_combined_tabular_parameters_span_background_and_regular_steps()
    {
        // The combined table is decided over the whole rendered list, so a setup table carried by a
        // background step has to reach it.
        var scenario = new Scenario
        {
            Id = "s1", DisplayName = "Test",
            BackgroundSteps =
            [
                new ScenarioStep
                {
                    Keyword = "Given", Text = "these people exist",
                    Parameters =
                    [
                        new StepParameter
                        {
                            Name = "people",
                            Kind = StepParameterKind.Tabular,
                            TabularValue = new TabularParameterValue(
                                [new TabularColumn("Name", false)],
                                [new TabularRow(TableRowType.Matching, [new TabularCell("Ann", null, VerificationStatus.NotApplicable)])])
                        }
                    ]
                }
            ],
            Steps =
            [
                new ScenarioStep
                {
                    Keyword = "Then", Text = "the report lists them",
                    Parameters =
                    [
                        new StepParameter
                        {
                            Name = "listed",
                            Kind = StepParameterKind.Tabular,
                            TabularValue = new TabularParameterValue(
                                [new TabularColumn("Name", false)],
                                [new TabularRow(TableRowType.Matching, [new TabularCell("Ann", null, VerificationStatus.Success)])],
                                IsLinkedOutput: true)
                        }
                    ]
                }
            ]
        };
        var content = GenerateReport(MakeFeatures(scenario), "BgTabular.html");
        Assert.Contains("step-param-combined-table", content);
    }

    [Fact]
    public void Report_search_index_covers_text_that_only_appears_in_a_background_step()
    {
        var content = GenerateReport(MakeFeatures(WithZephyrBackground()), "BgSearch.html");
        var searchAttr = ReportMarkup.AttributeValue(content, "details", "data-search");
        Assert.Contains("zephyr", searchAttr);
    }

    [Fact]
    public void Report_search_index_covers_background_steps_in_separated_mode_too()
    {
        var content = GenerateReportSeparateBackground(MakeFeatures(WithZephyrBackground()), "BgSearchSeparated.html");
        var searchAttr = ReportMarkup.AttributeValue(content, "details", "data-search");
        Assert.Contains("zephyr", searchAttr);
    }

    [Fact]
    public void Report_features_summary_step_count_includes_background_steps()
    {
        var content = GenerateReport(MakeFeatures(TwoAndTwo(ExecutionResult.Passed)), "BgSummaryCount.html");
        var row = ReportMarkup.FeatureSummaryRow(content, "Test Feature");
        // Feature | Scenarios | Passed | Failed | Skipped | Steps | Passed | Failed | Skipped
        Assert.Equal("4", row[5]);
        Assert.Equal("4", row[6]);
    }

    [Fact]
    public void Report_features_summary_shows_step_columns_when_only_background_steps_exist()
    {
        var scenario = new Scenario
        {
            Id = "s1", DisplayName = "Test", Result = ExecutionResult.Passed,
            BackgroundSteps = [new ScenarioStep { Keyword = "Given", Text = "bg only", Status = ExecutionResult.Passed }],
            Steps = []
        };
        var content = GenerateReport(MakeFeatures(scenario), "BgSummaryOnlyBackground.html");
        var row = ReportMarkup.FeatureSummaryRow(content, "Test Feature");
        Assert.Equal(9, row.Count);
        Assert.Equal("1", row[5]);
    }

    [Fact]
    public void Rendering_the_html_does_not_leak_a_collapsed_keyword_into_the_model()
    {
        // Background steps are one shared array across every scenario of a Rule group, and the HTML and the
        // data writers run concurrently — so the And has to be a projection, never a mutation.
        var background = new ScenarioStep[] { new() { Keyword = "Given", Text = "the system is running" } };
        var features = new[]
        {
            new Feature
            {
                DisplayName = "Test Feature",
                Scenarios =
                [
                    new Scenario
                    {
                        Id = "s1", DisplayName = "One",
                        BackgroundSteps = background,
                        Steps = [new ScenarioStep { Keyword = "Given", Text = "a valid request" }]
                    }
                ]
            }
        };
        var html = GenerateReport(features, "BgNoMutation.html");
        Assert.Contains(">And</span>", html);

        Assert.Equal("Given", background[0].Keyword);
        Assert.Equal("Given", features[0].Scenarios[0].Steps![0].Keyword);
    }

    private static Scenario TwoAndTwo(ExecutionResult? status = null) => new()
    {
        Id = "s1", DisplayName = "Test", Result = status ?? ExecutionResult.Passed,
        BackgroundSteps =
        [
            new ScenarioStep { Keyword = "Given", Text = "bg one", Status = status },
            new ScenarioStep { Keyword = "And", Text = "bg two", Status = status }
        ],
        Steps =
        [
            new ScenarioStep { Keyword = "When", Text = "step one", Status = status },
            new ScenarioStep { Keyword = "Then", Text = "step two", Status = status }
        ]
    };

    private static Scenario WithZephyrBackground() => new()
    {
        Id = "s1", DisplayName = "Test",
        BackgroundSteps = [new ScenarioStep { Keyword = "Given", Text = "the zephyr service is armed" }],
        Steps = [new ScenarioStep { Keyword = "When", Text = "something happens" }]
    };
}
