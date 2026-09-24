using Microsoft.Playwright;
using Kronikol.Reports;
using static Kronikol.DefaultDiagramsFetcher;

namespace Kronikol.Tests.EndToEnd;

[Collection(PlaywrightCollections.Reports)]
public class ParameterizedGroupTests : PlaywrightTestBase
{
    public ParameterizedGroupTests(PlaywrightFixture fixture) : base(fixture) { }

    private string GenerateParamReport(string fileName, bool withDiagrams = true, bool withFailure = false)
    {
        var scenarios = new[]
        {
            new Scenario
            {
                Id = "p1", DisplayName = "Process(region: UK, amount: 100)", IsHappyPath = true,
                Result = ExecutionResult.Passed, Duration = TimeSpan.FromSeconds(1),
                OutlineId = "Process",
                ExampleValues = new Dictionary<string, string> { ["region"] = "UK", ["amount"] = "100" },
                Steps =
                [
                    new ScenarioStep { Keyword = "Given", Text = "a valid region UK", Status = ExecutionResult.Passed },
                    new ScenarioStep { Keyword = "When", Text = "processing amount 100", Status = ExecutionResult.Passed },
                    new ScenarioStep { Keyword = "Then", Text = "the result is success", Status = ExecutionResult.Passed }
                ]
            },
            new Scenario
            {
                Id = "p2", DisplayName = "Process(region: US, amount: 200)", IsHappyPath = false,
                Result = withFailure ? ExecutionResult.Failed : ExecutionResult.Passed,
                Duration = TimeSpan.FromSeconds(2),
                OutlineId = "Process",
                ExampleValues = new Dictionary<string, string> { ["region"] = "US", ["amount"] = "200" },
                ErrorMessage = withFailure ? "Expected 200 but got 0" : null,
                ErrorStackTrace = withFailure ? "at ProcessTests.cs:42" : null,
                Steps =
                [
                    new ScenarioStep { Keyword = "Given", Text = "a valid region US", Status = ExecutionResult.Passed },
                    new ScenarioStep { Keyword = "When", Text = "processing amount 200", Status = withFailure ? ExecutionResult.Failed : ExecutionResult.Passed },
                    new ScenarioStep { Keyword = "Then", Text = "the result is success", Status = withFailure ? ExecutionResult.Skipped : ExecutionResult.Passed }
                ]
            },
            new Scenario
            {
                Id = "p3", DisplayName = "Process(region: DE, amount: 300)", IsHappyPath = false,
                Result = ExecutionResult.Passed, Duration = TimeSpan.FromSeconds(1),
                OutlineId = "Process",
                ExampleValues = new Dictionary<string, string> { ["region"] = "DE", ["amount"] = "300" },
                Steps =
                [
                    new ScenarioStep { Keyword = "Given", Text = "a valid region DE", Status = ExecutionResult.Passed },
                    new ScenarioStep { Keyword = "When", Text = "processing amount 300", Status = ExecutionResult.Passed },
                    new ScenarioStep { Keyword = "Then", Text = "the result is success", Status = ExecutionResult.Passed }
                ]
            }
        };

        var features = new[]
        {
            new Feature { DisplayName = "Payment Processing", Scenarios = scenarios }
        };

        var diagrams = withDiagrams
            ? scenarios.Select(s => new DiagramAsCode(s.Id, "",
                $"@startuml\nActor -> Service : {s.ExampleValues!["region"]}\n@enduml")).ToArray()
            : scenarios.Select(s => new DiagramAsCode(s.Id, "", "")).ToArray();

        var path = ReportGenerator.GenerateHtmlReport(
            diagrams, features,
            DateTime.UtcNow, DateTime.UtcNow,
            null, Path.Combine(TempDir, fileName), "Param Test Report", true,
            diagramFormat: DiagramFormat.PlantUml,
            plantUmlRendering: PlantUmlRendering.BrowserJs,
            groupParameterizedTests: true);

        File.Copy(path, Path.Combine(OutputDir, fileName), true);
        return new Uri(path).AbsoluteUri;
    }

