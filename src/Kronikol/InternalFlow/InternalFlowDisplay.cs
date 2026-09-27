namespace Kronikol;

/// <summary>
/// Controls how the internal-flow visualization is displayed in the report.
/// </summary>
public enum InternalFlowDisplay
{
    /// <summary>The visualization opens in a popup/modal overlay.</summary>
    Popup,

    /// <summary>The visualization is rendered inline within the scenario section. Not implemented: behaves as <see cref="Popup"/>.</summary>
    Inline
}
