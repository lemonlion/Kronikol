using Kronikol.Reports;
using Kronikol.Tracking;

namespace Kronikol.Tests.Reports;

/// <summary>
/// A scenario's diagram toolbars (5.0.0, plans/OPTION_C_PLAN.md S8, D29 to D34, D37, R17.23 and R17.26, item 25): the
/// view tabs on a bar of their own, then the scenario's settings, an options bar in three zones (payloads, their format
/// and width, the toggles) that its gear opens, starting "This scenario:", hidden while a view other than the sequence
/// diagrams shows; on a phone a gear on the section's title shows it. The payload controls say Payloads, Expanded,
/// Collapsed and Truncated, a toggle says its bare word with <c>aria-pressed</c>, and each label sits in a span that
/// holds its bold width.
/// </summary>
public class ScenarioToolbarTests
{
    private const string NotesSource = """
        @startuml
        actor "Caller" as caller
        participant "Svc" as svc
        database "Db" as db
        hnote across #black <<stepDelimiter>>: <color:white>Given an order request</color>
        caller -> svc : POST /api/orders
        note left
        <color:gray>[content-type=application/json]</color>

        {
          "query": "SELECT 1"
        }
        end note
        svc -> db : INSERT INTO orders
        svc --> caller : 200 OK
        note right <<assertionNote>>: ✓ status code should be OK
        @enduml
        """;

    private static Feature[] OneScenario =>
    [
        new Feature
        {
            DisplayName = "F1",
            Scenarios = [new Scenario { Id = "s1", DisplayName = "S1", Result = ExecutionResult.Passed, Duration = TimeSpan.FromMilliseconds(500) }]
        }
    ];

    private static string Generate(PlantUmlRendering rendering = PlantUmlRendering.BrowserJs)
    {
        var path = ReportGenerator.GenerateHtmlReport(
            [new DefaultDiagramsFetcher.DiagramAsCode("s1", "", NotesSource)], OneScenario,
            DateTime.UtcNow, DateTime.UtcNow, null, $"ScenarioToolbar_{Guid.NewGuid():N}.html", "Test", true,
            diagramFormat: DiagramFormat.PlantUml, plantUmlRendering: rendering);
        return ReportHtml.Read(path);
    }

    private static string GenerateRich(Action<ReportToggleDefaults>? configure = null)
    {
        var (diagrams, _, testRunFeatures, segments, cleanup) = ToggleDefaultsBaselineTests.BuildRichFixture();
        using (cleanup)
        {
            var options = new ReportConfigurationOptions();
            configure?.Invoke(options.TestRunReportToggleDefaults);
            var resolved = ReportToggleDefaultsResolver.Resolve(options, specifications: false);
            return ReportHtml.Read(ToggleDefaultsBaselineTests.GenerateTestRunShape(diagrams, testRunFeatures, segments,
                $"ScenarioToolbarRich_{Guid.NewGuid():N}.html", resolved));
        }
    }

    private static void InOrder(string text, params string[] parts)
    {
        var at = -1;
        foreach (var part in parts)
        {
            var next = text.IndexOf(part, at + 1, StringComparison.Ordinal);
            Assert.True(next > at, $"'{part}' is missing or out of order");
            at = next;
        }
    }

    [Fact]
    public void A_scenario_with_views_has_its_tabs_on_a_bar_then_its_settings_behind_a_gear()
    {
        var html = GenerateRich();
        var start = html.IndexOf("<div class=\"diagram-toggle diagram-toggle-tabs\"", StringComparison.Ordinal);
        Assert.True(start >= 0, "no tabs bar");
        InOrder(html[start..],
            "<div class=\"diagram-toggle diagram-toggle-tabs\" role=\"tablist\" aria-label=\"Diagram views\">"
            + "<button class=\"diagram-toggle-btn diagram-toggle-active\" role=\"tab\" aria-selected=\"true\" tabindex=\"0\" data-dtype=\"seq\" title=\"The calls between the test and the services, in order\">Sequence Diagrams</button>"
            + "<button class=\"diagram-toggle-btn\" role=\"tab\" aria-selected=\"false\" tabindex=\"-1\" data-dtype=\"activity\" title=\"The work inside the services, span by span, as an activity diagram\">Activity Diagrams</button>",
            "</div><div class=\"scenario-settings\"><div class=\"diagram-toggle diagram-options-bar\"><span class=\"scenario-settings-label\">This scenario:</span><span class=\"details-radio\">",
            "<span class=\"diagram-format\">",
            "<span class=\"diagram-flags\"><button class=\"details-radio-btn toggle-btn",
            "</span></div><button type=\"button\" class=\"scenario-settings-gear\" aria-label=\"This scenario's settings\" aria-expanded=\"false\" title=\"This scenario's payload and diagram settings, to set it apart from the report's\"><svg viewBox=\"-12 -12 24 24\" aria-hidden=\"true\" focusable=\"false\"><path d=\"M6.81 -2.33L9.41 -1.91",
            "</svg></button></div>");
        Assert.DoesNotContain("<span class=\"diagram-toggle-spacer\">", html);
    }