    private string GenerateSingleParamReport(string fileName)
    {
        var scenario = new Scenario
        {
            Id = "single1", DisplayName = "Process(region: UK, amount: 100)", IsHappyPath = true,
            Result = ExecutionResult.Passed, Duration = TimeSpan.FromSeconds(1),
            OutlineId = "Process",
            ExampleValues = new Dictionary<string, string> { ["region"] = "UK", ["amount"] = "100" },
            Steps =
            [
                new ScenarioStep { Keyword = "Given", Text = "a valid region UK", Status = ExecutionResult.Passed },
                new ScenarioStep { Keyword = "When", Text = "processing amount 100", Status = ExecutionResult.Passed },
                new ScenarioStep { Keyword = "Then", Text = "the result is success", Status = ExecutionResult.Passed }
            ]
        };

        var features = new[]
        {
            new Feature { DisplayName = "Single Param Feature", Scenarios = [scenario] }
        };
        var diagrams = new[] { new DiagramAsCode(scenario.Id, "",
            "@startuml\nActor -> Service : UK\n@enduml") };

        var path = ReportGenerator.GenerateHtmlReport(
            diagrams, features,
            DateTime.UtcNow, DateTime.UtcNow,
            null, Path.Combine(TempDir, fileName), "Single Param Report", true,
            diagramFormat: DiagramFormat.PlantUml,
            plantUmlRendering: PlantUmlRendering.BrowserJs,
            groupParameterizedTests: true);

        File.Copy(path, Path.Combine(OutputDir, fileName), true);
        return new Uri(path).AbsoluteUri;
    }

    private string GenerateMultiFeatureParamReport(string fileName)
    {
        Scenario[] MakeScenarios(string prefix, string outlineId) =>
        [
            new Scenario
            {
                Id = $"{prefix}-1", DisplayName = $"{outlineId}(v: V1)",
                Result = ExecutionResult.Passed, Duration = TimeSpan.FromSeconds(1),
                OutlineId = outlineId,
                ExampleValues = new Dictionary<string, string> { ["v"] = "V1" },
                Steps =
                [
                    new ScenarioStep { Keyword = "Given", Text = $"{prefix} step 1", Status = ExecutionResult.Passed },
                    new ScenarioStep { Keyword = "Then", Text = $"{prefix} step 2", Status = ExecutionResult.Passed }
                ]
            },
            new Scenario
            {
                Id = $"{prefix}-2", DisplayName = $"{outlineId}(v: V2)",
                Result = ExecutionResult.Passed, Duration = TimeSpan.FromSeconds(2),
                OutlineId = outlineId,
                ExampleValues = new Dictionary<string, string> { ["v"] = "V2" },
                Steps =
                [
                    new ScenarioStep { Keyword = "Given", Text = $"{prefix} step A", Status = ExecutionResult.Passed },
                    new ScenarioStep { Keyword = "Then", Text = $"{prefix} step B", Status = ExecutionResult.Passed }
                ]
            }
        ];

        var scenariosA = MakeScenarios("fA", "ProcessA");
        var scenariosB = MakeScenarios("fB", "ProcessB");

        var features = new[]
        {
            new Feature { DisplayName = "Feature Alpha", Scenarios = scenariosA },
            new Feature { DisplayName = "Feature Beta", Scenarios = scenariosB }
        };

        var allScenarios = scenariosA.Concat(scenariosB).ToArray();
        var diagrams = allScenarios.Select(s =>
            new DiagramAsCode(s.Id, "", $"@startuml\nActor -> Service : {s.Id}\n@enduml")).ToArray();

        var path = ReportGenerator.GenerateHtmlReport(
            diagrams, features,
            DateTime.UtcNow, DateTime.UtcNow,
            null, Path.Combine(TempDir, fileName), "Cross Feature Report", true,
            diagramFormat: DiagramFormat.PlantUml,
            plantUmlRendering: PlantUmlRendering.BrowserJs,
            groupParameterizedTests: true);

        File.Copy(path, Path.Combine(OutputDir, fileName), true);
        return new Uri(path).AbsoluteUri;
    }

    private async Task ExpandFeatures()
    {
        await Page.Locator("button.collapse-expand-all", new() { HasTextString = "Expand All Features" }).ClickAsync();
    }

    private async Task ExpandAll()
    {
        await ExpandFeatures();
        await Page.Locator("button.collapse-expand-all", new() { HasTextString = "Expand All Scenarios" }).ClickAsync();
    }

    // ── Parameterized group renders ──

