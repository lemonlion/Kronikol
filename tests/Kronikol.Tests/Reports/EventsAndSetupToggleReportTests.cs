using Kronikol.PlantUml;
using Kronikol.Reports;

namespace Kronikol.Tests.Reports;

/// <summary>
/// The Events and Setup toggles (5.0.0, plans/OPTION_C_PLAN.md S9, D38): Events takes a sequence diagram's calls to and
/// from message brokers out, Setup a scenario's setup (its Setup frame, or everything before its first When step). Each is
/// written beside the other filter toggles, in the report's bar and in every scenario's settings, where some diagram of
/// the report has something for it to take out, and starts as configured (shown unless set otherwise).
/// </summary>
public class EventsAndSetupToggleReportTests
{
    private const string Plain = "@startuml\nparticipant \"Orders\" as orders\nparticipant \"Stock\" as stock\norders -> stock : reserve\n@enduml";
    private const string WithBroker = "@startuml\nparticipant \"Orders\" as orders\nqueue \"Orders Topic\" as topic\norders -> topic : OrderPlaced\n@enduml";
    private static readonly string WithWhenStep = "@startuml\nparticipant \"Orders\" as orders\nparticipant \"Stock\" as stock\n" +
        StepBarPlantUml.Build("Given stock exists") + "\norders -> stock : seed\n" + StepBarPlantUml.Build("When an order is placed") +
        "\norders -> stock : reserve\n@enduml";
    private static readonly string WithRichWhenStep = "@startuml\nparticipant \"Orders\" as orders\nparticipant \"Stock\" as stock\n" +
        "orders -> stock : seed\n" + StepBarPlantUml.RichPrefix + "When an order is placed\norders -> stock : reserve\n@enduml";
    private const string WithSetupFrame = "@startuml\nparticipant \"Orders\" as orders\nparticipant \"Stock\" as stock\n" +
        "partition Setup\norders -> stock : seed\nend\norders -> stock : reserve\n@enduml";
    private static readonly string WithGivenStepOnly = "@startuml\nparticipant \"Orders\" as orders\nparticipant \"Stock\" as stock\n" +
        StepBarPlantUml.Build("Given stock exists") + "\norders -> stock : seed\n@enduml";

    private static readonly Feature[] Features =
    [
        new()
        {
            DisplayName = "Orders",
            Scenarios = [new() { Id = "s1", DisplayName = "Place an order", Result = ExecutionResult.Passed, Duration = TimeSpan.FromMilliseconds(10) }]
        }
    ];

    private static string Generate(string source, Action<ReportToggleDefaults>? configure = null)
    {
        var options = new ReportConfigurationOptions();
        configure?.Invoke(options.TestRunReportToggleDefaults);
        var path = ReportGenerator.GenerateHtmlReport(
            [new DefaultDiagramsFetcher.DiagramAsCode("s1", "", source)], Features, DateTime.UtcNow, DateTime.UtcNow,
            null, $"EventsSetup_{Guid.NewGuid():N}.html", "Test", true,
            diagramFormat: DiagramFormat.PlantUml, plantUmlRendering: PlantUmlRendering.BrowserJs,
            toggleDefaults: ReportToggleDefaultsResolver.Resolve(options, specifications: false));
        return File.ReadAllText(path);
    }

    private static string[] Toggles(string html, string name) => ReportMarkup.Elements(html, "button", $"data-toggle=\"{name}\"");

    [Fact]
    public void Events_toggles_appear_where_a_diagram_has_a_message_broker()
    {
        var toggles = Toggles(Generate(WithBroker), "events");

        Assert.Equal(2, toggles.Length);
        Assert.Contains(toggles, t => t.Contains("onclick=\"window._toggleEvents(this)\""));
        Assert.Contains(toggles, t => t.Contains("onclick=\"window._toggleScenarioEvents(this)\""));
        Assert.All(toggles, t => Assert.Contains("aria-pressed=\"true\"", t));
    }

    [Fact]
    public void No_events_toggle_without_a_message_broker() => Assert.Empty(Toggles(Generate(Plain), "events"));

