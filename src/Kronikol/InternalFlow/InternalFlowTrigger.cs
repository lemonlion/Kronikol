namespace Kronikol;

/// <summary>
/// The user interaction that reveals the internal-flow visualization for a scenario.
/// </summary>
public enum InternalFlowTrigger
{
    /// <summary>Show the visualization when the user clicks the scenario.</summary>
    Click,

    /// <summary>Show the visualization when the user hovers over the scenario. Not implemented: behaves as <see cref="Click"/>.</summary>
    Hover
}