    [Fact]
    public async Task Parameterized_group_renders_with_table()
    {
        await Page.GotoAsync(GenerateParamReport("ParamGroup.html"));
        await Page.Locator("details.feature").First.WaitForAsync();
        await ExpandFeatures();

        var group = Page.Locator("details.scenario-parameterized");
        await group.WaitForAsync(new() { Timeout = 5000 });
        var rows = group.Locator("table.param-test-table tbody tr");
        Assert.Equal(3, await rows.CountAsync());
    }

    [Fact]
    public async Task Clicking_row_switches_detail_panel()
    {
        await Page.GotoAsync(GenerateParamReport("ParamRowSwitch.html"));
        await Page.Locator("details.feature").First.WaitForAsync();
        await ExpandFeatures();

        var group = Page.Locator("details.scenario-parameterized");
        await group.Locator("summary").First.ClickAsync();

        var rows = group.Locator("tbody tr");
        Assert.True(await rows.CountAsync() >= 3);

        await Expect(rows.Nth(0)).ToHaveClassAsync(new System.Text.RegularExpressions.Regex("row-active"));

        await rows.Nth(1).ClickAsync();
        await Expect(rows.Nth(1)).ToHaveClassAsync(new System.Text.RegularExpressions.Regex("row-active"));
        await Expect(rows.Nth(0)).Not.ToHaveClassAsync(new System.Text.RegularExpressions.Regex("row-active"));
    }

    [Fact]
    public async Task Clicking_row_switches_visible_detail_content()
    {
        await Page.GotoAsync(GenerateParamReport("ParamDetailSwitch.html"));
        await Page.Locator("details.feature").First.WaitForAsync();
        await ExpandFeatures();

        var group = Page.Locator("details.scenario-parameterized");
        await group.Locator("summary").First.ClickAsync();

        var panels = group.Locator(".param-detail-panel");
        Assert.True(await panels.CountAsync() >= 2);
        await Expect(panels.Nth(0)).ToBeVisibleAsync();
        await Expect(panels.Nth(1)).ToBeHiddenAsync();

        await group.Locator("tbody tr").Nth(1).ClickAsync();
        await Expect(panels.Nth(0)).ToBeHiddenAsync();
        await Expect(panels.Nth(1)).ToBeVisibleAsync();
    }

    // ── Status badges ──

    [Fact]
    public async Task Status_badges_show_correct_status_per_row()
    {
        await Page.GotoAsync(GenerateParamReport("ParamStatus.html", withFailure: true));
        await Page.Locator("details.feature").First.WaitForAsync();
        await ExpandFeatures();

        var group = Page.Locator("details.scenario-parameterized");
        var badges = group.Locator(".status-badge");
        Assert.True(await badges.CountAsync() >= 3);

        await Expect(badges.Nth(0)).ToHaveClassAsync(new System.Text.RegularExpressions.Regex("badge-pass"));
        await Expect(badges.Nth(1)).ToHaveClassAsync(new System.Text.RegularExpressions.Regex("badge-pass"));
        await Expect(badges.Nth(2)).ToHaveClassAsync(new System.Text.RegularExpressions.Regex("badge-fail"));
    }

    // ── Copy button ──

    [Fact]
    public async Task Copy_scenario_name_button_exists_on_parameterized_group()
    {
        await Page.GotoAsync(GenerateParamReport("ParamCopy.html"));
        await Page.Locator("details.feature").First.WaitForAsync();
        await ExpandFeatures();

        var group = Page.Locator("details.scenario-parameterized");
        var copyBtn = group.Locator(".copy-scenario-name");
        await Expect(copyBtn).ToBeVisibleAsync();

        var name = await copyBtn.GetAttributeAsync("data-scenario-name");
        Assert.Equal("Process", name);
    }

    [Fact]
    public async Task Copy_button_shows_checkmark_after_click()
    {
        await Page.GotoAsync(GenerateParamReport("ParamCopyCheck.html"));
        await Page.Locator("details.feature").First.WaitForAsync();
        await ExpandFeatures();

        await Context.GrantPermissionsAsync(["clipboard-read", "clipboard-write"]);

        var copyBtn = Page.Locator("details.scenario-parameterized .copy-scenario-name");
        await copyBtn.ClickAsync();

        await Expect(copyBtn).ToHaveTextAsync("\u2713", new() { Timeout = 2000 });
    }

