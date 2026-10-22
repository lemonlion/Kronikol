using System.Text.RegularExpressions;
using Kronikol.Reports;

namespace Kronikol.Tests.Reports;

/// <summary>
/// <see cref="ReportConfigurationOptions.PayloadScrollLines"/> (5.4.0, <c>plans/LONG_NOTE_SCROLL_PLAN.md</c>): a payload
/// whose body would draw more lines is drawn that many lines tall and scrolls in its box. On at 40 by default, the
/// owner's decision of 2026-10-21. The option, its way to the page, its range, the title of the Expanded control, and the
/// places in the page's scripts that must know about the box; the box itself is LongPayloadScrollTests' (Playwright).
/// </summary>
public class PayloadScrollLinesTests
{
    private static readonly Feature[] Features =
    [
        new Feature
        {
            DisplayName = "F1",
            Scenarios =
            [
                new Scenario
                {
                    Id = "s1", DisplayName = "S1", Result = ExecutionResult.Passed, Duration = TimeSpan.FromMilliseconds(500),
                    Steps = [new ScenarioStep { Keyword = "Given", Text = "a step", Status = ExecutionResult.Passed }]
                }
            ]
        }
    ];

    private static string Generate(ResolvedToggleDefaults toggles, PlantUmlRendering rendering = PlantUmlRendering.BrowserJs)
    {
        var diagrams = new[] { new DefaultDiagramsFetcher.DiagramAsCode("s1", "", "@startuml\na -> b : GET /x\n@enduml") };
        var path = ReportGenerator.GenerateHtmlReport(diagrams, Features, DateTime.UtcNow, DateTime.UtcNow, null,
            $"PayloadScrollLines_{Guid.NewGuid():N}.html", "Test", true,
            diagramFormat: DiagramFormat.PlantUml, plantUmlRendering: rendering, toggleDefaults: toggles);
        return File.ReadAllText(path);
    }

