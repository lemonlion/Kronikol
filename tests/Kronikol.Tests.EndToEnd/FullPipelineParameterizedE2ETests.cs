using System.Diagnostics;
using Microsoft.Playwright;

namespace Kronikol.Tests.EndToEnd;

/// <summary>
/// Fixture that runs the real test pipelines once before all full-pipeline E2E tests.
/// Generates reports for xUnit3, LightBDD, and ReqNRoll by running dotnet test.
/// </summary>
public sealed class FullPipelineFixture : IAsyncLifetime
{
    public static readonly string RepoRoot = Path.GetFullPath(
        Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "..", "..", "..", "..", ".."));

    private static readonly string XUnit3ProjectDir = Path.Combine(
        RepoRoot, "examples", "Example.Api", "tests", "Example.Api.Tests.Component.xUnit3");

    private static readonly string LightBDDProjectDir = Path.Combine(
        RepoRoot, "examples", "Example.Api", "tests", "Example.Api.Tests.Component.LightBDD.xUnit3");

    private static readonly string ReqNRollProjectDir = Path.Combine(
        RepoRoot, "examples", "Example.Api", "tests", "Example.Api.Tests.Component.ReqNRoll.xUnit3");

    public string XUnit3ReportPath => Path.Combine(XUnit3ProjectDir, "bin", "Debug", "net10.0", "Reports", "Specifications.html");
    public string LightBDDReportPath => Path.Combine(LightBDDProjectDir, "bin", "Debug", "net10.0", "Reports", "Specifications.html");
    public string ReqNRollReportPath => Path.Combine(ReqNRollProjectDir, "bin", "Debug", "net10.0", "Reports", "Specifications.html");

    public ValueTask InitializeAsync()
    {
        RunDotnetTest(XUnit3ProjectDir, "FullyQualifiedName~ParameterizedDiagnostic");
        // LightBDD needs all tests to run for report generation (report is written in scope teardown)
        RunDotnetTest(LightBDDProjectDir);
        RunDotnetTest(ReqNRollProjectDir);
        return ValueTask.CompletedTask;
    }

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;

    private static void RunDotnetTest(string projectDir, string? filter = null)
    {
        // No --no-restore: a clean CI runner has never restored the example projects, and
        // dotnet test would fail with NETSDK1004 (missing assets file) before running anything.
        var args = $"test \"{projectDir}\" -v q";
        if (filter != null)
            args += $" --filter \"{filter}\"";

        var psi = new ProcessStartInfo("dotnet", args)
        {
            WorkingDirectory = RepoRoot,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        using var process = Process.Start(psi)!;
        var stdout = process.StandardOutput.ReadToEnd();
        var stderr = process.StandardError.ReadToEnd();
        // Cold restore + build + run can far exceed the old 120s on a CI runner; a timeout
        // must throw rather than fall through to ExitCode (which throws obscurely on a live process).
        if (!process.WaitForExit(600_000))
        {
            try { process.Kill(entireProcessTree: true); } catch { }
            throw new InvalidOperationException($"dotnet test timed out after 10 minutes:\n{stdout}\n{stderr}");
        }

        if (process.ExitCode != 0)
        {
            throw new InvalidOperationException(
                $"dotnet test failed (exit {process.ExitCode}):\n{stdout}\n{stderr}");
        }
    }
}

[CollectionDefinition("FullPipeline")]
public class FullPipelineCollection : ICollectionFixture<FullPipelineFixture>, ICollectionFixture<PlaywrightFixture> { }

/// <summary>
/// Full-pipeline end-to-end tests that run real dotnet test projects, open the generated
/// Specifications.html reports in Playwright, and verify parameterized test rendering.
/// </summary>
[Collection("FullPipeline")]
public class FullPipelineParameterizedE2ETests : PlaywrightTestBase
{
    private readonly FullPipelineFixture _pipeline;

    public FullPipelineParameterizedE2ETests(PlaywrightFixture fixture, FullPipelineFixture pipeline) : base(fixture)
    {
        _pipeline = pipeline;
    }

    #region Helpers

