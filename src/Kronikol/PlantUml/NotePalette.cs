namespace Kronikol.PlantUml;

/// <summary>The inks Kronikol writes into notes, derived from the fills they land on.</summary>
internal static class NotePalette
{
    /// <summary>PlantUML's own note fill, unthemed, as the pinned engine paints it.</summary>
    internal const string DefaultNoteFill = "#FEFFDD";

    /// <summary>What <c>AddEventStyling</c> gives the event note.</summary>
    internal const string EventNoteFill = "#CFECF7";

    /// <summary>WCAG 2.1 AA for text under 18 pt. The note body is 13 px.</summary>
    internal const double HeaderContrastFloor = 4.5;

    /// <summary>
    /// The ink of a note's header lines (its headers, and the <c>[Full path]</c> block): the lightest
    /// neutral grey that clears <see cref="HeaderContrastFloor"/> on every fill a header line lands on,
    /// found by walking down from <c>gray</c> (<c>#808080</c>), the ink before 3.30.0. It comes out as
    /// <c>#686868</c>. The report's scripts recognise a header line by this tag, and accept <c>gray</c> too,
    /// since a merged report holds sources written before 3.30.0.
    /// </summary>
    internal static readonly string HeaderInk = WcagContrast.LightestGreyClearing(HeaderContrastFloor, 0x80, DefaultNoteFill, EventNoteFill);

    /// <summary>The creole tag that starts a header line.</summary>
    internal static readonly string HeaderTag = "<color:" + HeaderInk + ">";
}
