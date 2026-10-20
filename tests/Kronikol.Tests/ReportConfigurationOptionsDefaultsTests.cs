namespace Kronikol.Tests;

public class ReportConfigurationOptionsDefaultsTests
{
    [Fact]
    public void CompressTestRunReport_defaults_to_true_from_5()
    {
        // From 5.0.0 the data file is TestRunReport.krkl unless a run asks for the JSON (plans/REPORT_FORMAT_PLAN.md 4.3, 6.2).
        Assert.True(new ReportConfigurationOptions().CompressTestRunReport);
    }

    [Fact]
    public void GenerateTestRunReportSchema_defaults_to_false_from_5()
    {
        // Nothing in Kronikol reads the schema file at runtime; from 5.0.0 a run writes it only when asked, for a consumer
        // who validates the data file it decodes (plans/REPORT_FORMAT_PLAN.md Q7).
        Assert.False(new ReportConfigurationOptions().GenerateTestRunReportSchema);
    }

    [Fact]
    public void InternalFlowTracking_defaults_to_true()
    {
        var options = new ReportConfigurationOptions();
        Assert.True(options.InternalFlowTracking);
    }

    [Fact]
    public void WholeTestFlowVisualization_defaults_to_Both()
    {
        var options = new ReportConfigurationOptions();
        Assert.Equal(WholeTestFlowVisualization.Both, options.WholeTestFlowVisualization);
    }

    [Fact]
    public void GenerateComponentDiagram_defaults_to_true()
    {
        var options = new ReportConfigurationOptions();
        Assert.True(options.GenerateComponentDiagram);
    }

    // 4.0.0 (plans/V4_PLAN.md R8): notes start as YAML; Json was the default through 3.x.
    [Fact]
    public void NotePayloadFormat_defaults_to_Yaml()
    {
        var options = new ReportConfigurationOptions();
        Assert.Equal(Kronikol.Reports.NotePayloadFormat.Yaml, options.NotePayloadFormat);
    }

    [Fact]
    public void Toggle_default_groups_are_non_null_with_every_property_unset()
    {
        var options = new ReportConfigurationOptions();
        // Both groups initialized non-null so user code composes without ??= noise …
        Assert.NotNull(options.TestRunReportToggleDefaults);
        Assert.NotNull(options.SpecificationsToggleDefaults);
        // … and every property starts null (= inherit).
        foreach (var group in new[] { options.TestRunReportToggleDefaults, options.SpecificationsToggleDefaults })
        foreach (var property in typeof(Kronikol.Reports.ReportToggleDefaults).GetProperties())
            Assert.Null(property.GetValue(group));
    }

    [Fact]
    public void History_and_diagnostics_are_tabs_of_the_report_by_default_and_out_of_its_features()
    {
        // plans/HISTORY_AND_DIAGNOSTICS_MOVE_PLAN.md as 5.0.0 moves it (plans/OPTION_C_PLAN.md S3, the owner's call):
        // the page is written by default, the History and Diagnostics tabs are on (4.6.0 to 4.x: off), the scenarios'
        // own history stays an opt-in, and history still reaches the HTML unless told not to.
        var options = new ReportConfigurationOptions();
        Assert.True(options.GenerateLabsReport);
        Assert.False(options.ShowScenarioHistory);
        Assert.True(options.ShowHistorySection);
        Assert.True(options.ShowReportDiagnosticsSection);
        Assert.True(options.EmbedHistoryInReport);
    }

    [Fact]
    public void Browser_render_options_default_to_four_workers_64MB_cache_and_12000px_fragments()
    {
        var options = new ReportConfigurationOptions();
        Assert.Equal(4, options.BrowserRenderWorkers);
        Assert.Equal(64, options.BrowserRenderCacheMegabytes);
        Assert.Equal(12000, options.BrowserFragmentMaxHeight);
        Assert.Equal(Kronikol.Constants.TrackingDefaults.BrowserRenderWorkers, options.BrowserRenderWorkers);
        Assert.Equal(Kronikol.Constants.TrackingDefaults.BrowserRenderCacheMegabytes, options.BrowserRenderCacheMegabytes);
        Assert.Equal(Kronikol.Constants.TrackingDefaults.BrowserFragmentMaxHeight, options.BrowserFragmentMaxHeight);
    }
}
