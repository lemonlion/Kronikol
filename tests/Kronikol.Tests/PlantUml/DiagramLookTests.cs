using Kronikol.ComponentDiagram;
using Kronikol.Constants;
using Kronikol.PlantUml;
using Kronikol.Tracking;

namespace Kronikol.Tests.PlantUml;

/// <summary>
/// The diagram look the emitter writes into a sequence diagram's source (5.0.0, plans/OPTION_C_PLAN.md S10, P17 to
/// P20 and R17.22): the system font where a browser engine draws the diagram, flat boxes, rounded participants with
/// thin lines and thin note edges, services drawn as boxes, and databases in deep cyan. A diagram drawn with a user's
/// theme keeps the theme's own look.
/// </summary>
public class DiagramLookTests
{
    private static RequestResponseLog Request(string serviceName = "OrderService", string callerName = "WebApp") => new(
        TestName: "My Test",
        TestId: "test-1",
        Method: HttpMethod.Get,
        Content: null,
        Uri: new Uri("http://example.com/api/orders"),
        Headers: [],
        ServiceName: serviceName,
        CallerName: callerName,
        Type: RequestResponseType.Request,
        TraceId: Guid.NewGuid(),
        RequestResponseId: Guid.NewGuid(),
        TrackingIgnore: false);

    private static string Source(string? theme = null, bool systemUiFont = false, params RequestResponseLog[] logs) =>
        PlantUmlCreator.GetPlantUmlImageTagsPerTestId(logs.Length > 0 ? logs : [Request()], plantUmlTheme: theme,
            systemUiFont: systemUiFont).Single().PlantUmls.First().PlainText.ReplaceLineEndings("\n");

    // The engine measures and paints the same face only when it is the system's own: a font list is quoted into one
    // family no system has, and the JVM (Server and Local rendering) knows no system-ui.
    [Fact]
    public void A_diagram_a_browser_engine_draws_is_set_in_the_system_font() =>
        Assert.Contains("!pragma teoz true\nskinparam defaultFontName system-ui\n", Source(systemUiFont: true));

    [Fact]
    public void A_diagram_the_JVM_draws_keeps_its_own_font() =>
        Assert.DoesNotContain("defaultFontName", Source());

    [Fact]
    public void Every_sequence_diagram_has_flat_boxes_rounded_participants_and_thin_lines()
    {
        var source = Source();

        Assert.Contains("skinparam shadowing false\nskinparam SequenceMessagePadding 4\nskinparam sequenceMessageAlignment center\n", source);
        Assert.Contains(
            "<style>\n" +
            "sequenceDiagram { participant { RoundCorner 12; Padding 8 14; Margin 0 10; LineThickness 1 } }\n" +
            "sequenceDiagram { actor, database, queue, collections, boundary, control, entity { Margin 0 10; LineThickness 1 } }\n" +
            "sequenceDiagram { lifeLine { LineStyle 0; LineThickness 1 } }\n" +
            "sequenceDiagram { note { LineThickness 1 } }\n" +
            "</style>\n", source);
    }

    // The source's line readers go by how a line starts: the query tool's payload reader opens a note at "note " and the
    // report's Setup filter takes "participant" and any word for a declaration, so a look written one property to a line
    // ("  note {", "  participant {") would be read as a note and a participant.
    [Fact]
    public void No_line_of_the_look_reads_as_a_note_or_a_declaration()
    {
        var look = DiagramLook.ForSequence(null, systemUiFont: true).Split('\n', StringSplitOptions.RemoveEmptyEntries);

        Assert.All(look, line => Assert.Matches(@"^(?:skinparam |<style>$|</style>$|sequenceDiagram \{ )", line));
    }

    // The look is the base the diagram's own styles (event notes, marker notes) and settings are written over.
    [Fact]
    public void The_look_comes_straight_after_the_pragma_before_the_diagrams_own_settings()
    {
        var source = Source(systemUiFont: true);

        Assert.True(source.IndexOf("!pragma teoz true", StringComparison.Ordinal) < source.IndexOf("skinparam defaultFontName", StringComparison.Ordinal));
        Assert.True(source.IndexOf("</style>", StringComparison.Ordinal) < source.IndexOf("skinparam wrapWidth", StringComparison.Ordinal));
    }

    [Fact]
    public void A_user_theme_keeps_its_own_look()
    {
        var source = Source(theme: "cerulean", systemUiFont: true);

        Assert.Contains("!theme cerulean", source);
        Assert.DoesNotContain("defaultFontName", source);
        Assert.DoesNotContain("skinparam shadowing false", source);
        Assert.DoesNotContain("RoundCorner 12", source);
    }

    // An entity is drawn as a circle on a line; a service reads as a box, as the participants around it do.
    [Fact]
    public void A_service_is_drawn_as_a_box()
    {
        Assert.Equal("participant", DependencyPalette.GetSequenceShape(DependencyType.HttpApi));

        var source = Source(logs: [Request(callerName: "WebApp"), Request(serviceName: "Backend", callerName: "Gateway")]);

        Assert.Contains("participant \"Gateway\" as gateway", source);
        Assert.DoesNotContain("entity \"", source);
    }

