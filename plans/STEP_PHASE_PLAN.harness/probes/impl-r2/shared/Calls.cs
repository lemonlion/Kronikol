using System.Net;
using Kronikol.Tracking;

namespace PhaseProbe;

/// <summary>Answers every request, and really awaits, as one that answers at once hides overlaps.</summary>
public sealed class AnswerEverything : HttpMessageHandler
{
    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        await Task.Delay(2, cancellationToken);
        return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("{\"ok\":true}") };
    }
}

public sealed class Calls(HttpClient client, MessageTracker tracker)
{
    public HttpClient Client => client;

    public async Task Make(string label)
    {
        (await client.GetAsync($"http://probe.local/{label}")).EnsureSuccessStatusCode();
    }

    public void Message(string label) => tracker.TrackSendEvent("bus", label, new Uri($"bus://probe/{label}"), new { label, payload = "body-" + label });
}
