using System.Text;
using System.Text.RegularExpressions;

namespace Kronikol.Tests.PlantUml.Ikvm;

/// <summary>
/// Renders through real Java PlantUML and refuses to hand back anything that is not the diagram asked
/// for.
///
/// <para><b>Why this exists.</b> PlantUML does not throw when it cannot draw. Asked for a component
/// diagram on a machine with no Graphviz it returns a valid SVG — a small card reading "Cannot find
/// Graphviz" — and asked for something it cannot parse it returns a valid SVG saying "Syntax Error".
/// Both are a few hundred pixels wide, so every width bound in this project passed against them. That is
/// not a hypothetical: the CI runner had no Graphviz, and from the commit that introduced these facts
/// until this one, <em>every component-diagram width assertion on CI was measured against a ~400px error
/// card</em>. The class exists to stop a user-reported 6697px diagram being silently cropped, and on the
/// only machine that ran it in anger it was certifying an image with no diagram in it at all.</para>
///
/// <para>So "did it render?" is asked before "how big is it?", and asked of the bytes rather than of the
/// exit path — there is no exit path. A test that wants to measure a diagram goes through here.</para>
/// </summary>
internal static class RenderedDiagram
{
    /// <summary>
    /// The phrases PlantUML puts in an image when it is telling you about itself rather than drawing what
    /// you asked for. Matched against the SVG's text content, which is where they land.
    /// </summary>
    private static readonly string[] NotADiagram =
    [
        "Syntax Error",
        "Cannot find Graphviz",
        "Dot executable does not exist",
        "Dot Executable:",
        "No diagram found",
        "java.lang.",
    ];

    /// <summary>Renders to SVG, or fails the test naming what PlantUML said instead.</summary>
    public static string Svg(string plantUml)
    {
        var svg = Encoding.UTF8.GetString(IkvmPlantUmlRenderer.Render(plantUml, PlantUmlImageFormat.Svg));
        AssertIsADiagram(svg);
        return svg;
    }

    /// <summary>
    /// The size real PlantUML draws the source at, read off the SVG's <c>viewBox</c>. The SVG is not
    /// subject to <c>PLANTUML_LIMIT_SIZE</c>, so it reports the size the diagram <em>wanted</em> — which
    /// is exactly what has to stay under the limit, because the raster is what gets cropped.
    /// </summary>
    public static (int Width, int Height) Size(string plantUml)
    {
        var svg = Svg(plantUml);
        var viewBox = Regex.Match(svg, @"viewBox=""0 0 (\d+) (\d+)""");
        Assert.True(viewBox.Success, "no viewBox in the rendered SVG");
        return (int.Parse(viewBox.Groups[1].Value), int.Parse(viewBox.Groups[2].Value));
    }

    /// <summary>
    /// Fails when the SVG is one of PlantUML's own error cards rather than the requested diagram. The
    /// message names the phrase that gave it away and the first line of drawn text, because "a test
    /// failed" and "this machine cannot draw component diagrams" want very different responses.
    /// </summary>
    public static void AssertIsADiagram(string svg)
    {
        foreach (var phrase in NotADiagram)
            if (svg.Contains(phrase, StringComparison.Ordinal))
                Assert.Fail(
                    $"PlantUML returned an error image, not a diagram — it contains \"{phrase}\".\n"
                    + "If that is \"Cannot find Graphviz\", this machine has no `dot` on PATH and cannot lay out a\n"
                    + "component diagram at all; install Graphviz (`apt-get install graphviz`, `brew install graphviz`).\n"
                    + "Drawn text: " + string.Join(" · ", DrawnText(svg).Take(6)));
    }

    /// <summary>Every run of text the SVG actually paints, in order.</summary>
    public static IEnumerable<string> DrawnText(string svg) =>
        Regex.Matches(svg, @"<text\b[^>]*>([^<]*)</text>").Select(m => m.Groups[1].Value);

    /// <summary>
    /// Everything the SVG paints, as one searchable line.
    ///
    /// <para>PlantUML emits <b>one <c>&lt;text&gt;</c> element per word</b>, with the spaces between them
    /// as separate <c>&amp;#160;</c> runs — so a participant called <c>Data Insights API</c> is drawn as
    /// five elements and is findable in none of them. Entities are decoded and every run of whitespace,
    /// no-break space included, is collapsed to one space, so a caller can ask for the name it wrote.</para>
    /// </summary>
    public static string DrawnLine(string svg) =>
        Regex.Replace(
            System.Net.WebUtility.HtmlDecode(string.Join(" ", DrawnText(svg))),
            @"\s+", " ").Trim();
}