    // ── Scenario link ──

    [Fact]
    public async Task Scenario_link_exists_on_parameterized_group()
    {
        await Page.GotoAsync(GenerateParamReport("ParamLink.html"));
        await Page.Locator("details.feature").First.WaitForAsync();
        await ExpandFeatures();

        var group = Page.Locator("details.scenario-parameterized");
        var link = group.Locator(".scenario-link");
        await Expect(link).ToBeVisibleAsync();

        var href = await link.GetAttributeAsync("href");
        Assert.Contains("#scenario-", href);
    }

    // ── Deep linking to a row ──

    [Fact]
    public async Task Deep_link_hash_opens_parameterized_group_and_selects_row()
    {
        var url = GenerateParamReport("ParamDeepLink.html");
        await Page.GotoAsync(url);
        await Page.Locator("details.feature").First.WaitForAsync();

        await ExpandFeatures();
        var group = Page.Locator("details.scenario-parameterized");
        await group.Locator("summary").First.ClickAsync();
        var rows = group.Locator("tbody tr");
        var secondRowId = await rows.Nth(1).GetAttributeAsync("data-scenario-id");
        Assert.False(string.IsNullOrEmpty(secondRowId));

        await Page.GotoAsync(url + "#" + secondRowId);
        await Page.WaitForFunctionAsync(
            "() => document.querySelector('details.scenario-parameterized')?.hasAttribute('open')",
            null, new() { Timeout = 5000, PollingInterval = 200 });
    }

    // ── Search highlighting ──

    [Fact]
    public async Task Search_highlights_matching_rows_in_parameterized_group()
    {
        await Page.GotoAsync(GenerateParamReport("ParamSearch.html"));
        await Page.Locator("details.feature").First.WaitForAsync();

        await FillSearchBar("region: US");

        await Page.WaitForFunctionAsync(
            "() => document.querySelectorAll('tr.row-search-match').length > 0",
            null, new() { Timeout = 3000, PollingInterval = 200 });

        var matched = await Page.Locator("tr.row-search-match").CountAsync();
        Assert.True(matched >= 1);
    }

    // ── Diagram section ──

    [Fact]
    public async Task Parameterized_group_has_diagram_section()
    {
        await Page.GotoAsync(GenerateParamReport("ParamDiagrams.html"));
        await Page.Locator("details.feature").First.WaitForAsync();
        await ExpandFeatures();

        var group = Page.Locator("details.scenario-parameterized");
        await group.Locator("summary").First.ClickAsync();
        await Expect(group.Locator("details.example-diagrams")).ToBeAttachedAsync();
    }

    // ── Diagram rendering on row click ──

    [Fact]
    public async Task First_row_diagram_renders_on_page_load()
    {
        await Page.GotoAsync(GenerateParamReport("ParamDiagramFirstRow.html"));
        await Page.Locator("details.feature").First.WaitForAsync();
        await ExpandAll();

        var group = Page.Locator("details.scenario-parameterized");
        await Page.EvaluateAsync("(el) => el.scrollIntoView(true)", await group.ElementHandleAsync());

        var firstDiagram = group.Locator("[id$='-diagram-0']");
        await Page.WaitForFunctionAsync("""
            () => {
                var d = document.querySelector("[id$='-diagram-0']");
                return d && d.offsetParent !== null && d.querySelectorAll('svg').length > 0;
            }
        """, null, new() { Timeout = 20000, PollingInterval = 200 });

        await Expect(firstDiagram).ToBeVisibleAsync();
    }

    [Fact]
    public async Task Clicking_second_row_renders_its_diagram()
    {
        await Page.GotoAsync(GenerateParamReport("ParamDiagramSecondRow.html"));
        await Page.Locator("details.feature").First.WaitForAsync();
        await ExpandAll();

        var group = Page.Locator("details.scenario-parameterized");
        await Page.EvaluateAsync("(el) => el.scrollIntoView(true)", await group.ElementHandleAsync());

        await Page.WaitForFunctionAsync("""
            () => {
                var d = document.querySelector("[id$='-diagram-0']");
                return d && d.offsetParent !== null && d.querySelectorAll('svg').length > 0;
            }
        """, null, new() { Timeout = 20000, PollingInterval = 200 });

        var rows = group.Locator("tbody tr");
        await rows.Nth(1).ClickAsync();

        await Page.WaitForFunctionAsync("""
            () => {
                var d = document.querySelector("[id$='-diagram-1']");
                return d && d.offsetParent !== null && d.querySelectorAll('svg').length > 0;
            }
        """, null, new() { Timeout = 20000, PollingInterval = 200 });

        await Expect(group.Locator("[id$='-diagram-0']")).ToBeHiddenAsync();
    }

