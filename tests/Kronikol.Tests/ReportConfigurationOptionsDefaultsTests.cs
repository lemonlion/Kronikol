namespace Kronikol.Tests;

public class ReportConfigurationOptionsDefaultsTests
{
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
    public void History_and_diagnostics_are_on_the_labs_page_by_default_and_out_of_the_report()
    {
        // plans/HISTORY_AND_DIAGNOSTICS_MOVE_PLAN.md: the page is written by default, every view the report can carry
        // is an opt-in that defaults off, and history still reaches the HTML (the page) unless told not to.
        var options = new ReportConfigurationOptions();
        Assert.True(options.GenerateLabsReport);
        Assert.False(options.ShowScenarioHistory);
        Assert.False(options.ShowHistorySection);
        Assert.False(options.ShowReportDiagnosticsSection);
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
