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
        (await client.GetAsync($"http://probe.local/{label}")).EnsureSuccessStatusCode();
        tracker.TrackSendEvent("bus", label, new Uri($"bus://probe/{label}"));
    }

    /// <summary>An HTTP call and no message.</summary>
    public async Task HttpOnly(string label)
    {
        (await client.GetAsync($"http://probe.local/{label}")).EnsureSuccessStatusCode();
    }

    /// <summary>A message and no HTTP call: what a step that drives a queue or a database looks like to the handler.</summary>
    public void Message(string label) => tracker.TrackSendEvent("bus", label, new Uri($"bus://probe/{label}"));
}
