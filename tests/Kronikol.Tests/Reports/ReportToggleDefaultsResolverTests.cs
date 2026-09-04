using Kronikol.Reports;

namespace Kronikol.Tests.Reports;

/// <summary>
/// The toggle-defaults resolution chain (TOGGLE_DEFAULTS_PLAN.md §3.2), modelled on
/// <c>SqlResponseDetailResolverTests</c>: built-in passthrough, TestRunReport override,
/// Specifications inherit-unless-overridden, per-property independence, the flat
/// <c>NotePayloadFormat</c> precedence, and the undefined-enum-cast guard.
/// </summary>
public class ReportToggleDefaultsResolverTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Unset_config_resolves_to_the_built_ins_for_both_reports(bool specifications)
    {
        var resolved = ReportToggleDefaultsResolver.Resolve(new ReportConfigurationOptions(), specifications);
        Assert.Equal(ResolvedToggleDefaults.BuiltIn, resolved);
    }

    [Fact]
    public void Built_in_defaults_are_todays_hard_coded_literals()
    {
        var b = ResolvedToggleDefaults.BuiltIn;
        Assert.Equal(ReportDetailsState.Truncated, b.Details);
        Assert.Equal(TruncateLineCount.Lines40, b.TruncateLines);
        Assert.True(b.HeadersShown);
        Assert.False(b.AssertionsShown);
        Assert.True(b.StepsShown);
        Assert.True(b.DatabasesShown);
        Assert.Equal(NotePayloadFormat.Json, b.NotePayloadFormat);
        Assert.False(b.FeaturesExpanded);
        Assert.False(b.ScenariosExpanded);
        Assert.Equal(DiagramTabKind.Sequence, b.DiagramTab);
        Assert.False(b.ScenarioTimelineVisible);
        Assert.False(b.ComponentDiagramVisible);
        Assert.Equal(FilterCombinationMode.And, b.DependencyFilterMode);
        Assert.Equal(FilterCombinationMode.Or, b.CategoryFilterMode);
        Assert.False(b.FeaturesSummaryOpen);
        Assert.True(b.FailureClustersOpen);
        Assert.True(b.StepsSectionOpen);
        Assert.True(b.DiagramsSectionOpen);
        Assert.False(b.DiagnosticsOpen);
        Assert.True(b.RulesOpen);
        Assert.False(b.BackgroundStepsOpen);
        Assert.False(b.RawPlantUmlOpen);
        Assert.Equal(ParameterTableView.Flat, b.ParameterTableView);
        Assert.Equal(InternalFlowTab.Activity, b.InternalFlowTab);
    }

    [Fact]
    public void TestRunReport_group_overrides_apply_to_both_reports()
    {
        var options = new ReportConfigurationOptions
        {
            TestRunReportToggleDefaults =
            {
                Details = ReportDetailsState.Expanded,
                HeadersShown = false,
                DependencyFilterMode = FilterCombinationMode.Or
            }
        };

        foreach (var specifications in new[] { false, true })
        {
            var resolved = ReportToggleDefaultsResolver.Resolve(options, specifications);
            Assert.Equal(ReportDetailsState.Expanded, resolved.Details);
            Assert.False(resolved.HeadersShown);
            Assert.Equal(FilterCombinationMode.Or, resolved.DependencyFilterMode);
            // Untouched properties keep the built-ins
            Assert.Equal(TruncateLineCount.Lines40, resolved.TruncateLines);
            Assert.Equal(FilterCombinationMode.Or, resolved.CategoryFilterMode);
        }
    }

    [Fact]
    public void Specifications_group_overrides_only_the_specifications_report()
    {
        var options = new ReportConfigurationOptions
        {
            SpecificationsToggleDefaults = { ScenariosExpanded = true, TruncateLines = TruncateLineCount.Lines10 }
        };

        var testRun = ReportToggleDefaultsResolver.Resolve(options, specifications: false);
        Assert.False(testRun.ScenariosExpanded);
        Assert.Equal(TruncateLineCount.Lines40, testRun.TruncateLines);

        var specs = ReportToggleDefaultsResolver.Resolve(options, specifications: true);
        Assert.True(specs.ScenariosExpanded);
        Assert.Equal(TruncateLineCount.Lines10, specs.TruncateLines);
    }

    [Fact]
    public void Specifications_inherits_the_effective_testrun_value_per_property()
    {
        // Mixed per-property independence: spec sets one property, inherits another the
        // test-run group changed, and a third nobody set.
        var options = new ReportConfigurationOptions
        {
            TestRunReportToggleDefaults = { AssertionsShown = true, StepsShown = false },
            SpecificationsToggleDefaults = { StepsShown = true }
        };

        var specs = ReportToggleDefaultsResolver.Resolve(options, specifications: true);
        Assert.True(specs.AssertionsShown);   // inherited from the test-run override
        Assert.True(specs.StepsShown);        // spec override wins over the test-run override
        Assert.True(specs.DatabasesShown);    // nobody set it — built-in

        var testRun = ReportToggleDefaultsResolver.Resolve(options, specifications: false);
        Assert.False(testRun.StepsShown);     // the spec override never leaks into the test-run report
    }

    [Fact]
    public void Note_payload_format_group_value_wins_over_the_flat_option()
    {
        var flatOnly = new ReportConfigurationOptions { NotePayloadFormat = NotePayloadFormat.Yaml };
        Assert.Equal(NotePayloadFormat.Yaml,
            ReportToggleDefaultsResolver.Resolve(flatOnly, specifications: false).NotePayloadFormat);
        Assert.Equal(NotePayloadFormat.Yaml,
            ReportToggleDefaultsResolver.Resolve(flatOnly, specifications: true).NotePayloadFormat);

        var groupWins = new ReportConfigurationOptions
        {
            NotePayloadFormat = NotePayloadFormat.Yaml,
            TestRunReportToggleDefaults = { NotePayloadFormat = NotePayloadFormat.Json }
        };
        Assert.Equal(NotePayloadFormat.Json,
            ReportToggleDefaultsResolver.Resolve(groupWins, specifications: false).NotePayloadFormat);

        var specDiverges = new ReportConfigurationOptions
        {
            SpecificationsToggleDefaults = { NotePayloadFormat = NotePayloadFormat.Yaml }
        };
        Assert.Equal(NotePayloadFormat.Json,
            ReportToggleDefaultsResolver.Resolve(specDiverges, specifications: false).NotePayloadFormat);
        Assert.Equal(NotePayloadFormat.Yaml,
            ReportToggleDefaultsResolver.Resolve(specDiverges, specifications: true).NotePayloadFormat);
    }

    [Fact]
    public void Undefined_truncate_line_count_cast_fails_naming_the_valid_members()
    {
        var options = new ReportConfigurationOptions
        {
            TestRunReportToggleDefaults = { TruncateLines = (TruncateLineCount)37 }
        };

        var ex = Assert.Throws<ArgumentException>(() =>
            ReportToggleDefaultsResolver.Resolve(options, specifications: false));
        Assert.Contains("TruncateLineCount", ex.Message);
        Assert.Contains("37", ex.Message);
        Assert.Contains("Lines40", ex.Message);
    }

    [Fact]
    public void Undefined_casts_of_the_other_enums_fail_too()
    {
        var options = new ReportConfigurationOptions
        {
            SpecificationsToggleDefaults = { Details = (ReportDetailsState)9 }
        };
        var ex = Assert.Throws<ArgumentException>(() =>
            ReportToggleDefaultsResolver.Resolve(options, specifications: true));
        Assert.Contains("ReportDetailsState", ex.Message);
    }
}
