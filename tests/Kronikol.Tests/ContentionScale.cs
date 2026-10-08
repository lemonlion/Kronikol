using System.Diagnostics;
using System.Text.Json;

namespace Kronikol.Tests;

/// <summary>
/// How much slower this machine is running right now than an idle one, for a wall-clock budget to scale by, as the
/// Playwright project's render budgets are (its own <c>ContentionScale</c>). A budget that holds on an idle machine fails
/// a fast reader while other suites run: <c>HistoryLedgerTests</c>' read budget failed at 1,795 ms and at 2,372 ms
/// against 1,500 on 2026-10-08, for a read that takes about 100 ms alone.
/// </summary>
internal static class ContentionScale
{
    /// <summary>The probe's median on an idle machine: 9 to 12 ms, measured on the machine the budgets were set on.</summary>
    public const double ReferenceMs = 10.0;

    /// <summary>The most a budget is stretched, so a reader that really slowed down still fails on a busy machine.</summary>
    public const double Cap = 5.0;

    private static readonly string ProbeJson =
        "[" + string.Join(",", Enumerable.Range(0, 200_000).Select(i => $"\"{i:x16}\"")) + "]";

    /// <summary>Times the probe five times, after one untimed run that pays for its compilation, and stretches by the median.</summary>
    public static double Measure()
    {
        Probe();
        var samples = new double[5];
        for (var i = 0; i < samples.Length; i++)
        {
            var watch = Stopwatch.StartNew();
            Probe();
            samples[i] = watch.Elapsed.TotalMilliseconds;
        }

        return Stretch(samples);
    }

    public static double Stretch(IReadOnlyList<double> probeMs)
    {
        if (probeMs.Count == 0)
            return 1.0;

        var sorted = probeMs.Order().ToList();
        return Math.Clamp(sorted[sorted.Count / 2] / ReferenceMs, 1.0, Cap);
    }

    // Parsing and allocating, as the readers it scales do: a collector kept busy by other suites slows them, and it
    // would not slow a tight arithmetic loop.
    private static void Probe()
    {
        using var document = JsonDocument.Parse(ProbeJson);
        var length = 0L;
        foreach (var element in document.RootElement.EnumerateArray())
            length += element.GetString()!.Length;
        GC.KeepAlive(length);
    }
}
