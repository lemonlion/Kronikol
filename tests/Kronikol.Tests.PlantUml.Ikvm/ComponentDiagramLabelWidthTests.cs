using System.Text.RegularExpressions;
using Kronikol.ComponentDiagram;
using Kronikol.Constants;

namespace Kronikol.Tests.PlantUml.Ikvm;

/// <summary>
/// The component diagram has to survive <em>real</em> PlantUML, not just the TeaVM JavaScript build
/// Kronikol embeds for <c>BrowserJs</c> rendering — the same source is what
/// <c>PlantUmlRendering.Server</c> sends to a PlantUML server, what the context menu's "Open PlantUML
/// source" hands a user to paste into plantuml.com, and what <c>PlantUmlRendering.Local</c> renders
/// through IKVM.
/// <para>
/// The two engines disagree about one thing that matters here: <b>the JS build wraps a long edge label
/// at <c>skinparam wrapWidth</c>; Java PlantUML does not wrap arrow labels at all.</b> A user-reported
/// diagram whose ClickHouse edge listed thirty SQL operations therefore drew as 840×863 in the report
/// and as <b>6697</b>×586 in real PlantUML — past the 4096-pixel <c>PLANTUML_LIMIT_SIZE</c> that
/// plantuml.com and every default PlantUML install crop at, which cut every dependency node off the
/// canvas. From the user's side the architecture overview simply did not render.
/// </para>
/// <para>
/// So the width bound cannot be delegated to <c>wrapWidth</c>: the generator has to put the line breaks
/// in itself. These facts measure the drawn diagram rather than the source, because the source looked
/// perfectly valid in both engines — the failure was purely one of size.
/// </para>
/// </summary>
public class ComponentDiagramLabelWidthTests
{
    /// <summary>
    /// PlantUML's own default <c>PLANTUML_LIMIT_SIZE</c>. A diagram wider than this is cropped to it —
    /// silently, and with no error anywhere in the output.
    /// </summary>
    private const int PlantUmlLimitSize = 4096;

    /// <summary>The thirty ClickHouse operations from the reported diagram, verbatim.</summary>
    private static readonly string[] ReportedClickHouseOperations =
    [
        "DELETE FROM all_locations",
        "DELETE FROM location_performance_weekly",
        "DELETE FROM market_transactions_output",
        "Other",
        "SELECT FROM aggregated",
        "SELECT FROM aggregated_nation",
        "SELECT FROM all_locations",
        "SELECT FROM base_with_comp",
        "SELECT FROM columns",
        "SELECT FROM competitor_customer_performance_weekly",
        "SELECT FROM competitor_location_performance_monthly",
        "SELECT FROM competitor_location_performance_weekly",
        "SELECT FROM current_buckets",
        "SELECT FROM current_period",
        "SELECT FROM current_with_month_key",
        "SELECT FROM current_with_week_key",
        "SELECT FROM industry_performance_weekly",
        "SELECT FROM industry_performance_yearly",
        "SELECT FROM location_demographics_monthly",
        "SELECT FROM location_demographics_weekly",
        "SELECT FROM location_demographics_yearly",
        "SELECT FROM location_performance_daily",
        "SELECT FROM location_performance_monthly",
        "SELECT FROM location_performance_weekly",
        "SELECT FROM location_performance_yearly",
        "SELECT FROM market_shares_monthly",
        "SELECT FROM market_shares_weekly",
        "SELECT FROM market_shares_yearly",
        "SELECT FROM the",
    ];

    /// <summary>The reported diagram, rebuilt from its relationships.</summary>
    private static ComponentRelationship[] ReportedRelationships() =>
    [
        new("Data Insights API", "ClickHouse", "ClickHouse", [.. ReportedClickHouseOperations], 609, 199, "ClickHouse"),
        new("Caller", "Data Insights API", "HTTP", ["GET", "POST"], 252, 241, null),
        new("Data Insights API", "Redis", "Redis", ["Get", "Set"], 959, 224, "Redis"),
        new("Data Insights API", "Intelligence AI", "HTTP", ["POST"], 30, 27, null),
        new("Data Insights API", "MongoDB", "MongoDB", ["Find ← Trial", "Insert → Trial"], 14, 14, "MongoDB"),
        new("Data Insights API", "BigQuery", "BigQuery", ["Other"], 78, 3, "BigQuery"),
    ];

    /// <summary>
    /// The size real PlantUML draws <paramref name="plantUml"/> at, read off the SVG's <c>viewBox</c>
    /// (the SVG is not subject to <c>PLANTUML_LIMIT_SIZE</c>, so it reports the size the diagram
    /// <em>wanted</em> — which is exactly what has to stay under the limit).
    /// </summary>
    private static (int Width, int Height) DrawnSize(string plantUml)
    {
        var svg = System.Text.Encoding.UTF8.GetString(
            IkvmPlantUmlRenderer.Render(plantUml, PlantUmlImageFormat.Svg));
        Assert.DoesNotContain("Syntax Error", svg, StringComparison.Ordinal);
        var viewBox = Regex.Match(svg, @"viewBox=""0 0 (\d+) (\d+)""");
        Assert.True(viewBox.Success, "no viewBox in the rendered SVG");
        return (int.Parse(viewBox.Groups[1].Value), int.Parse(viewBox.Groups[2].Value));
    }