    private async Task NavigateToReport(string reportPath)
    {
        Assert.True(File.Exists(reportPath), $"Report not found at: {reportPath}");
        File.Copy(reportPath, Path.Combine(OutputDir, Path.GetFileName(reportPath) + "_" + Guid.NewGuid().ToString("N")[..6] + ".html"), true);

        await Page.GotoAsync(new Uri(reportPath).AbsoluteUri);
        await Page.Locator("details.feature").First.WaitForAsync(new() { Timeout = 10000 });
    }

    private async Task ExpandAll()
    {
        await Page.Locator("button.collapse-expand-all", new() { HasTextString = "Expand All Features" }).ClickAsync();
        await Page.Locator("button.collapse-expand-all", new() { HasTextString = "Expand All Scenarios" }).ClickAsync();
    }

    private ILocator GetParameterizedGroup() => Page.Locator("details.scenario-parameterized");

    private async Task OpenParameterizedGroup(string? containsText = null)
    {
        var group = containsText != null
            ? Page.Locator("details.scenario-parameterized", new() { HasTextString = containsText })
            : GetParameterizedGroup();
        var details = group.First;
        // Only open if not already open (ExpandAll may have already opened it)
        var isOpen = await details.GetAttributeAsync("open");
        if (isOpen == null)
            await details.Locator("summary").First.ClickAsync();
    }

    private ILocator GetParamRows() => Page.Locator("table.param-test-table:not(.param-table-grouped) > tbody > tr[data-row-idx]");
    private ILocator GetParamHeaders() => Page.Locator("table.param-test-table:not(.param-table-grouped)").First.Locator("thead th");
    private ILocator GetGroupedParamRows() => Page.Locator("table.param-table-grouped > tbody > tr[data-row-idx]");

    #endregion

    // ═══════════════════════════════════════════════════════════════════
    // xUnit3 Full Pipeline Tests
    // ═══════════════════════════════════════════════════════════════════

    [Fact]
    public async Task XUnit3_pipeline_generates_report_with_parameterized_group()
    {
        await NavigateToReport(_pipeline.XUnit3ReportPath);
        await ExpandAll();

        await Expect(GetParameterizedGroup().First).ToBeVisibleAsync();
    }

    [Fact]
    public async Task XUnit3_pipeline_renders_three_data_rows()
    {
        await NavigateToReport(_pipeline.XUnit3ReportPath);
        await ExpandAll();
        await OpenParameterizedGroup();

        Assert.Equal(3, await GetParamRows().CountAsync());
    }

    [Fact]
    public async Task XUnit3_pipeline_renders_recipe_name_column_with_correct_values()
    {
        await NavigateToReport(_pipeline.XUnit3ReportPath);
        await ExpandAll();
        await OpenParameterizedGroup();

        var rows = GetParamRows();
        var row1Text = await rows.Nth(0).InnerTextAsync();
        var row2Text = await rows.Nth(1).InnerTextAsync();
        var row3Text = await rows.Nth(2).InnerTextAsync();

        Assert.Contains("Classic", row1Text);
        Assert.Contains("Rustic Wholesome", row2Text);
        Assert.Contains("Spiced Deluxe", row3Text);
    }

    [Fact]
    public async Task XUnit3_pipeline_renders_ingredient_data_for_Classic_recipe()
    {
        await NavigateToReport(_pipeline.XUnit3ReportPath);
        await ExpandAll();
        await OpenParameterizedGroup();

        var firstRow = GetParamRows().First;
        var rowHtml = await firstRow.InnerHTMLAsync();

        Assert.Contains("Plain Flour", rowHtml);
        Assert.Contains("Granny Smith", rowHtml);
        Assert.Contains("Ceylon", rowHtml);
    }

    [Fact]
    public async Task XUnit3_pipeline_renders_baking_data_for_Classic_recipe()
    {
        await NavigateToReport(_pipeline.XUnit3ReportPath);
        await ExpandAll();
        await OpenParameterizedGroup();

        var firstRow = GetParamRows().First;
        var rowHtml = await firstRow.InnerHTMLAsync();

        Assert.Contains("180", rowHtml);
        Assert.Contains("25", rowHtml);
        Assert.Contains("Standard", rowHtml);
    }

