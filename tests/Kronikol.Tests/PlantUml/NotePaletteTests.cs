using Kronikol.PlantUml;

namespace Kronikol.Tests.PlantUml;

/// <summary>
/// The ink Kronikol writes a note's header lines in (DIAGRAM_COLOURS_PLAN S1). It was <c>gray</c>, which
/// PlantUML paints <c>#808080</c>: 3.87 : 1 on the default note fill and 3.20 on the event note, below the
/// 4.5 WCAG 2.1 AA asks of text under 18 pt (the note body is 13 px). The value is computed from the fills,
/// not typed in, so a change to either fill moves the header with it and these facts say by how much.
/// </summary>
public class NotePaletteTests
{
    [Fact]
    public void The_header_ink_is_the_lightest_grey_that_clears_aa_on_both_note_fills()
    {
        Assert.Equal("#686868", NotePalette.HeaderInk);
        Assert.Equal("<color:#686868>", NotePalette.HeaderTag);
    }

    [Theory]
    [InlineData("#808080", "#FEFFDD", 3.87)] // the audit's numbers, so the formula is the audit's
    [InlineData("#808080", "#CFECF7", 3.20)]
    [InlineData("#686868", "#FEFFDD", 5.46)]
    [InlineData("#686868", "#CFECF7", 4.51)]
    [InlineData("#000000", "#FFFFFF", 21.00)]
    [InlineData("#FFFFFF", "#000000", 21.00)] // the order of the two colours does not matter
    public void The_ratio_is_wcag_2_1_contrast(string foreground, string background, double expected)
    {
        Assert.Equal(expected, WcagContrast.Ratio(foreground, background), 2);
    }

    [Fact]
    public void The_header_ink_clears_the_floor_on_every_fill_a_header_line_lands_on()
    {
        Assert.Equal(4.5, NotePalette.HeaderContrastFloor);
        Assert.True(WcagContrast.Ratio(NotePalette.HeaderInk, NotePalette.DefaultNoteFill) >= NotePalette.HeaderContrastFloor);
        Assert.True(WcagContrast.Ratio(NotePalette.HeaderInk, NotePalette.EventNoteFill) >= NotePalette.HeaderContrastFloor);
    }

    [Fact]
    public void One_step_lighter_than_the_header_ink_fails_the_floor_on_the_event_note()
    {
        Assert.True(WcagContrast.Ratio("#696969", NotePalette.EventNoteFill) < NotePalette.HeaderContrastFloor);
    }

    [Fact]
    public void The_event_note_is_what_moves_the_answer()
    {
        // The default note alone would settle on #757575; the event note's darker fill takes it to #686868.
        Assert.Equal("#757575", WcagContrast.LightestGreyClearing(4.5, 0x80, NotePalette.DefaultNoteFill));
        Assert.Equal("#686868", WcagContrast.LightestGreyClearing(4.5, 0x80, NotePalette.DefaultNoteFill, NotePalette.EventNoteFill));
    }

    [Fact]
    public void The_walk_never_answers_lighter_than_where_it_starts()
    {
        // A floor the start already clears answers the start itself, never a lighter grey.
        Assert.Equal("#808080", WcagContrast.LightestGreyClearing(3.0, 0x80, NotePalette.DefaultNoteFill));
        // A floor nothing clears answers black.
        Assert.Equal("#000000", WcagContrast.LightestGreyClearing(25, 0x80, NotePalette.DefaultNoteFill));
    }

    [Fact]
    public void The_header_ink_still_reads_as_quieter_than_the_body()
    {
        // Secondary, not hidden: the body's black is far above the header on the same fill.
        Assert.True(WcagContrast.Ratio("#000000", NotePalette.DefaultNoteFill) > 3 * WcagContrast.Ratio(NotePalette.HeaderInk, NotePalette.DefaultNoteFill));
    }
}