    [Fact]
    public void The_reported_diagram_fits_inside_plantumls_default_size_limit()
    {
        var plantUml = ComponentDiagramGenerator.GeneratePlantUml(ReportedRelationships(), useC4: false);

        var (width, height) = DrawnSize(plantUml);

        Assert.True(width < PlantUmlLimitSize, $"drawn {width}px wide — PlantUML crops at {PlantUmlLimitSize}");
        Assert.True(height < PlantUmlLimitSize, $"drawn {height}px tall — PlantUML crops at {PlantUmlLimitSize}");
    }

    [Fact]
    public void Every_dependency_node_survives_the_reported_diagram()
    {
        // The failure mode is not an error — it is a crop. Past 4096 pixels PlantUML keeps the top-left
        // corner and throws the rest away, so "did it render?" has to mean "are the nodes still there?".
        var plantUml = ComponentDiagramGenerator.GeneratePlantUml(ReportedRelationships(), useC4: false);
        var png = IkvmPlantUmlRenderer.Render(plantUml, PlantUmlImageFormat.Png);

        // PNG header: width and height are big-endian uint32 at offsets 16 and 20.
        var width = (png[16] << 24) | (png[17] << 16) | (png[18] << 8) | png[19];

        Assert.True(width < PlantUmlLimitSize, $"the rasterised diagram was cropped to {width}px");
    }

    /// <summary>
    /// A participant name is the diagram's other unbounded width axis, and the narrower one: measured
    /// against this engine a <c>&lt;&lt;system&gt;&gt;</c> rectangle grows at about 7.3 pixels per
    /// character and crosses 4096 around 528 characters, while the hexagon an
    /// <c>DependencyCategories.AI</c> dependency draws as grows at about 14 and crosses it around 284.
    /// <c>skinparam wrapWidth</c> is no defence: it only breaks at whitespace, and the names that get
    /// long — hosts, fully-qualified type names, connection descriptors — have none.
    /// </summary>
    [Theory]
    [InlineData(DependencyCategories.AI)]        // hexagon — the widest shape, and the first to crop
    [InlineData(DependencyCategories.HTTP)]      // rectangle <<system>>
    [InlineData("ClickHouse")]                   // database
    [InlineData(DependencyCategories.Redis)]     // collections
    public void A_dependency_with_an_enormous_name_still_fits(string category)
    {
        // Long enough to crop every shape: the hexagon goes at ~284 characters, the rectangle at ~528,
        // database/collections/queue at ~570.
        var name = "DataInsights.Reporting.Analytics." + new string('x', 700) + "Gateway";
        var relationships = new ComponentRelationship[]
        {
            new("Caller", name, category, ["Post"], 5, 3, category),
        };

        var plantUml = ComponentDiagramGenerator.GeneratePlantUml(relationships, useC4: false);

        var (width, height) = DrawnSize(plantUml);

        Assert.True(width < PlantUmlLimitSize, $"drawn {width}px wide — PlantUML crops at {PlantUmlLimitSize}");
        Assert.True(height < PlantUmlLimitSize, $"drawn {height}px tall — PlantUML crops at {PlantUmlLimitSize}");
    }

    [Fact]
    public void A_wrapped_participant_name_stays_bold_and_leaks_no_creole_markers()
    {
        // Creole bold is line-scoped: `**a\nb**` loses the weight on every line AND draws a literal `**`
        // at the end. The name has to close and reopen its markers on each line instead.
        var name = "DataInsights.Reporting." + new string('x', 300) + "Gateway";
        var relationships = new ComponentRelationship[]
        {
            new("Caller", name, "HTTP", ["GET"], 5, 3, null),
        };

        var svg = System.Text.Encoding.UTF8.GetString(IkvmPlantUmlRenderer.Render(
            ComponentDiagramGenerator.GeneratePlantUml(relationships, useC4: false), PlantUmlImageFormat.Svg));

        var nameRuns = Regex.Matches(svg, @"<text\b[^>]*>([^<]*x{10,}[^<]*)</text>")
            .Select(m => m.Value)
            .ToArray();
        Assert.NotEmpty(nameRuns);
        Assert.All(nameRuns, run => Assert.Contains("font-weight=\"bold\"", run, StringComparison.Ordinal));
        Assert.DoesNotContain("**", svg, StringComparison.Ordinal);
    }

    [Fact]
    public void A_label_far_longer_than_any_real_one_still_fits()
    {
        // The reported label was 1049 characters. The statement cap allows a label of nearly 2000, so the
        // width bound has to hold at the cap rather than merely at the size that was reported.
        var methods = Enumerable.Range(0, 60).Select(i => $"SELECT FROM some_quite_long_table_name_{i:D2}");
        var relationships = new ComponentRelationship[]
        {
            new("Caller", "Warehouse", "ClickHouse", [.. methods], 5000, 400, "ClickHouse"),
        };

        var plantUml = ComponentDiagramGenerator.GeneratePlantUml(relationships, useC4: false);

        var (width, height) = DrawnSize(plantUml);

        Assert.True(width < PlantUmlLimitSize, $"drawn {width}px wide — PlantUML crops at {PlantUmlLimitSize}");
        Assert.True(height < PlantUmlLimitSize, $"drawn {height}px tall — PlantUML crops at {PlantUmlLimitSize}");
    }
}