    [Fact]
    public async Task XUnit3_pipeline_renders_toppings_data_for_Classic_recipe()
    {
        await NavigateToReport(_pipeline.XUnit3ReportPath);
        await ExpandAll();
        await OpenParameterizedGroup();

        var firstRow = GetParamRows().First;
        var rowHtml = await firstRow.InnerHTMLAsync();

        // Toppings: Streusel, Icing Glaze
        Assert.Contains("Streusel", rowHtml);
        Assert.Contains("Icing Glaze", rowHtml);
    }

    [Fact]
    public async Task XUnit3_pipeline_renders_Rustic_Wholesome_recipe_data()
    {
        await NavigateToReport(_pipeline.XUnit3ReportPath);
        await ExpandAll();
        await OpenParameterizedGroup();

        var secondRow = GetParamRows().Nth(1);
        var rowHtml = await secondRow.InnerHTMLAsync();

        Assert.Contains("Whole Wheat", rowHtml);
        Assert.Contains("Honeycrisp", rowHtml);
        Assert.Contains("Cassia", rowHtml);
        Assert.Contains("175", rowHtml);
        Assert.Contains("30", rowHtml);
        Assert.Contains("Cast Iron", rowHtml);
        Assert.Contains("Brown Sugar Crumb", rowHtml);
    }

    [Fact]
    public async Task XUnit3_pipeline_renders_expected_batch_data()
    {
        await NavigateToReport(_pipeline.XUnit3ReportPath);
        await ExpandAll();
        await OpenParameterizedGroup();

        var firstRow = GetParamRows().First;
        var rowHtml = await firstRow.InnerHTMLAsync();

        // MuffinBatchExpectation(5, 2, true)
        Assert.Contains("ExpectedIngredientCount", rowHtml);
        Assert.Contains("5", rowHtml);
        Assert.Contains("ExpectedToppingCount", rowHtml);
        Assert.Contains("HasBakingInfo", rowHtml);
        Assert.Contains("True", rowHtml);
    }

    [Fact]
    public async Task XUnit3_pipeline_row_click_activates_row()
    {
        await NavigateToReport(_pipeline.XUnit3ReportPath);
        await ExpandAll();
        await OpenParameterizedGroup();

        var secondRow = GetParamRows().Nth(1);
        await secondRow.ClickAsync();

        await Expect(secondRow).ToHaveClassAsync(new System.Text.RegularExpressions.Regex("row-active"));
    }

    // ═══════════════════════════════════════════════════════════════════
    // LightBDD Full Pipeline Tests
    // ═══════════════════════════════════════════════════════════════════

    [Fact]
    public async Task LightBDD_pipeline_generates_report_with_parameterized_group()
    {
        await NavigateToReport(_pipeline.LightBDDReportPath);
        await ExpandAll();

        await Expect(GetParameterizedGroup().First).ToBeVisibleAsync();
    }

    [Fact]
    public async Task LightBDD_pipeline_renders_two_data_rows()
    {
        await NavigateToReport(_pipeline.LightBDDReportPath);
        await ExpandAll();
        await OpenParameterizedGroup();

        Assert.Equal(3, await GetParamRows().CountAsync());
    }

    [Fact]
    public async Task LightBDD_pipeline_renders_recipe_names()
    {
        await NavigateToReport(_pipeline.LightBDDReportPath);
        await ExpandAll();
        await OpenParameterizedGroup();

        var tableHtml = await GetParameterizedGroup().First.Locator("table.param-test-table").First.InnerHTMLAsync();

        Assert.Contains("Classic", tableHtml);
        Assert.Contains("Rustic Wholesome", tableHtml);
        Assert.Contains("Spiced Deluxe", tableHtml);
    }

    [Fact]
    public async Task LightBDD_pipeline_renders_ingredient_data_for_Classic()
    {
        await NavigateToReport(_pipeline.LightBDDReportPath);
        await ExpandAll();
        await OpenParameterizedGroup();

        var firstRow = GetParamRows().First;
        var rowHtml = await firstRow.InnerHTMLAsync();

        Assert.Contains("Plain Flour", rowHtml);
        Assert.Contains("Granny Smith", rowHtml);
        Assert.Contains("Ceylon", rowHtml);
    }

