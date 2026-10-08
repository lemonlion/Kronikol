using System.Text;
using Kronikol.Tracking;

namespace Probe133;

/// <summary>
/// Records, at one point of a test framework's lifecycle, what each of Kronikol's identity readers answers:
/// the adapter's CurrentTestInfo.Fetcher, the resolver chain over it, Track.TestIdResolver, and where the
/// adapter's TrackingDiagramOverride.InsertPlantUml logs its marker (or what it throws).
/// </summary>
public static class Probe
{
    private static readonly object Gate = new();
    private static readonly List<string> Lines = new();

    public static Func<(string Name, string Id)> Fetcher = () => throw new InvalidOperationException("no fetcher set");
    public static Action<string> InsertPlantUml = _ => throw new InvalidOperationException("no override set");
    public static Func<string> Context = () => "";

    public static void Record(string where)
    {
        var sb = new StringBuilder();
        sb.AppendLine(where);
        sb.AppendLine("  context:        " + Try(Context));
        sb.AppendLine("  fetcher:        " + Try(() => Fetcher().ToString()));
        sb.AppendLine("  resolver:       " + Try(() =>
            TestInfoResolver.ResolveWithSource(null, Fetcher) is { } t ? $"({t.Name}, {t.Id}, {t.Source})" : "null"));
        sb.AppendLine("  TestIdResolver: " + (Track.TestIdResolver is null ? "<unset>" : Try(() => Track.TestIdResolver() ?? "null")));
        var marker = "probe-" + Guid.NewGuid().ToString("N");
        var outcome = Try(() => { InsertPlantUml(marker); return "ok"; });
        var logged = RequestResponseLogger.RequestAndResponseLogs
            .Where(l => l.PlantUml?.Contains(marker) == true)
            .Select(l => l.TestId).Distinct().ToArray();
        sb.AppendLine($"  override:       {outcome}; marker logged under: {(logged.Length == 0 ? "nothing" : string.Join(",", logged))}");
        lock (Gate) Lines.Add(sb.ToString());
    }

    /// <summary>Runs the action on a new thread that inherits no execution context (no AsyncLocal flows).</summary>
    public static void OnThreadWithNoFlow(Action action)
    {
        Thread thread;
        using (ExecutionContext.SuppressFlow())
        {
            thread = new Thread(() => action());
            thread.Start();
        }
        thread.Join();
    }

    public static void ProbeOtherFlows(string prefix)
    {
        OnThreadWithNoFlow(() => Record($"{prefix}: a thread with no flowed context"));
        OnThreadWithNoFlow(() =>
        {
            using (TestIdentityScope.Begin("Scoped", "scoped-id"))
                Record($"{prefix}: a thread with no flowed context, inside TestIdentityScope.Begin(Scoped, scoped-id)");
        });
        TestIdentityScope.SetGlobalFallback("Fallback", "fallback-id");
        try
        {
            OnThreadWithNoFlow(() => Record($"{prefix}: a thread with no flowed context, GlobalFallback set (fallback-id)"));
        }
        finally
        {
            TestIdentityScope.ClearGlobalFallback();
        }
        Task.Run(() => Record($"{prefix}: Task.Run (flows the context)")).Wait();
    }

    public static void Flush(string fileName)
    {
        var path = Path.Combine(AppContext.BaseDirectory, fileName);
        lock (Gate) File.WriteAllText(path, string.Join("", Lines));
    }

    private static string Try(Func<string> f)
    {
        try { return f(); }
        catch (Exception e) { return $"THROWS {e.GetType().Name}: {e.Message}"; }
    }
}
