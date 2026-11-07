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

    /// <summary>
    /// The ink of a failed answer's reason (plans/FAILED_CALLS_PLAN.md R3): a named colour, so the scripts' header test
    /// (<c>NOTE_HEADER_TAG</c>, <c>gray</c> or a six-digit hex) never takes a reason line for a header line, which the page
    /// hides by default. The engine draws it as <see cref="ReasonInkDrawn"/>, which clears
    /// <see cref="HeaderContrastFloor"/> on both note fills, and the page paints that from its theme (<c>--dg-fail-ink</c>).
    /// </summary>
    internal const string ReasonInk = "firebrick";

    /// <summary>The fill the engine writes for <see cref="ReasonInk"/>, which <c>inline-svg-styles.css</c> paints from the theme.</summary>
    internal const string ReasonInkDrawn = "#B22222";

    /// <summary>The creole tag that opens each line of a reason; <see cref="ReasonClose"/> ends it.</summary>
    internal const string ReasonTag = "<color:" + ReasonInk + ">";

    internal const string ReasonClose = "</color>";
}
