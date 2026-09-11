namespace Kronikol.Reports;

/// <summary>
/// The font a sequence-diagram note's text starts in (<see cref="PlantUmlRendering.BrowserJs"/>
/// only). Whatever the default, readers can switch any note — or all of them — in the report.
/// <para>
/// PlantUML draws note text in a proportional font, and the payloads that most need reading are
/// written in columns: analytics SQL pads its <c>AS</c> clauses, nested <c>CASE</c>/<c>WHEN</c>
/// blocks are indented, tabular text is aligned. Measured on five note lines whose <c>AS</c> token
/// sits in the same source column, the proportional font scattered them across 65.8 pixels and a
/// monospace font put every one of them at the same x. No amount of extra width fixes that.
/// </para>
/// </summary>
public enum NoteFontFamily
{
    /// <summary>The engine's own proportional note font (the default).</summary>
    Default,

    /// <summary>
    /// A monospace font, so columns and indentation in a payload line up as they were written.
    /// Kronikol names <c>Courier New</c> specifically: the engine sizes the note box from its own
    /// metrics and the browser paints the text, and measured across seven candidates it is the only
    /// name both of them resolve. An unresolved name is not a no-op — it sizes the box with a
    /// third metric and paints a fourth font into it.
    /// </summary>
    Monospace
}

/// <summary>
/// How wide a sequence-diagram note starts (<see cref="PlantUmlRendering.BrowserJs"/> only).
/// Whatever the default, readers can widen or narrow any note in the report.
/// </summary>
public enum NoteWidthMode
{
    /// <summary>Notes wrap at the diagram's own wrap width (the default).</summary>
    Default,

    /// <summary>
    /// Notes start widened to fill the diagram's container, so a wide payload wraps as few times as
    /// it can. The target is measured from the drawn diagram's slack when the report is first
    /// rendered, and recomputed whenever the control is used again.
    /// </summary>
    Full
}
