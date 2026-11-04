using ProbeService;

var builder = WebApplication.CreateBuilder(args);
builder.Services.AddHttpClient("downstream");
builder.Services.AddSingleton<IEvents, NoEvents>();
var app = builder.Build();

// One call the test makes, and under it a call this service makes downstream and an event it sends.
app.MapGet("/{label}", async (string label, IHttpClientFactory clients, IEvents events) =>
{
    await Task.Delay(1);
    (await clients.CreateClient("downstream").GetAsync($"http://downstream.local/svc-{label}")).EnsureSuccessStatusCode();
    events.Published($"svcmsg-{label}");
    return Results.Ok(new { label });
});

app.Run();

public partial class Program;

namespace ProbeService
{
    public interface IEvents
    {
        void Published(string label);
    }

    public sealed class NoEvents : IEvents
    {
        public void Published(string label) { }
    }
}