    // ── Parameter name titleization ──

    [Fact]
    public async Task Parameter_column_headers_are_titleized()
    {
        await Page.GotoAsync(GenerateParamReport("ParamTitleize.html"));
        await Page.Locator("details.feature").First.WaitForAsync();
        await ExpandFeatures();

        var group = Page.Locator("details.scenario-parameterized");
        await group.Locator("summary").First.ClickAsync();

        var subHeaders = group.Locator("th.sub-header");
        Assert.True(await subHeaders.CountAsync() >= 2);
        Assert.Equal("Region", await subHeaders.Nth(0).TextContentAsync());
        Assert.Equal("Amount", await subHeaders.Nth(1).TextContentAsync());
    }

    // ── Single-instance parameterized scenario ──

    [Fact]
    public async Task Single_instance_parameterized_scenario_renders_as_table()
    {
        await Page.GotoAsync(GenerateSingleParamReport("ParamSingle.html"));
        await Page.Locator("details.feature").First.WaitForAsync();
        await ExpandFeatures();

        var group = Page.Locator("details.scenario-parameterized");
        await Expect(group).ToBeAttachedAsync();
        var rows = group.Locator("table.param-test-table tbody tr");
        Assert.Equal(1, await rows.CountAsync());
    }

    // ── Search row highlight uses box-shadow ──

    [Fact]
    public async Task Search_match_uses_box_shadow_not_outline()
    {
        await Page.GotoAsync(GenerateParamReport("ParamSearchStyle.html"));
        await Page.Locator("details.feature").First.WaitForAsync();

        await FillSearchBar("region: US");
        await Page.WaitForFunctionAsync(
            "() => document.querySelectorAll('tr.row-search-match').length > 0",
            null, new() { Timeout = 3000, PollingInterval = 200 });

        var matchedRow = Page.Locator("tr.row-search-match").First;
        var outline = await matchedRow.EvaluateAsync<string>(
            "e => window.getComputedStyle(e).outlineStyle");
        Assert.Equal("none", outline);
    }

    // ── Cross-feature parameterized group ──

    [Fact]
    public async Task Cross_feature_parameterized_groups_have_unique_prefixes()
    {
        var url = GenerateMultiFeatureParamReport("ParamCrossFeature.html");
        var htmlPath = new Uri(url).LocalPath;
        var html = await File.ReadAllTextAsync(htmlPath);

        var detailIds = System.Text.RegularExpressions.Regex.Matches(html, @"id=""(pgrp\d+-detail-\d+)""")
            .Cast<System.Text.RegularExpressions.Match>()
            .Select(m => m.Groups[1].Value)
            .ToArray();
        Assert.Equal(detailIds.Length, detailIds.Distinct().Count());

        var diagramIds = System.Text.RegularExpressions.Regex.Matches(html, @"id=""(pgrp\d+-diagram-\d+)""")
            .Cast<System.Text.RegularExpressions.Match>()
            .Select(m => m.Groups[1].Value)
            .ToArray();
        Assert.Equal(diagramIds.Length, diagramIds.Distinct().Count());
    }

    [Fact]
    public async Task Clicking_row_in_second_feature_does_not_hide_first_feature_panels()
    {
        await Page.GotoAsync(GenerateMultiFeatureParamReport("ParamCrossFeatureClick.html"));
        await Page.Locator("details.feature").First.WaitForAsync();
        await ExpandAll();

        var groups = Page.Locator("details.scenario-parameterized");
        Assert.True(await groups.CountAsync() >= 2);

        // Open both groups
        for (var i = 0; i < await groups.CountAsync(); i++)
        {
            var g = groups.Nth(i);
            if (await g.GetAttributeAsync("open") == null)
                await g.Locator("summary").First.ClickAsync();
        }

        var groupA = groups.Nth(0);
        var panelsA = groupA.Locator(".param-detail-panel");
        await Expect(panelsA.Nth(0)).ToBeVisibleAsync();

        // Click second row in group B
        var groupB = groups.Nth(1);
        await groupB.Locator("tbody tr").Nth(1).ClickAsync();

        // Group A panels should be unaffected
        await Expect(panelsA.Nth(0)).ToBeVisibleAsync();
        await Expect(groupB.Locator(".param-detail-panel").Nth(1)).ToBeVisibleAsync();
    }

