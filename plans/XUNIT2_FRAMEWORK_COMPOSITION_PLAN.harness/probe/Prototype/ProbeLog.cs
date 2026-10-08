using System.Collections.Concurrent;
using System.Diagnostics;
using Kronikol.xUnit2;

namespace Probe.Prototype;

/// <summary>
/// Instrumentation only (not part of the design). With PROBE_LOG set, every event is queued in memory and
/// written on ITestAssemblyFinished: &lt;PROBE_LOG&gt; holds one row per event (seq, microseconds since start,
/// managed thread id, event, test display name or "?", detail) and &lt;PROBE_LOG&gt;.scenarios.tsv one row per
/// ITest. With PROBE_TIMING set, one line of timings is appended there. Neither set: nothing is recorded.
/// </summary>
internal static class ProbeLog
{
    private static readonly ConcurrentQueue<(long Seq, string Line)> Lines = new();
    private static readonly long Origin = Stopwatch.GetTimestamp();
    private static string? _path;
    private static long _seq;

    public static void Init()
    {
        _path = Environment.GetEnvironmentVariable("PROBE_LOG");
        if (_path is null)
            return;

        // Inside the test method, before its call: is the scenario the sink set on ITestStarting visible here?
        ProbeHooks.InBody = path =>
        {
            var scenario = PrototypeContext.Current;
            var (name, id) = XUnit2TestTrackingContext.GetCurrentTestInfo();
            Write("body", scenario?.Test.DisplayName ?? "?",
                $"visible={(scenario is null ? 0 : 1)} path={path} trackingId={(name == "Unknown Test" ? "-" : id)}");
        };
    }

    public static void Write(string evt, string test, string detail)
    {
        if (_path is null)
            return;
        var seq = Interlocked.Increment(ref _seq);
        var micros = (Stopwatch.GetTimestamp() - Origin) * 1_000_000 / Stopwatch.Frequency;
        Lines.Enqueue((seq, $"{seq}\t{micros}\t{Environment.CurrentManagedThreadId}\t{evt}\t{Clean(test)}\t{Clean(detail)}"));
    }

    public static void Timing(decimal assemblyExecutionSeconds, long reportMilliseconds, int scenarios)
    {
        var path = Environment.GetEnvironmentVariable("PROBE_TIMING");
        if (path is not null)
            File.AppendAllText(path,
                $"assemblyExecutionSeconds={assemblyExecutionSeconds:0.000} reportMilliseconds={reportMilliseconds} scenarios={scenarios}{Environment.NewLine}");
        Write("report", "-", $"assemblyExecutionSeconds={assemblyExecutionSeconds:0.000} reportMilliseconds={reportMilliseconds} scenarios={scenarios}");
    }

    public static void Flush(IEnumerable<ProbeScenario> scenarios)
    {
        if (_path is null)
            return;
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(_path))!);
        File.WriteAllLines(_path,
            new[] { "seq\tmicros\tthread\tevent\ttest\tdetail" }.Concat(Lines.OrderBy(l => l.Seq).Select(l => l.Line)));
        File.WriteAllLines(Path.ChangeExtension(_path, ".scenarios.tsv"),
            new[] { "test\tsawStarting\tresult\texecutionSeconds\tbeforeAttributes\ttrackingId\townId" }.Concat(
                scenarios.Select(s =>
                    $"{Clean(s.Test.DisplayName)}\t{s.SawStarting}\t{s.Result?.ToString() ?? "none"}\t{s.ExecutionTime:0.000000}\t{string.Join(",", s.BeforeAttributes)}\t{s.TrackingId ?? "-"}\t{s.OwnId}")));
    }

    private static string Clean(string value) => value.Replace('\t', ' ').Replace('\r', ' ').Replace('\n', ' ');
}
