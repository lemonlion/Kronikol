namespace Kronikol.Reports;

/// <summary>
/// How wide a sequence-diagram note starts (<see cref="PlantUmlRendering.BrowserJs"/> only).
/// Whatever the default, readers can widen or narrow any note in the report. The report's note-width
/// dropdown calls the two states <c>Wrap</c> and <c>Wide</c>.
/// </summary>
public enum NoteWidthMode
{
    /// <summary>
    /// Notes wrap at the diagram's own wrap width,
    /// <see cref="ReportConfigurationOptions.DiagramNoteWrapWidth"/> (the default). <c>Wrap</c> in the
    /// report's dropdown.
    /// </summary>
    Default,

    /// <summary>
    /// Notes start widened to fill the diagram's container, so a wide payload wraps as few times as
    /// it can. The target is measured from the drawn diagram's slack when the report is first
    /// rendered, and recomputed whenever the control is used again. <c>Wide</c> in the report's
    /// dropdown. Only a note that wraps changes size; one that already fits stays as it is.
    /// </summary>
    Full
}