    [Fact]
    public async Task Steps_and_diagrams_borders_are_left_aligned_in_parameterized_group()
    {
        await Page.GotoAsync(GenerateParamReport("ParamBorderAlign.html"));
        await Page.Locator("details.feature").First.WaitForAsync();
        await ExpandAll();

        var group = Page.Locator("details.scenario-parameterized");
        if (await group.GetAttributeAsync("open") == null)
            await group.Locator("summary").First.ClickAsync();

        var stepsDetails = group.Locator(".scenario-steps").First;
        var diagramsDetails = group.Locator(".example-diagrams").First;

        var stepsBox = await stepsDetails.BoundingBoxAsync();
        var diagramsBox = await diagramsDetails.BoundingBoxAsync();

        Assert.True(Math.Abs(stepsBox!.X - diagramsBox!.X) <= 2,
            $"Steps left ({stepsBox.X}px) and diagrams left ({diagramsBox.X}px) should be aligned");
    }

    [Fact]
    public async Task Clicking_third_row_renders_its_diagram()
    {
        await Page.GotoAsync(GenerateParamReport("ParamDiagramThirdRow.html"));
        await Page.Locator("details.feature").First.WaitForAsync();
        await ExpandAll();

        var group = Page.Locator("details.scenario-parameterized");
        await Page.EvaluateAsync("(el) => el.scrollIntoView(true)", await group.ElementHandleAsync());

        await Page.WaitForFunctionAsync("""
            () => {
                var d = document.querySelector("[id$='-diagram-0']");
                return d && d.offsetParent !== null && d.querySelectorAll('svg').length > 0;
            }
        """, null, new() { Timeout = 20000, PollingInterval = 200 });

        var rows = group.Locator("tbody tr");
        await rows.Nth(2).ClickAsync();

        await Page.WaitForFunctionAsync("""
            () => {
                var d = document.querySelector("[id$='-diagram-2']");
                return d && d.offsetParent !== null && d.querySelectorAll('svg').length > 0;
            }
        """, null, new() { Timeout = 20000, PollingInterval = 200 });

        await Expect(group.Locator("[id$='-diagram-2']")).ToBeVisibleAsync();
    }

    [Fact]
    public async Task Each_row_click_renders_a_diagram()
    {
        await Page.GotoAsync(GenerateParamReport("ParamDiagramDistinct.html"));
        await Page.Locator("details.feature").First.WaitForAsync();
        await ExpandAll();

        var group = Page.Locator("details.scenario-parameterized");
        await Page.EvaluateAsync("(el) => el.scrollIntoView(true)", await group.ElementHandleAsync());

        await Page.WaitForFunctionAsync("""
            () => {
                var d = document.querySelector("[id$='-diagram-0']");
                return d && d.offsetParent !== null && d.querySelectorAll('svg').length > 0;
            }
        """, null, new() { Timeout = 20000, PollingInterval = 200 });

        var rowCount = await group.Locator("tbody tr").CountAsync();

        for (var i = 0; i < rowCount; i++)
        {
            await group.Locator("tbody tr").Nth(i).ClickAsync();

            await Page.WaitForFunctionAsync("""
                (idx) => {
                    var d = document.querySelector("[id$='-diagram-" + idx + "']");
                    return d && d.offsetParent !== null && d.querySelectorAll('svg').length > 0;
                }
            """, i, new() { Timeout = 20000 });

            for (var j = 0; j < rowCount; j++)
            {
                var diag = group.Locator($"[id$='-diagram-{j}']");
                if (j == i)
                    await Expect(diag).ToBeVisibleAsync();
                else
                    await Expect(diag).ToBeHiddenAsync();
            }
        }
    }

