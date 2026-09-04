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

    [Fact]
    public void NotePayloadFormat_defaults_to_Json()
    {
        var options = new ReportConfigurationOptions();
        Assert.Equal(Kronikol.Reports.NotePayloadFormat.Json, options.NotePayloadFormat);
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