    [Fact]
    public void A_section_on_another_view_hides_its_settings_until_the_sequence_diagrams_show()
    {
        var html = GenerateRich(t => t.DiagramTab = DiagramTabKind.Activity);

        InOrder(html,
            "<details class=\"example-diagrams diagrams-not-seq\"",
            "<button class=\"diagram-toggle-btn\" role=\"tab\" aria-selected=\"false\" tabindex=\"-1\" data-dtype=\"seq\" title=\"The calls between the test and the services, in order\">Sequence Diagrams</button>"
            + "<button class=\"diagram-toggle-btn diagram-toggle-active\" role=\"tab\" aria-selected=\"true\" tabindex=\"0\" data-dtype=\"activity\" title=\"The work inside the services, span by span, as an activity diagram\">Activity Diagrams</button>",
            "</div><div class=\"scenario-settings\"><div class=\"diagram-toggle diagram-options-bar\">");
        Assert.DoesNotContain("not-seq\"", html.Replace("diagrams-not-seq\"", ""));
    }

    [Fact]
    public void A_section_with_no_sequence_diagrams_has_no_settings()
    {
        var html = GenerateRich();
        // each section's head: its title, its tabs and its settings, up to its first view
        var heads = html.Split("<details class=\"example-diagrams").Skip(1)
            .Select(s => s[..new[] { "<div class=\"diagram-view", "<div class=\"plantuml-browser", "</details>" }
                .Select(end => s.IndexOf(end, StringComparison.Ordinal)).Where(i => i >= 0).Min()]).ToArray();
        var flowOnly = heads.Where(h => !h.Contains("Sequence Diagrams", StringComparison.Ordinal)).ToArray();

        Assert.Contains(flowOnly, h => h.Contains("data-dtype=\"activity\"", StringComparison.Ordinal) && h.Contains("data-dtype=\"flame\"", StringComparison.Ordinal));
        Assert.All(flowOnly, h =>
        {
            Assert.DoesNotContain("scenario-settings", h);
            Assert.DoesNotContain("gear-in-summary", h);
            Assert.DoesNotContain("details-radio", h);
        });
        Assert.Contains(heads, h => h.Contains("<div class=\"scenario-settings\">", StringComparison.Ordinal));
    }

    [Fact]
    public void A_scenario_with_only_sequence_diagrams_has_no_tabs_but_the_same_settings()
    {
        var html = Generate();

        Assert.DoesNotContain("<div class=\"diagram-toggle diagram-toggle-tabs\"", html);
        InOrder(html,
            "<details class=\"example-diagrams\"",
            "<summary class=\"h4\" title=\"This scenario's sequence diagrams\">Sequence Diagrams<button type=\"button\" class=\"scenario-diagram-controls-toggle gear-in-summary\" aria-label=\"Diagram settings\" title=\"Diagram settings\" aria-expanded=\"false\">⚙</button></summary>"
            + "<div class=\"scenario-settings\"><div class=\"diagram-toggle diagram-options-bar\"><span class=\"scenario-settings-label\">This scenario:</span>");
    }