    [Fact]
    public async Task Single_instance_parameterized_scenario_shows_parameter_values()
    {
        await Page.GotoAsync(GenerateSingleParamReport("ParamSingleValues.html"));
        await Page.Locator("details.feature").First.WaitForAsync();
        await ExpandFeatures();

        var group = Page.Locator("details.scenario-parameterized");
        await group.Locator("summary").First.ClickAsync();

        var monoCells = group.Locator("td.mono");
        var count = await monoCells.CountAsync();
        var texts = new List<string>();
        for (var i = 0; i < count; i++)
            texts.Add(await monoCells.Nth(i).TextContentAsync() ?? "");

        Assert.Contains("UK", texts);
        Assert.Contains("100", texts);
    }

    [Fact]
    public async Task Single_instance_parameterized_scenario_shows_titleized_headers()
    {
        await Page.GotoAsync(GenerateSingleParamReport("ParamSingleHeaders.html"));
        await Page.Locator("details.feature").First.WaitForAsync();
        await ExpandFeatures();

        var group = Page.Locator("details.scenario-parameterized");
        await group.Locator("summary").First.ClickAsync();

        var subHeaders = group.Locator("th.sub-header");
        var count = await subHeaders.CountAsync();
        var texts = new List<string>();
        for (var i = 0; i < count; i++)
            texts.Add(await subHeaders.Nth(i).TextContentAsync() ?? "");

        Assert.Contains("Region", texts);
        Assert.Contains("Amount", texts);
    }

    [Fact]
    public async Task Switching_back_to_first_row_still_shows_its_diagram()
    {
        await Page.GotoAsync(GenerateParamReport("ParamDiagramSwitchBack.html"));
        await Page.Locator("details.feature").First.WaitForAsync();
        await ExpandAll();

        var group = Page.Locator("details.scenario-parameterized");
        await Page.EvaluateAsync("(el) => el.scrollIntoView(true)", await group.ElementHandleAsync());

        await Page.WaitForFunctionAsync("""
            () => {
                var d = document.querySelector("[id$='-diagram-0']");
                return d && d.offsetParent !== null && d.querySelectorAll('svg').length > 0;
            }
        """, null, new() { Timeout = 20000, PollingInterval = 200 });

        // Click second row
        await group.Locator("tbody tr").Nth(1).ClickAsync();
        await Page.WaitForFunctionAsync("""
            () => {
                var d = document.querySelector("[id$='-diagram-1']");
                return d && d.offsetParent !== null && d.querySelectorAll('svg').length > 0;
            }
        """, null, new() { Timeout = 20000, PollingInterval = 200 });

        // Switch back to first row
        await group.Locator("tbody tr").Nth(0).ClickAsync();

        var firstDiagram = group.Locator("[id$='-diagram-0']");
        await Expect(firstDiagram).ToBeVisibleAsync();
        var svgCount = await firstDiagram.Locator("svg").CountAsync();
        Assert.True(svgCount > 0, "First row diagram SVG should still be present after switching back");
    }

    // ── Layout state sync across row switches ──

    [Fact]
    public async Task Collapsing_steps_section_syncs_to_other_rows()
    {
        await Page.GotoAsync(GenerateParamReport("ParamLayoutSync.html"));
        await Page.Locator("details.feature").First.WaitForAsync();
        await ExpandFeatures();

        var group = Page.Locator("details.scenario-parameterized");
        await group.Locator("summary").First.ClickAsync();

        // Verify Steps section is initially open on row 0
        var stepsDetails0 = group.Locator(".param-detail-panel").Nth(0).Locator("details.scenario-steps");
        await Expect(stepsDetails0).ToHaveAttributeAsync("open", "");

        // Collapse the Steps section on row 0
        await stepsDetails0.Locator("summary").ClickAsync();
        await Page.WaitForFunctionAsync("""
            () => {
                var panel = document.querySelector('.param-detail-panel:not([style*="display: none"], [style*="display:none"])');
                var d = panel && panel.querySelector('details.scenario-steps');
                return d && !d.open;
            }
        """, null, new() { Timeout = 5000, PollingInterval = 200 });

        // Click row 1
        await group.Locator("tbody tr").Nth(1).ClickAsync();

        // Steps section on row 1 should also be collapsed (synced from row 0)
        var stepsDetails1 = group.Locator(".param-detail-panel").Nth(1).Locator("details.scenario-steps");
        var isOpen = await Page.EvaluateAsync<bool>("""
            () => {
                var panels = document.querySelectorAll('.param-detail-panel');
                for (var p of panels) {
                    if (p.style.display !== 'none') {
                        var d = p.querySelector('details.scenario-steps');
                        return d ? d.open : true;
                    }
                }
                return true;
            }
        """);
        Assert.False(isOpen, "Steps section on row 1 should be collapsed after syncing from row 0");
    }

