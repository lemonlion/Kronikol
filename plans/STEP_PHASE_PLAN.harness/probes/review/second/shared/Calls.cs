using System.Net;
using Kronikol.Tracking;
namespace PhaseProbe;
public sealed class AnswerEverything : HttpMessageHandler
{
    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
        Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("{}") });
}
/// <summary>Answers after a real wait, as any client over a socket does.</summary>
public sealed class AnswerAfterAWait : HttpMessageHandler
{
    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        await Task.Delay(30, cancellationToken);
        return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("{}") };
    }
}
public sealed class Calls(HttpClient client, MessageTracker tracker)
{
    public async Task Make(string label)
    {
        (await client.GetAsync($"http://probe.local/{label}")).EnsureSuccessStatusCode();
        tracker.TrackSendEvent("bus", label, new Uri($"bus://probe/{label}"));
    }
    public void Message(string label) => tracker.TrackSendEvent("bus", label, new Uri($"bus://probe/{label}"));
}