    [Fact]
    public void The_payload_controls_say_Payloads_and_each_label_holds_its_bold_width()
    {
        var html = Generate();

        InOrder(html,
            "<span class=\"scenario-settings-label\">This scenario:</span><span class=\"details-radio\"><span class=\"details-radio-label\" title=\"How the payloads in this scenario's sequence diagrams are shown (the boxes beside the arrows, holding each request, response and message, with its headers): whole, folded to one line, or cut at a line limit\">Payloads</span>"
            + "<button class=\"details-radio-btn\" data-state=\"expanded\" onclick=\"window._setAllNotes(this,'expanded')\" title=\"The payloads in this scenario's sequence diagrams show in full; a long one scrolls in its box\"><span class=\"btn-label\" data-label=\"Expanded\">Expanded</span></button>"
            + "<button class=\"details-radio-btn\" data-state=\"collapsed\" onclick=\"window._setAllNotes(this,'collapsed')\" title=\"The payloads in this scenario's sequence diagrams fold to one line each; a box's + button opens it\"><span class=\"btn-label\" data-label=\"Collapsed\">Collapsed</span></button>"
            + "<button class=\"details-radio-btn details-active\" data-state=\"truncated\" onclick=\"window._setAllNotes(this,'truncated')\" title=\"Payloads in this scenario's sequence diagrams longer than the line limit beside this are cut at that many lines\"><span class=\"btn-label\" data-label=\"Truncated\">Truncated</span></button>"
            + "<select class=\"truncate-lines-select\" autocomplete=\"off\" aria-label=\"Payload length\" title=\"How many lines a cut payload box keeps in this scenario's sequence diagrams (with Truncated picked)\" onchange=\"window._setScenarioTruncateLines(this)\"><optgroup label=\"Payload length\"><option value=\"3\">3 lines</option>",
            "</optgroup></select></span>");
        Assert.DoesNotContain("class=\"truncate-lines-label\"", html);
        Assert.DoesNotContain(">Details:<", html);
    }

    [Fact]
    public void A_toggle_says_its_bare_word_and_whether_it_is_on()
    {
        var html = Generate();

        Assert.Contains("<button class=\"details-radio-btn toggle-btn\" data-toggle=\"headers\" data-shown=\"false\" aria-pressed=\"false\" onclick=\"window._toggleScenarioHeaders(this)\" title=\"Show or hide the headers at the top of each payload in this scenario's sequence diagrams\"><span class=\"btn-label\" data-label=\"Headers\">Headers</span></button>", html);
        Assert.Contains("<button class=\"details-radio-btn toggle-btn details-active\" data-toggle=\"steps\" data-shown=\"true\" aria-pressed=\"true\" onclick=\"window._toggleScenarioSteps(this)\" title=\"Show or hide the bars marking where each step starts in this scenario's sequence diagrams\"><span class=\"btn-label\" data-label=\"Steps\">Steps</span></button>", html);
        Assert.DoesNotContain(" Shown</", html);
        Assert.DoesNotContain(" Hidden</", html);
    }

    [Fact]
    public void The_format_and_width_controls_name_the_payloads()
    {
        var html = Generate();

        Assert.Contains("<select class=\"note-format-select\" autocomplete=\"off\" aria-label=\"Payload format\" title=\"How the payloads in this scenario's sequence diagrams are written: JSON as captured, or the same data as YAML\" onchange=\"window._setScenarioNoteFormat(this)\"><optgroup label=\"Payload format\"><option value=\"json\">JSON</option><option value=\"yaml\" selected>YAML</option></optgroup></select>", html);
        Assert.Contains("<select class=\"note-width-select\" autocomplete=\"off\" aria-label=\"Payload width\" title=\"How wide the payloads are in this scenario's sequence diagrams. Wrap: they wrap at a set width. Wide: they widen to fill the diagram.\" onchange=\"window._setScenarioNoteWidth(this)\"><optgroup label=\"Payload width\">", html);
        Assert.DoesNotContain("Note payload format", html);
        Assert.DoesNotContain("\"Note width\"", html);
    }

    [Fact]
    public void The_page_carries_the_settings_script_only_where_it_has_settings()
    {
        Assert.Contains("<script id=\"kronikol-scenario-settings\">", Generate());
        Assert.DoesNotContain("<script id=\"kronikol-scenario-settings\">", Generate(PlantUmlRendering.Server));
    }

    [Fact]
    public void A_report_drawn_when_it_was_written_has_no_settings_to_open()
    {
        var html = Generate(PlantUmlRendering.Server);

        Assert.DoesNotContain("<div class=\"scenario-settings\">", html);
        Assert.DoesNotContain("class=\"scenario-settings-gear\"", html);
        Assert.DoesNotContain("gear-in-summary\"", html);
    }
}