    [Fact]
    public async Task LightBDD_pipeline_renders_baking_data_for_Classic()
    {
        await NavigateToReport(_pipeline.LightBDDReportPath);
        await ExpandAll();
        await OpenParameterizedGroup();

        var firstRow = GetParamRows().First;
        var rowHtml = await firstRow.InnerHTMLAsync();

        Assert.Contains("180", rowHtml);
        Assert.Contains("25", rowHtml);
        Assert.Contains("Standard", rowHtml);
    }

    [Fact]
    public async Task LightBDD_pipeline_renders_toppings_data_for_Classic()
    {
        await NavigateToReport(_pipeline.LightBDDReportPath);
        await ExpandAll();
        await OpenParameterizedGroup();

        // With raw value capture, toppings should render as individual items NOT "List<ToppingData>"
        var tableHtml = await GetParameterizedGroup().First.Locator("table.param-test-table").First.InnerHTMLAsync();

        Assert.Contains("Streusel", tableHtml);
        Assert.Contains("Icing Glaze", tableHtml);
        Assert.DoesNotContain("List&lt;ToppingData&gt;", tableHtml);
    }

    [Fact]
    public async Task LightBDD_pipeline_renders_toppings_for_all_recipes()
    {
        await NavigateToReport(_pipeline.LightBDDReportPath);
        await ExpandAll();
        await OpenParameterizedGroup();

        var tableHtml = await GetParameterizedGroup().First.Locator("table.param-test-table").First.InnerHTMLAsync();

        // Classic toppings
        Assert.Contains("Streusel", tableHtml);
        Assert.Contains("Icing Glaze", tableHtml);

        // Rustic Wholesome toppings
        Assert.Contains("Brown Sugar Crumb", tableHtml);
        Assert.Contains("Maple Drizzle", tableHtml);

        // Spiced Deluxe toppings
        Assert.Contains("Cinnamon Sugar", tableHtml);
        Assert.Contains("Cream Cheese Swirl", tableHtml);
    }

    [Fact]
    public async Task LightBDD_pipeline_renders_Rustic_Wholesome_recipe_data()
    {
        await NavigateToReport(_pipeline.LightBDDReportPath);
        await ExpandAll();
        await OpenParameterizedGroup();

        var tableHtml = await GetParameterizedGroup().First.Locator("table.param-test-table").First.InnerHTMLAsync();

        Assert.Contains("Whole Wheat", tableHtml);
        Assert.Contains("Honeycrisp", tableHtml);
        Assert.Contains("Cassia", tableHtml);
        Assert.Contains("175", tableHtml);
        Assert.Contains("30", tableHtml);
        Assert.Contains("Cast Iron", tableHtml);
        Assert.Contains("Brown Sugar Crumb", tableHtml);
        Assert.Contains("Maple Drizzle", tableHtml);
    }

    [Fact]
    public async Task LightBDD_pipeline_renders_expected_batch_data()
    {
        await NavigateToReport(_pipeline.LightBDDReportPath);
        await ExpandAll();
        await OpenParameterizedGroup();

        var tableHtml = await GetParameterizedGroup().First.Locator("table.param-test-table").First.InnerHTMLAsync();

        // MuffinBatchExpectation { ExpectedIngredientCount = 5, ExpectedToppingCount = 2, HasBakingInfo = true }
        Assert.Contains("ExpectedIngredientCount", tableHtml);
        Assert.Contains("5", tableHtml);
    }

    [Fact]
    public async Task LightBDD_pipeline_row_click_activates_row()
    {
        await NavigateToReport(_pipeline.LightBDDReportPath);
        await ExpandAll();
        await OpenParameterizedGroup();

        var firstRow = GetParamRows().First;
        await firstRow.ClickAsync();

        await Expect(firstRow).ToHaveClassAsync(new System.Text.RegularExpressions.Regex("row-active"));
    }

    // ═══════════════════════════════════════════════════════════════════
    // ReqNRoll Full Pipeline Tests
    // ═══════════════════════════════════════════════════════════════════