    [Fact]
    public void The_option_is_40_by_default_and_the_resolved_defaults_carry_it()
    {
        Assert.Equal(40, new ReportConfigurationOptions().PayloadScrollLines);
        Assert.Equal(40, ResolvedToggleDefaults.BuiltIn.PayloadScrollLines);
        Assert.Equal(40, Kronikol.Constants.TrackingDefaults.PayloadScrollLines);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void The_option_reaches_both_reports_through_every_overlay(bool specifications)
    {
        var options = new ReportConfigurationOptions { PayloadScrollLines = 100 };
        // a start state set in both groups: each overlay builds a new record, which must carry the option on
        options.TestRunReportToggleDefaults.HeadersShown = true;
        options.SpecificationsToggleDefaults.Details = ReportDetailsState.Expanded;
        var resolved = ReportToggleDefaultsResolver.Resolve(options, specifications);
        Assert.Equal(100, resolved.PayloadScrollLines);
        Assert.True(resolved.HeadersShown);
        Assert.Equal(0, ReportToggleDefaultsResolver.Resolve(new ReportConfigurationOptions { PayloadScrollLines = 0 }, specifications).PayloadScrollLines);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(10)]
    [InlineData(40)]
    [InlineData(1000)]
    public void A_value_in_range_is_seeded_into_the_notes_script(int lines)
    {
        var script = DiagramContextMenu.GetCollapsibleNotesScript(ResolvedToggleDefaults.BuiltIn with { PayloadScrollLines = lines });
        Assert.Contains($"window._payloadScrollLines = {lines};", script);
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(1)]
    [InlineData(9)]
    [InlineData(1001)]
    public void A_value_out_of_range_fails_naming_the_range(int lines)
    {
        var e = Assert.Throws<ArgumentOutOfRangeException>(() =>
            DiagramContextMenu.GetCollapsibleNotesScript(ResolvedToggleDefaults.BuiltIn with { PayloadScrollLines = lines }));
        Assert.Contains("PayloadScrollLines", e.Message);
        Assert.Contains("0 (never cap a payload) or 10 to 1000", e.Message);
    }

    /// <summary>The range is checked where the value is used: a caller can hand the generator its own resolved record.</summary>
    [Fact]
    public void A_value_out_of_range_handed_to_the_generator_fails_too()
    {
        var e = Assert.Throws<ArgumentOutOfRangeException>(() => Generate(ResolvedToggleDefaults.BuiltIn with { PayloadScrollLines = 5 }));
        Assert.Contains("10 to 1000", e.Message);
    }

    /// <summary>
    /// A value out of range fails whatever draws the diagrams: the documentation says any other value fails report
    /// generation, and only a BrowserJs report, whose page reads the value, checked it.
    /// </summary>
    [Fact]
    public void A_value_out_of_range_fails_a_report_whose_diagrams_the_page_does_not_draw()
    {
        var e = Assert.Throws<ArgumentOutOfRangeException>(() =>
            Generate(ResolvedToggleDefaults.BuiltIn with { PayloadScrollLines = 5 }, PlantUmlRendering.Server));
        Assert.Contains("10 to 1000", e.Message);
    }

    /// <summary>The options are checked as they are resolved, before anything is drawn.</summary>
    [Fact]
    public void A_value_out_of_range_in_the_options_fails_as_they_are_resolved()
    {
        var e = Assert.Throws<ArgumentOutOfRangeException>(() =>
            ReportToggleDefaultsResolver.Resolve(new ReportConfigurationOptions { PayloadScrollLines = 5 }, specifications: false));
        Assert.Contains("10 to 1000", e.Message);
    }

    [Fact]
    public void The_Expanded_control_says_a_long_payload_scrolls_unless_the_option_is_0()
    {
        const string capped = "show in full; a long one scrolls in its box\"";
        var on = Generate(ResolvedToggleDefaults.BuiltIn);
        Assert.Contains("title=\"The payloads in every sequence diagram in the report " + capped, on);
        Assert.Contains("title=\"The payloads in this scenario's sequence diagrams " + capped, on);
        var off = Generate(ResolvedToggleDefaults.BuiltIn with { PayloadScrollLines = 0 });
        Assert.DoesNotContain("a long one scrolls", off);
        Assert.Contains("title=\"The payloads in every sequence diagram in the report show in full\"", off);
    }

    private static string Function(string script, string signature)
    {
        var start = script.IndexOf(signature, StringComparison.Ordinal);
        Assert.True(start >= 0, signature);
        var end = script.IndexOf("\n    }\n", start, StringComparison.Ordinal);
        return script[start..end];
    }

    /// <summary>The cap is a step of the composer's own: after the note states, where every note still has its index in the
    /// original source, and before the filters drop notes; its records go to a slot each caller takes with its source.</summary>
    [Fact]
    public void The_cap_runs_between_the_note_states_and_the_filters()
    {
        var compose = Function(DiagramContextMenu.GetCollapsibleNotesScript(), "function composeNoteSource(container, origSource, noteBlocks)");
        var states = compose.IndexOf("buildSourceWithNoteStates(", StringComparison.Ordinal);
        var cap = compose.IndexOf("capLongNotes(container, built, window._payloadScrollLines)", StringComparison.Ordinal);
        var filters = compose.IndexOf("applyFilters(capped.source, container)", StringComparison.Ordinal);
        Assert.True(states >= 0 && states < cap && cap < filters, compose);
        Assert.Contains("container._noteScrollPending = {", compose);
    }

    /// <summary>The records go with the picture: every swap of a diagram's picture installs them before it is decorated,
    /// and the first drawing does so in the render script.</summary>
    [Fact]
    public void Every_picture_swap_installs_the_records_it_was_drawn_with()
    {
        var notes = DiagramContextMenu.GetCollapsibleNotesScript();
        Assert.Equal(7, Regex.Matches(notes, @"installNoteScroll\(container, scroll, newSource\);").Count);
        var render = DiagramContextMenu.GetPlantUmlBrowserRenderScript();
        var install = render.IndexOf("window._installNoteScroll(", StringComparison.Ordinal);
        var decorate = render.IndexOf("window._makeNotesCollapsible(hookTarget)", StringComparison.Ordinal);
        Assert.True(install > 0 && install < decorate);
    }

    [Fact]
    public void The_role_pass_measures_nothing_in_a_box()
    {
        Assert.Contains("e.closest('.note-scroll-frame')", DiagramContextMenu.GetPlantUmlBrowserRenderScript());
    }

    /// <summary>The context menu leaves a box to its reader (a click places the caret, a drag selects), copies a picture
    /// without it, and reads a boxed note's "current" text and the source before the cap.</summary>
    [Fact]
    public void The_context_menu_knows_the_box()
    {
        var menu = DiagramContextMenu.GetContextMenuScript();
        Assert.Equal(2, Regex.Matches(menu, Regex.Escape("e.target.closest('.note-scroll')")).Count);
        Assert.Equal(2, Regex.Matches(menu, Regex.Escape("svg: withoutScrollBoxes(clone)")).Count);
        Assert.Contains("window._noteScrollLines(container, globalNoteIdx)", menu);
        Assert.Contains("window._noteUncappedSource(container, source)", menu);
        Assert.DoesNotContain("copyTextToClipboard(source)", menu);
        Assert.DoesNotContain("openTextInTab(source,", menu);
    }

    /// <summary>The three arrow-key handlers leave a focused box its keys: the scenario keys (Up and Down), and the tabs' and
    /// the Features tools' capture-phase handlers, which a box's own handler could not stop.</summary>
    [Fact]
    public void The_arrow_key_handlers_leave_a_box_its_keys()
    {
        var html = Generate(ResolvedToggleDefaults.BuiltIn);
        Assert.Contains("if (e.target.closest && e.target.closest('.note-scroll')) return;", html);
        Assert.Contains("e.target.closest('[role=\"tablist\"], .note-scroll')", html);
        Assert.Contains("t.closest('[role=\"tablist\"], .note-scroll')", html);
    }
}
