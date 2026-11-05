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
    /// When the phase is <see cref="TestPhase.Unknown"/> (no phase detection, or a flow no step's phase has
    /// reached), tracking is always allowed.
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

    /// <summary>The phase a step attribute's keyword, or a hand-made step's, says. Exact case: a word it does not know says nothing.</summary>
    internal static TestPhase? PhaseOfAttribute(string? keyword) => keyword switch
    {
        "Given" or "But" => TestPhase.Setup,
        "When" or "Then" or "ButWhen" => TestPhase.Action,
        _ => null
    };

    /// <summary>
    /// What a step's keyword says about its test's phase: the phase it moves the test to, <paramref name="previous"/> for
    /// a keyword that continues the step before it, or null for no keyword or a word that says nothing about phases
    /// (a LightBDD step type of the suite's own). <c>But</c> is <c>[ButStep]</c>'s, a setup step; an adapter gives a
    /// framework's <c>But</c>, which continues the step before it, as the keyword it continues.
    /// </summary>
    internal static TestPhase? PhaseOfStep(string? keyword, TestPhase previous) => keyword?.ToUpperInvariant() switch
    {
        "GIVEN" or "SETUP" or "BUT" => TestPhase.Setup,
        "WHEN" or "THEN" or "BUTWHEN" => TestPhase.Action,
        "AND" => previous,
        _ => null
    };

    /// <summary>
    /// Maps a BDD step-type string (e.g. "Given", "When", "Then") to a <see cref="TestPhase"/>. It reads one word with
    /// nothing before it, so <c>And</c> and <c>But</c>, which continue a step, read as <see cref="TestPhase.Setup"/>; a
    /// test's steps go through <see cref="StepCollector"/>, which knows the step before.
    /// </summary>
    public static TestPhase ResolvePhaseFromStepType(string? stepType)
    {
        if (stepType is null) return TestPhase.Unknown;

        if (stepType.StartsWith("BUTWHEN", StringComparison.OrdinalIgnoreCase))
            return TestPhase.Action;

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
