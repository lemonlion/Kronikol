namespace Kronikol.Tracking;

/// <summary>
/// Identifies the current phase of a test scenario for phase-aware tracking configuration.
/// </summary>
public enum TestPhase
{
    /// <summary>No phase detection active — phase-specific overrides are ignored.</summary>
    Unknown = 0,

    /// <summary>The setup phase (BDD Given steps and the steps that continue them, or before <c>StartAction()</c>).</summary>
    Setup = 1,

    /// <summary>The action phase (BDD When/Then steps and the steps that continue them, or after <c>StartAction()</c>).</summary>
    Action = 2
}
