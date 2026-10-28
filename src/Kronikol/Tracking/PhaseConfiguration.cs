namespace Kronikol.Tracking;

/// <summary>
/// Utilities for phase-aware tracking configuration. Reads the ambient
/// <see cref="TestPhaseContext.Current"/> to determine whether to track,
/// and which verbosity level to use.
/// </summary>
public static class PhaseConfiguration
{
    /// <summary>
    /// Returns <c>true</c> if tracking should occur for the current phase.
    /// When the phase is <see cref="TestPhase.Unknown"/> (no phase detection),
    /// tracking is always allowed.
    /// </summary>
    public static bool ShouldTrack(bool trackDuringSetup, bool trackDuringAction)
    {
        return TestPhaseContext.Current switch
        {
            TestPhase.Setup => trackDuringSetup,
            TestPhase.Action => trackDuringAction,
            _ => true
        };
    }

    /// <summary>
    /// Returns the verbosity level appropriate for the current phase.
    /// Falls back to <paramref name="defaultVerbosity"/> when no override
    /// is configured for the active phase, or the phase is <see cref="TestPhase.Unknown"/>.
    /// </summary>
    public static T GetEffectiveVerbosity<T>(T defaultVerbosity, T? setupVerbosity, T? actionVerbosity) where T : struct
    {
        return TestPhaseContext.Current switch
        {
            TestPhase.Setup => setupVerbosity ?? defaultVerbosity,
            TestPhase.Action => actionVerbosity ?? defaultVerbosity,
            _ => defaultVerbosity
        };
    }

    /// <summary>
    /// The verbosities a call's notes may be drawn at: the one <see cref="GetEffectiveVerbosity{T}"/> returns, and each
    /// phase's too (its override, else <paramref name="defaultVerbosity"/>) while the phase is
    /// <see cref="TestPhase.Unknown"/> and either phase has an override, which is when
    /// <see cref="PhaseVariantExtensions.AttachVariants{TVerbosity}"/> adds a variant for each phase. A tracker reads a
    /// body when any of them shows it, and makes each note at its own verbosity from that one read.
    /// </summary>
    internal static T[] DrawnAt<T>(T defaultVerbosity, T? setupVerbosity, T? actionVerbosity) where T : struct =>
        TestPhaseContext.Current == TestPhase.Unknown && (setupVerbosity.HasValue || actionVerbosity.HasValue)
            ? [defaultVerbosity, setupVerbosity ?? defaultVerbosity, actionVerbosity ?? defaultVerbosity]
            : [GetEffectiveVerbosity(defaultVerbosity, setupVerbosity, actionVerbosity)];

    /// <summary>
    /// Maps a BDD step-type string (e.g. "Given", "When", "Then") to a <see cref="TestPhase"/>.
    /// </summary>
    public static TestPhase ResolvePhaseFromStepType(string? stepType)
    {
        if (stepType is null) return TestPhase.Unknown;

        if (stepType.StartsWith("GIVEN", StringComparison.OrdinalIgnoreCase)
            || stepType.StartsWith("AND", StringComparison.OrdinalIgnoreCase)
            || stepType.StartsWith("BUT", StringComparison.OrdinalIgnoreCase))
            return TestPhase.Setup;

        if (stepType.StartsWith("WHEN", StringComparison.OrdinalIgnoreCase)
            || stepType.StartsWith("THEN", StringComparison.OrdinalIgnoreCase))
            return TestPhase.Action;

        return TestPhase.Unknown;
    }
}
