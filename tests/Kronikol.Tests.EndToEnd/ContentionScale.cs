namespace Kronikol.Tests.EndToEnd;

/// <summary>
/// Contention-scaled perf budgets for the render bench. The bench init script runs a calibration
/// worker for the whole measurement: a fixed CPU spin (calibrated to <see cref="ReferenceMs"/> on an
/// uncontended machine) every 250 ms, each duration recorded. When the machine is saturated — a CI
/// runner sharing 2 cores across the build, a laptop running a full local suite — the spin stretches
/// by the same factor as every other CPU-bound task, including the main-thread work the absolute
/// budgets meter. Scaling the budgets by that measured factor keeps them meaningful under load
/// instead of false-failing (raising the budgets chased the load and lost 3 times: 582 then 510-667
/// against WorstTaskMs 500 even with 3 retries). A real regression scales with the load too, so it
/// still breaches the scaled budget; the cap keeps a wedged runner from scaling a multi-second
/// regression into passing.
/// </summary>
internal static class ContentionScale
{
    /// <summary>What the probe's spin takes with the machine to itself. Calibrated 2026-08-30: ~18 ms
    /// (dev box, 12M-iteration sqrt loop); rounded up so typical quiet hardware lands at 1.</summary>
    public const double ReferenceMs = 20.0;

    /// <summary>Beyond this the run's numbers carry no signal — and a genuinely broken build must not
    /// be scaled into passing. Observed contention breaches needed 1.3-1.9x.</summary>
    public const double Cap = 5.0;

    /// <summary>The spin the probe worker runs; duration posted per iteration.</summary>
    public const string ProbeWorkerJs =
        "function spin(){var t0=performance.now();var x=0;for(var i=0;i<12000000;i++)x+=Math.sqrt(i);" +
        "postMessage(performance.now()-t0+(x<0?1:0));setTimeout(spin,250);}spin();";

    /// <summary>How much slower CPU-bound work ran than on a quiet machine: median probe duration over
    /// the reference, floored at 1 (never tighten), capped at <see cref="Cap"/>. No samples — no scaling.</summary>
    public static double Stretch(IReadOnlyList<double> probeMs)
    {
        if (probeMs.Count == 0)
            return 1.0;

        var sorted = probeMs.Order().ToList();
        var median = sorted[sorted.Count / 2];
        return Math.Clamp(median / ReferenceMs, 1.0, Cap);
    }
}