    // ── Row states: resting, hovered, selected ──

    /// <summary>One row per status, and a passed row first in the report's order (by name), which the
    /// report selects at load, so every status has an unselected row to hover.</summary>
    private string GenerateStatusRowsReport(string fileName)
    {
        (string Region, ExecutionResult Result)[] rows =
            [("AT", ExecutionResult.Passed), ("UK", ExecutionResult.Passed), ("DE", ExecutionResult.Failed), ("FR", ExecutionResult.Skipped), ("ES", ExecutionResult.Bypassed)];
        var scenarios = rows.Select((r, i) => new Scenario
        {
            Id = $"rs{i}", DisplayName = $"Process(region: {r.Region})", Result = r.Result, Duration = TimeSpan.FromSeconds(1),
            OutlineId = "Process",
            ExampleValues = new Dictionary<string, string> { ["region"] = r.Region },
            Steps = [new ScenarioStep { Keyword = "Given", Text = $"a valid region {r.Region}", Status = r.Result }]
        }).ToArray();

        var path = ReportGenerator.GenerateHtmlReport(
            scenarios.Select(s => new DiagramAsCode(s.Id, "", "")).ToArray(),
            [new Feature { DisplayName = "Payment Processing", Scenarios = scenarios }],
            DateTime.UtcNow, DateTime.UtcNow,
            null, Path.Combine(TempDir, fileName), "Row States Report", true,
            diagramFormat: DiagramFormat.PlantUml,
            plantUmlRendering: PlantUmlRendering.BrowserJs,
            groupParameterizedTests: true);

        File.Copy(path, Path.Combine(OutputDir, fileName), true);
        return new Uri(path).AbsoluteUri;
    }

    /// <summary>Every row carries a status class, whose rule used to beat the row's hover and selected
    /// tints: a hovered row gave no feedback in any report, a selected skipped row turned paler and a
    /// selected bypassed row did not change. Each status darkens its own tint under the pointer and further
    /// when selected; the selected tint holds while the pointer is still over the row, and after it leaves.</summary>
    [Theory]
    [InlineData("uk", "passed", "rgb(240, 255, 240)", "rgb(227, 250, 233)", "rgb(213, 245, 227)")]
    [InlineData("de", "failed", "rgb(255, 240, 240)", "rgb(253, 229, 228)", "rgb(250, 219, 216)")]
    [InlineData("fr", "skipped", "rgb(255, 248, 225)", "rgb(254, 241, 204)", "rgb(252, 233, 184)")]
    [InlineData("es", "bypassed", "rgb(240, 240, 255)", "rgb(232, 232, 253)", "rgb(223, 224, 251)")]
    public async Task A_row_darkens_its_status_tint_under_the_pointer_and_further_when_selected(string region, string status, string resting, string hovered, string selected)
    {
        await Page.GotoAsync(GenerateStatusRowsReport($"RowStates_{status}.html"));
        await Page.Locator("details.feature").First.WaitForAsync();
        await ExpandFeatures();

        var group = Page.Locator("details.scenario-parameterized");
        await group.Locator("summary").First.ClickAsync();
        var row = group.Locator($"table.param-test-table tbody tr#scenario-process-region-{region}");
        await Expect(row).ToHaveClassAsync(new System.Text.RegularExpressions.Regex($"\\brow-{status}\\b"));
        await Expect(row).Not.ToHaveClassAsync(new System.Text.RegularExpressions.Regex("\\brow-active\\b"));
        await Expect(row).ToHaveCSSAsync("background-color", resting);

        await row.HoverAsync();
        await Expect(row).ToHaveCSSAsync("background-color", hovered);

        await row.ClickAsync();
        await Expect(row).ToHaveClassAsync(new System.Text.RegularExpressions.Regex("\\brow-active\\b"));
        await Expect(row).ToHaveCSSAsync("background-color", selected);

        await Page.Mouse.MoveAsync(0, 0);
        await Expect(row).ToHaveCSSAsync("background-color", selected);
    }
}