    [Fact]
    public void An_events_toggle_says_its_word()
    {
        var html = Generate(WithBroker);
        Assert.Equal(2, ReportMarkup.Elements(html, "span", "data-label=\"Events\"").Length);
    }

    public static TheoryData<string, string> SetupSources => new()
    {
        { "a When step bar", WithWhenStep },
        { "a When step bar with a table's form", WithRichWhenStep },
        { "a Setup frame", WithSetupFrame }
    };

    [Theory]
    [MemberData(nameof(SetupSources))]
    public void Setup_toggles_appear_where_a_diagram_has_a_setup_to_take_out(string what, string source)
    {
        var toggles = Toggles(Generate(source), "setup");

        Assert.True(toggles.Length == 2, $"{what}: {toggles.Length} setup toggles");
        Assert.Contains(toggles, t => t.Contains("onclick=\"window._toggleSetup(this)\""));
        Assert.Contains(toggles, t => t.Contains("onclick=\"window._toggleScenarioSetup(this)\""));
    }

    [Fact]
    public void No_setup_toggle_without_a_When_step_or_a_Setup_frame()
    {
        Assert.Empty(Toggles(Generate(Plain), "setup"));
        Assert.Empty(Toggles(Generate(WithGivenStepOnly), "setup"));
    }

    [Fact]
    public void The_toggles_sit_after_the_databases_toggle_in_the_scenarios_settings()
    {
        var html = ReportMarkup.Only(Generate("@startuml\nparticipant \"Orders\" as orders\ndatabase \"Order Db\" as db\nqueue \"Orders Topic\" as topic\n" +
            "orders -> db : seed\n" + StepBarPlantUml.Build("When an order is placed") + "\norders -> topic : OrderPlaced\n@enduml"));
        var settings = html[html.IndexOf("class=\"scenario-settings\"", StringComparison.Ordinal)..];
        var databases = settings.IndexOf("data-toggle=\"databases\"", StringComparison.Ordinal);
        var events = settings.IndexOf("data-toggle=\"events\"", StringComparison.Ordinal);
        var setup = settings.IndexOf("data-toggle=\"setup\"", StringComparison.Ordinal);

        Assert.True(databases >= 0 && databases < events && events < setup, $"databases {databases}, events {events}, setup {setup}");
    }

    [Fact]
    public void The_toggles_start_as_configured()
    {
        var html = Generate(WithBroker.Replace("@enduml", StepBarPlantUml.Build("When an order is placed") + "\n@enduml"),
            d => { d.EventsShown = false; d.SetupShown = false; });

        Assert.All(Toggles(html, "events"), t => Assert.Contains("aria-pressed=\"false\"", t));
        Assert.All(Toggles(html, "setup"), t => Assert.Contains("aria-pressed=\"false\"", t));
        Assert.Contains("window._eventsVisible = false", html);
        Assert.Contains("window._setupVisible = false", html);
    }

    [Fact]
    public void The_script_shows_events_and_setup_unless_configured_otherwise()
    {
        var script = DiagramContextMenu.GetCollapsibleNotesScript(ResolvedToggleDefaults.BuiltIn);

        Assert.Contains("window._eventsVisible = true", script);
        Assert.Contains("window._setupVisible = true", script);
        Assert.DoesNotContain("__EVENTS_VISIBLE_DEFAULT__", script);
        Assert.DoesNotContain("__SETUP_VISIBLE_DEFAULT__", script);
    }

    [Fact]
    public void Specifications_inherit_the_test_run_reports_events_and_setup_defaults()
    {
        var options = new ReportConfigurationOptions { TestRunReportToggleDefaults = { EventsShown = false, SetupShown = false } };
        var resolved = ReportToggleDefaultsResolver.Resolve(options, specifications: true);

        Assert.False(resolved.EventsShown);
        Assert.False(resolved.SetupShown);
        Assert.True(ResolvedToggleDefaults.BuiltIn.EventsShown);
        Assert.True(ResolvedToggleDefaults.BuiltIn.SetupShown);
    }
}
