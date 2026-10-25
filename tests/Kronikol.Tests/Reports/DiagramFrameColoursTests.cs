using System.Globalization;
using System.Text.RegularExpressions;
using Kronikol.PlantUml;
using Kronikol.Reports;

namespace Kronikol.Tests.Reports;

/// <summary>
/// A loop's box and the Setup box take the theme's colours (5.8.0). The diagram's source draws them in the light theme's
/// colours (<see cref="DiagramLook"/>), which a picture drawn away from the page keeps; the page gives each of those
/// colours a token with a light and a dark value. Before, the Setup box stayed a near-white block in the dark theme with
/// the dark theme's light ink on it, and a loop's frame and the ink of its tab were lost on the dark ground.
/// </summary>
public class DiagramFrameColoursTests
{
    private const string SequenceScope = ".plantuml-browser:where(:not(.iflow-diagram)) svg ";

    public static TheoryData<string, string, string> Mapped => new()
    {
        { DiagramLook.LoopFill, "fill", "--dg-loop" },
        // the engine draws a box's ground with an edge of the ground's own colour
        { DiagramLook.LoopFill, "stroke", "--dg-loop" },
        { DiagramLook.LoopEdge, "stroke", "--dg-loop-edge" },
        { DiagramLook.LoopTab, "fill", "--dg-loop-tab" },
        { DiagramLook.SetupFill, "fill", "--dg-setup" },
        { DiagramLook.SetupEdge, "stroke", "--dg-setup-edge" },
    };

    [Theory]
    [MemberData(nameof(Mapped))]
    public void The_page_paints_each_of_the_looks_frame_colours_in_its_token(string colour, string property, string token)
    {
        var painted = CssRules.Parse(DiagramContextMenu.GetInlineSvgStyles())
            .Where(r => r.Selectors.Any(s => s.Contains($"[{property}=\"{colour}\" i]", StringComparison.OrdinalIgnoreCase)))
            .ToList();

        Assert.True(painted.Count > 0, $"no rule paints {property} {colour}");
        // in a sequence diagram only: an activity diagram keeps the engine's colours
        Assert.All(painted, r => Assert.All(r.Selectors, s => Assert.StartsWith(SequenceScope, s, StringComparison.Ordinal)));
        // the token, with the engine's colour to fall back on where a page carries no token sheet (--dg-loop is not --dg-loop-edge)
        Assert.Contains(painted, r => Regex.IsMatch(r.Declarations.GetValueOrDefault(property) ?? "", $@"^var\({Regex.Escape(token)}\s*[,)]"));
    }

    public static TheoryData<string, string> Tokens => new()
    {
        { "--dg-loop", DiagramLook.LoopFill },
        { "--dg-loop-edge", DiagramLook.LoopEdge },
        { "--dg-loop-tab", DiagramLook.LoopTab },
        { "--dg-setup", DiagramLook.SetupFill },
        { "--dg-setup-edge", DiagramLook.SetupEdge },
    };

    // The light theme's value is the colour the source draws in, so the page, a copy and a picture drawn away from the
    // page (a CI summary, a server-drawn report) look alike; the dark theme has its own.
    [Theory]
    [MemberData(nameof(Tokens))]
    public void Each_frame_token_is_the_looks_colour_in_the_light_theme_and_its_own_in_the_dark(string token, string colour)
    {
        var css = Stylesheets.ReportTokensStyleSheet;
        var light = CssRules.Value(css, ":root", token);
        var dark = CssRules.Value(css, ":root[data-theme=\"dark\"]", token);

        Assert.Equal(colour, light, StringComparer.OrdinalIgnoreCase);
        Assert.NotNull(dark);
        Assert.NotEqual(light, dark, StringComparer.OrdinalIgnoreCase);
    }

    // The labels drawn on each box (a call's label in the Setup box, a loop's condition and its tab's keyword) are in the
    // diagram's ink, which must read on the box in either theme: the dark theme's light ink sat on a near-white Setup box.
    [Theory]
    [InlineData(":root", "--dg-setup")]
    [InlineData(":root", "--dg-loop")]
    [InlineData(":root", "--dg-loop-tab")]
    [InlineData(":root[data-theme=\"dark\"]", "--dg-setup")]
    [InlineData(":root[data-theme=\"dark\"]", "--dg-loop")]
    [InlineData(":root[data-theme=\"dark\"]", "--dg-loop-tab")]
    public void The_diagrams_ink_reads_on_each_box_in_each_theme(string block, string ground)
    {
        var css = Stylesheets.ReportTokensStyleSheet;
        var ink = CssRules.Value(css, block, "--dg-ink")!;
        var under = CssRules.Value(css, block, ground)!;

        var ratio = Contrast(ink, under);
        Assert.True(ratio >= 4.5, $"{block}: the diagram's ink {ink} on {ground} {under} is {ratio.ToString("0.00", CultureInfo.InvariantCulture)}:1");
    }

    private static double Contrast(string a, string b)
    {
        var (la, lb) = (Luminance(a), Luminance(b));
        return (Math.Max(la, lb) + 0.05) / (Math.Min(la, lb) + 0.05);
    }

    private static double Luminance(string hex)
    {
        var h = hex.Trim().TrimStart('#');
        double Channel(int i)
        {
            var c = int.Parse(h.Substring(i, 2), NumberStyles.HexNumber) / 255.0;
            return c <= 0.03928 ? c / 12.92 : Math.Pow((c + 0.055) / 1.055, 2.4);
        }
        return 0.2126 * Channel(0) + 0.7152 * Channel(2) + 0.0722 * Channel(4);
    }
}