    // Red read as an error beside the failures it sits among (R17.22): databases are deep cyan, in the palette and so
    // in every diagram that takes its colours from it.
    [Fact]
    public void A_database_is_drawn_in_deep_cyan()
    {
        Assert.Equal("#0E7C86", DependencyPalette.DefaultColors[DependencyType.Database]);
        Assert.Equal("#0E7C86", DependencyPalette.GetColor(DependencyCategories.SQL));
    }

    // A component diagram drawn without C4 is the browser engines' (the JVM's draws with C4), so it is set in the system
    // font, unless a user's theme sets the look.
    [Fact]
    public void A_component_diagram_a_browser_engine_draws_is_set_in_the_system_font()
    {
        ComponentRelationship[] calls = [new("App", "Api", "HTTP", ["GET"], 1, 1)];

        Assert.Contains("left to right direction\nskinparam defaultFontName system-ui\n",
            ComponentDiagramGenerator.GeneratePlantUml(calls, useC4: false).ReplaceLineEndings("\n"));
        Assert.DoesNotContain("defaultFontName", ComponentDiagramGenerator.GeneratePlantUml(calls, useC4: true));
        Assert.DoesNotContain("defaultFontName",
            ComponentDiagramGenerator.GeneratePlantUml(calls, new ComponentDiagramOptions { PlantUmlTheme = "cerulean" }, useC4: false));
    }

    // The diff of two component diagrams is drawn by the same engines, so the same rule holds for it.
    [Fact]
    public void A_component_diff_a_browser_engine_draws_is_set_in_the_system_font()
    {
        ComponentRelationship[] baseline = [new("A", "B", "HTTP", ["GET"], 1, 1)];
        var diff = ComponentDiagramDiffer.Compare(baseline, [.. baseline, new("A", "C", "HTTP", ["GET"], 1, 1)]);

        Assert.StartsWith("@startuml\nskinparam defaultFontName system-ui\n",
            ComponentDiagramDiffer.GenerateDiffPlantUml(diff, useC4: false).ReplaceLineEndings("\n"));
        Assert.DoesNotContain("defaultFontName", ComponentDiagramDiffer.GenerateDiffPlantUml(diff, useC4: true));
    }

    // Every component box carries its name in white at AA or better: the queue box takes the preview's deeper violet
    // (5.87:1, was 4.67:1) and the cache box a burnt orange (was #F39C12, 2.19:1), each with an edge deeper than its fill.
    [Theory]
    [InlineData("database", "#0B6C75", "#09616A")]
    [InlineData("collections", "#B35900", "#8F4700")]
    [InlineData("queue", "#8E44AD", "#7D3C98")]
    public void A_component_box_carries_its_white_name_at_AA(string shape, string fill, string edge)
    {
        var plantUml = ComponentDiagramGenerator.GeneratePlantUml(
            [new ComponentRelationship("App", "Api", "HTTP", ["GET"], 1, 1)], useC4: false).ReplaceLineEndings("\n");

        Assert.Contains($"skinparam {shape} {{\n  BackgroundColor {fill}\n  FontColor #FFFFFF\n  BorderColor {edge}\n}}", plantUml);
        Assert.True(Contrast(fill, "#FFFFFF") >= 4.5, $"{shape}: white on {fill} is {Contrast(fill, "#FFFFFF"):0.00}:1");
    }

    // WCAG 2 contrast ratio of two sRGB colours written #RRGGBB.
    private static double Contrast(string a, string b)
    {
        static double Luminance(string hex)
        {
            double Channel(int i)
            {
                var c = Convert.ToInt32(hex.Substring(1 + 2 * i, 2), 16) / 255.0;
                return c <= 0.04045 ? c / 12.92 : Math.Pow((c + 0.055) / 1.055, 2.4);
            }
            return 0.2126 * Channel(0) + 0.7152 * Channel(1) + 0.0722 * Channel(2);
        }
        var (la, lb) = (Luminance(a), Luminance(b));
        return (Math.Max(la, lb) + 0.05) / (Math.Min(la, lb) + 0.05);
    }

    // The component diagram's database box is a shade deeper than the arrows, so its white name reads at 6.15:1 (P35).
    [Fact]
    public void A_component_diagrams_database_box_is_deep_cyan()
    {
        var plantUml = ComponentDiagramGenerator.GeneratePlantUml(
            [new ComponentRelationship("App", "Db", "CosmosDB", ["Query"], 1, 1, DependencyCategory: "CosmosDB")], useC4: false);

        Assert.Contains("skinparam database {\n  BackgroundColor #0B6C75\n  FontColor #FFFFFF\n  BorderColor #09616A\n}",
            plantUml.ReplaceLineEndings("\n"));
    }
}
