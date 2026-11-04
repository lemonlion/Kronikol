using System.Collections.Concurrent;
using System.Net;
using Kronikol.Tracking;

namespace PhaseProbe;

/// <summary>Answers every request, so a tracked client needs no server.</summary>
public sealed class AnswerEverything : HttpMessageHandler
{
    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        // A real call yields, so a step that makes one is still open when its stub returns.
        await Task.Delay(1, cancellationToken);
        return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("{}") };
    }
}

/// <summary>
/// One labelled HTTP call and one labelled message per point in a test. The message tracker reads the ambient phase as it
/// stands in this flow; the HTTP handler can change it inside its own SendAsync first (its implicit action start), so the
/// two columns of a dump differ exactly where that happens.
/// </summary>
public sealed class Calls(HttpClient client, MessageTracker tracker)
{
    public async Task Make(string label)
    {
        FlowTrace.Note(label);
        (await client.GetAsync($"http://probe.local/{label}")).EnsureSuccessStatusCode();
        tracker.TrackSendEvent("bus", label, new Uri($"bus://probe/{label}"));
    }

    /// <summary>A message and no HTTP call: what a step that drives a queue or a database looks like to the handler.</summary>
    public void Message(string label) => FlowTraced(label, () => tracker.TrackSendEvent("bus", label, new Uri($"bus://probe/{label}")));

    private static void FlowTraced(string label, Action work)
    {
        FlowTrace.Note(label);
        work();
    }
}


/// <summary>
/// What a flow sees at each labelled point, with no tracker in between: the ambient phase, the id Track's resolver answers
/// (which is what StepCollector goes by), the scope's. A point with no id appears here and in no test of the dump, so it tells a silent
/// no-op from a phase, and a hook's note says the hook ran at all.
/// </summary>
public static class FlowTrace
{
    private static readonly ConcurrentQueue<string> Lines = new();

    public static void Note(string label)
    {
        string resolver;
        try { resolver = Track.TestIdResolver?.Invoke() ?? "none"; }
        catch (Exception e) { resolver = "threw " + e.GetType().Name; }
        var scope = TestIdentityScope.Current?.Id ?? "none";
        Lines.Enqueue($"{label,-44} ambient={TestPhaseContext.Current,-8} resolver={Short(resolver),-12} scope={Short(scope),-12} thread={Environment.CurrentManagedThreadId}");
    }

    private static string Short(string id) => id.Length <= 12 ? id : id[..12];

    public static IEnumerable<string> All => Lines;
}
