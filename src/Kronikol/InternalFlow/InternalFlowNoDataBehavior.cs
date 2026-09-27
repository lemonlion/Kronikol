namespace Kronikol;

/// <summary>
/// Controls what the report shows for scenarios that have no internal-flow span data.
/// </summary>
public enum InternalFlowNoDataBehavior
{
    /// <summary>Shows a message explaining that no span data was collected.</summary>
    ShowMessage,

    /// <summary>Hides the internal-flow link entirely.</summary>
    HideLink,

    /// <summary>Shows the link but with a visual distinction (e.g. dimmed) indicating no data. Not implemented: behaves as
    /// <see cref="ShowMessage"/>.</summary>
    VisualDistinction
}