    [Fact]
    public async Task ReqNRoll_pipeline_generates_report_with_parameterized_group()
    {
        await NavigateToReport(_pipeline.ReqNRollReportPath);
        await ExpandAll();

        await Expect(GetParameterizedGroup().First).ToBeVisibleAsync();
    }

    [Fact]
    public async Task ReqNRoll_pipeline_renders_scenario_description()
    {
        await NavigateToReport(_pipeline.ReqNRollReportPath);
        await ExpandAll();

        var description = Page.Locator(".scenario-description",
            new() { HasTextString = "Stale muffins must never reach a caller" }).First;
        await Expect(description).ToBeVisibleAsync();

        // The feature file indents description lines; the report must show them dedented
        var text = await description.TextContentAsync();
        Assert.NotNull(text);
        Assert.StartsWith("Every diagnostic batch is baked to order from the complete ingredient list.", text);
    }

    [Fact]
    public async Task ReqNRoll_pipeline_renders_outline_description_inside_parameterized_group()
    {
        await NavigateToReport(_pipeline.ReqNRollReportPath);
        await ExpandAll();
        await OpenParameterizedGroup("Different muffin recipes");

        var groupHtml = await GetParameterizedGroup().First.InnerHTMLAsync();
        Assert.Contains("Covers speciality flour, baking profile and topping permutations.", groupHtml);
    }

    [Fact]
    public async Task ReqNRoll_pipeline_renders_three_data_rows()
    {
        await NavigateToReport(_pipeline.ReqNRollReportPath);
        await ExpandAll();
        await OpenParameterizedGroup("Different muffin recipes");

        Assert.Equal(3, await GetParamRows().CountAsync());
    }

