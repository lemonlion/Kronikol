using Example.Api.Tests.Integration.Helpers;

namespace Example.Api.Tests.Integration.Tests;

/// <summary>
/// End-to-end tests verifying the full pipeline from TUnit [Arguments]/[MethodDataSource]
/// attributes → TUnit adapter → ExampleValues/ExampleRawValues → HTML report rendering.
/// </summary>
[Collection("SequentialTests")]
public class TUnitParameterizedRenderingTests
{
    private static Task<TestProjectRunResult>? _cachedRun;
    private static readonly SemaphoreSlim Lock = new(1);

    /// <summary>
    /// Runs the TUnit project once and caches the result for all tests in this class.
    /// </summary>
    private static async Task<TestProjectRunResult> GetRunResultAsync()
    {
        if (_cachedRun is not null) return await _cachedRun;

        await Lock.WaitAsync();
        try
        {
            _cachedRun ??= TestProjectRunner.RunAsync(TestProjects.TUnit, new Dictionary<string, string>
            {
                ["KRONIKOL_SEPARATE_SETUP"] = "true",
                ["KRONIKOL_SPECIFICATIONS_TITLE"] = "Dessert Provider Specifications"
            });
            return await _cachedRun;
        }
        finally
        {
            Lock.Release();
        }
    }

    [Fact]
    public async Task TUnit_project_generates_all_report_files()
    {
        var result = await GetRunResultAsync();
        Assert.True(result.Success, $"TUnit project failed:\n{result.StandardError}\n{result.StandardOutput}");

        var reports = ReportParser.GetReportFiles(result.ReportsFolderPath);
        Assert.NotNull(reports.SpecificationsHtml);
        Assert.NotNull(reports.TestRunReportHtml);
        Assert.NotNull(reports.SpecificationsYaml);
    }

    [Fact]
    public async Task R1_scalar_columns_renders_region_amount_expedited()
    {
        var result = await GetRunResultAsync();
        Assert.True(result.Success, $"TUnit failed:\n{result.StandardError}");

        var reports = ReportParser.GetReportFiles(result.ReportsFolderPath);
        Assert.NotNull(reports.SpecificationsHtml);

        var groups = await ReportParser.ExtractParameterizedGroupsAsync(reports.SpecificationsHtml);

        var r1 = groups.FirstOrDefault(g => g.ScenarioName.Contains("Process order in region"));
        Assert.NotNull(r1);
        Assert.Contains("Region", r1.ColumnHeaders);
        Assert.Contains("Amount", r1.ColumnHeaders);
        Assert.Contains("Expedited", r1.ColumnHeaders);
        Assert.Equal(3, r1.RowCount);
        Assert.False(r1.HasSubTables, "R1 scalar columns should not have subtables");
        Assert.False(r1.HasExpandables, "R1 scalar columns should not have expandables");
    }

    [Fact]
    public async Task R2_flattened_object_renders_order_scenario_properties_as_columns()
    {
        var result = await GetRunResultAsync();
        Assert.True(result.Success, $"TUnit failed:\n{result.StandardError}");

        var reports = ReportParser.GetReportFiles(result.ReportsFolderPath);
        Assert.NotNull(reports.SpecificationsHtml);

        var groups = await ReportParser.ExtractParameterizedGroupsAsync(reports.SpecificationsHtml);

        var r2 = groups.FirstOrDefault(g => g.ScenarioName.Contains("Validate order scenario"));
        Assert.NotNull(r2);
        // R2 flattens the single OrderScenario record into individual columns
        Assert.Contains("Region", r2.ColumnHeaders);
        Assert.Contains("Amount", r2.ColumnHeaders);
        Assert.Contains("Currency", r2.ColumnHeaders);
        Assert.Equal(3, r2.RowCount);
        Assert.False(r2.HasSubTables, "R2 flattened should not have subtables");
        Assert.False(r2.HasExpandables, "R2 flattened should not have expandables");
    }

    [Fact]
    public async Task R3_subtable_renders_shipping_address_with_nested_table()
    {
        var result = await GetRunResultAsync();
        Assert.True(result.Success, $"TUnit failed:\n{result.StandardError}");

        var reports = ReportParser.GetReportFiles(result.ReportsFolderPath);
        Assert.NotNull(reports.SpecificationsHtml);

        var groups = await ReportParser.ExtractParameterizedGroupsAsync(reports.SpecificationsHtml);

        var r3 = groups.FirstOrDefault(g => g.ScenarioName.Contains("Ship to address"));
        Assert.NotNull(r3);
        Assert.Contains("Order Id", r3.ColumnHeaders);
        Assert.Contains("Address", r3.ColumnHeaders);
        Assert.Equal(2, r3.RowCount);
        Assert.True(r3.HasSubTables, "R3 should render Address as a sub-table");
    }

    [Fact]
    public async Task R4_expandable_renders_customer_order_with_expand_details()
    {
        var result = await GetRunResultAsync();
        Assert.True(result.Success, $"TUnit failed:\n{result.StandardError}");

        var reports = ReportParser.GetReportFiles(result.ReportsFolderPath);
        Assert.NotNull(reports.SpecificationsHtml);

        var groups = await ReportParser.ExtractParameterizedGroupsAsync(reports.SpecificationsHtml);

        var r4 = groups.FirstOrDefault(g => g.ScenarioName.Contains("Enroll customer"));
        Assert.NotNull(r4);
        Assert.Contains("Tier", r4.ColumnHeaders);
        Assert.Contains("Customer", r4.ColumnHeaders);
        Assert.Equal(2, r4.RowCount);
        Assert.True(r4.HasExpandables, "R4 should render CustomerOrder as an expandable");
    }

    [Fact]
    public async Task Report_contains_all_four_parameterized_groups()
    {
        var result = await GetRunResultAsync();
        Assert.True(result.Success, $"TUnit failed:\n{result.StandardError}");

        var reports = ReportParser.GetReportFiles(result.ReportsFolderPath);
        Assert.NotNull(reports.SpecificationsHtml);

        var groups = await ReportParser.ExtractParameterizedGroupsAsync(reports.SpecificationsHtml);

        // All 4 parameterized test methods should produce groups
        Assert.True(groups.Length >= 4, $"Expected at least 4 parameterized groups, got {groups.Length}");
    }

    [Fact]
    public async Task Report_also_contains_non_parameterized_cake_scenarios()
    {
        var result = await GetRunResultAsync();
        Assert.True(result.Success, $"TUnit failed:\n{result.StandardError}");

        var reports = ReportParser.GetReportFiles(result.ReportsFolderPath);
        Assert.NotNull(reports.TestRunReportHtml);

        var scenarios = await ReportParser.ExtractScenariosAsync(reports.TestRunReportHtml);
        Assert.Contains(scenarios, s => s.Name.Contains("Cake", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task Both_reports_contain_parameterized_groups()
    {
        var result = await GetRunResultAsync();
        Assert.True(result.Success, $"TUnit failed:\n{result.StandardError}");

        var reports = ReportParser.GetReportFiles(result.ReportsFolderPath);
        Assert.NotNull(reports.SpecificationsHtml);
        Assert.NotNull(reports.TestRunReportHtml);

        var specGroups = await ReportParser.ExtractParameterizedGroupsAsync(reports.SpecificationsHtml);
        var runGroups = await ReportParser.ExtractParameterizedGroupsAsync(reports.TestRunReportHtml);

        Assert.True(specGroups.Length >= 4, $"Specifications should have >= 4 groups, got {specGroups.Length}");
        Assert.True(runGroups.Length >= 4, $"TestRunReport should have >= 4 groups, got {runGroups.Length}");
    }
}