    [Fact]
    public async Task ReqNRoll_pipeline_renders_all_scalar_column_headers()
    {
        await NavigateToReport(_pipeline.ReqNRollReportPath);
        await ExpandAll();
        await OpenParameterizedGroup("Different muffin recipes");

        var headers = GetParamHeaders();
        var headerTexts = new List<string>();
        for (var i = 0; i < await headers.CountAsync(); i++)
            headerTexts.Add(await headers.Nth(i).TextContentAsync() ?? "");

        // After ExampleValueGrouper, tables nest under "Recipe", scalars group under "Expected"
        Assert.Contains(headerTexts, h => h.Contains("Recipe Name", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(headerTexts, h => h.Contains("Recipe", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(headerTexts, h => h.Contains("Expected", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task ReqNRoll_pipeline_Classic_row_has_correct_data()
    {
        await NavigateToReport(_pipeline.ReqNRollReportPath);
        await ExpandAll();
        await OpenParameterizedGroup("Different muffin recipes");

        // Row order is non-deterministic; find the row containing "Classic"
        var tableHtml = await GetParameterizedGroup().First.Locator("table.param-test-table").First.InnerHTMLAsync();

        Assert.Contains("Classic", tableHtml);
        Assert.Contains("Plain Flour", tableHtml);
        Assert.Contains("Granny Smith", tableHtml);
        Assert.Contains("Ceylon", tableHtml);
        Assert.Contains("180", tableHtml);
        Assert.Contains("25", tableHtml);
        Assert.Contains("Standard", tableHtml);
        Assert.Contains("Streusel", tableHtml);
        Assert.Contains("Icing Glaze", tableHtml);
    }

    [Fact]
    public async Task ReqNRoll_pipeline_Rustic_Wholesome_row_has_correct_data()
    {
        await NavigateToReport(_pipeline.ReqNRollReportPath);
        await ExpandAll();
        await OpenParameterizedGroup("Different muffin recipes");

        var tableHtml = await GetParameterizedGroup().First.Locator("table.param-test-table").First.InnerHTMLAsync();

        Assert.Contains("Rustic Wholesome", tableHtml);
        Assert.Contains("Whole Wheat", tableHtml);
        Assert.Contains("Honeycrisp", tableHtml);
        Assert.Contains("Cassia", tableHtml);
        Assert.Contains("175", tableHtml);
        Assert.Contains("30", tableHtml);
        Assert.Contains("Cast Iron", tableHtml);
        Assert.Contains("Brown Sugar Crumb", tableHtml);
        Assert.Contains("Maple Drizzle", tableHtml);
    }

    [Fact]
    public async Task ReqNRoll_pipeline_Spiced_Deluxe_row_has_correct_data()
    {
        await NavigateToReport(_pipeline.ReqNRollReportPath);
        await ExpandAll();
        await OpenParameterizedGroup("Different muffin recipes");

        var tableHtml = await GetParameterizedGroup().First.Locator("table.param-test-table").First.InnerHTMLAsync();

        Assert.Contains("Spiced Deluxe", tableHtml);
        Assert.Contains("Almond", tableHtml);
        Assert.Contains("Pink Lady", tableHtml);
        Assert.Contains("Saigon", tableHtml);
        Assert.Contains("190", tableHtml);
        Assert.Contains("20", tableHtml);
        Assert.Contains("Silicone", tableHtml);
        Assert.Contains("Cinnamon Sugar", tableHtml);
        Assert.Contains("Cream Cheese Swirl", tableHtml);
    }

    [Fact]
    public async Task ReqNRoll_pipeline_has_subtable_and_expandable_cells()
    {
        await NavigateToReport(_pipeline.ReqNRollReportPath);
        await ExpandAll();
        await OpenParameterizedGroup("Different muffin recipes");

        var firstRow = GetGroupedParamRows().First;
        var tableHtml = await firstRow.InnerHTMLAsync();

        // Ingredients column renders as a sub-table (R3)
        Assert.Contains("cell-subtable", tableHtml);
        // Toppings column renders as an expandable (R4)
        Assert.Contains("param-expand", tableHtml);
    }

    [Fact]
    public async Task ReqNRoll_pipeline_renders_examples_block_bands()
    {
        await NavigateToReport(_pipeline.ReqNRollReportPath);
        await ExpandAll();
        await OpenParameterizedGroup("Different muffin recipes");

        // Muffins.feature splits its outline into two named Examples: blocks.
        var bands = GetParameterizedGroup().First
            .Locator("table.param-test-table:not(.param-table-grouped) tr.examples-block-row");
        Assert.Equal(2, await bands.CountAsync());

        await Expect(bands.Nth(0)).ToBeVisibleAsync();
        var firstBand = await bands.Nth(0).TextContentAsync() ?? "";
        Assert.Contains("Examples: a classic bake", firstBand);
        Assert.Contains("The everyday plain-flour recipe.", firstBand);
        Assert.Contains("1/1 passed", firstBand);

        await Expect(bands.Nth(1)).ToBeVisibleAsync();
        var secondBand = await bands.Nth(1).TextContentAsync() ?? "";
        Assert.Contains("Examples: speciality flours", secondBand);
        Assert.Contains("2/2 passed", secondBand);
    }

    [Fact]
    public async Task ReqNRoll_pipeline_row_click_shows_steps()
    {
        await NavigateToReport(_pipeline.ReqNRollReportPath);
        await ExpandAll();
        await OpenParameterizedGroup("Different muffin recipes");

        // Find and click the row containing "Rustic Wholesome" regardless of position
        var rows = GetParamRows();
        var rowCount = await rows.CountAsync();
        ILocator? targetRow = null;
        int targetIdx = -1;
        for (var i = 0; i < rowCount; i++)
        {
            var text = await rows.Nth(i).TextContentAsync() ?? "";
            if (text.Contains("Rustic Wholesome"))
            {
                targetRow = rows.Nth(i);
                targetIdx = i;
                break;
            }
        }
        Assert.NotNull(targetRow);

        await targetRow.ClickAsync();
        await Expect(targetRow).ToHaveClassAsync(new System.Text.RegularExpressions.Regex("row-active"));

        // Detail panel should show step text from the Rustic Wholesome scenario
        var detailPanels = GetParameterizedGroup().First.Locator(".param-detail-panel");
        var visiblePanel = detailPanels.Nth(targetIdx);
        await Expect(visiblePanel).ToBeVisibleAsync();
        var panelText = await visiblePanel.InnerTextAsync();
        Assert.Contains("Rustic Wholesome", panelText);
    }
}
