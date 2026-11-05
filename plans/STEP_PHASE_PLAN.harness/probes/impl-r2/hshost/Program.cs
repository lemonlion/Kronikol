using System.Net;
using System.Threading.Channels;

var builder = WebApplication.CreateBuilder(args);
builder.Services.AddHttpClient("down").ConfigurePrimaryHttpMessageHandler(() => new AnswerDown());
builder.Services.AddSingleton<IPublish, NoPublish>();
builder.Services.AddSingleton(Channel.CreateUnbounded<string>());
builder.Services.AddHostedService<Worker>();
var app = builder.Build();
app.MapGet("/call/{label}", async (string label, IHttpClientFactory f) =>
{
    (await f.CreateClient("down").GetAsync($"http://down.local/{label}-down")).EnsureSuccessStatusCode();
    return Results.Ok(new { ok = true });
});
app.MapGet("/nocall/{label}", (string label) => Results.Ok(new { ok = true }));
app.MapGet("/message/{label}", (string label, IPublish p) => { p.Send(label); return Results.Ok(new { ok = true }); });
app.MapGet("/queue/{label}", (string label, Channel<string> c) => { c.Writer.TryWrite(label); return Results.Accepted(); });
app.Run();

public partial class Program { }

public interface IPublish { void Send(string label); }

public sealed class NoPublish : IPublish { public void Send(string label) { } }

public sealed class AnswerDown : HttpMessageHandler
{
    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        await Task.Delay(2, cancellationToken);
        return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("{\"ok\":true}") };
    }
}

/// <summary>Takes its work from a queue, so its call has no request on its flow and lands after the request that queued it.</summary>
public sealed class Worker(Channel<string> channel, IHttpClientFactory factory) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await foreach (var label in channel.Reader.ReadAllAsync(stoppingToken))
        {
            await Task.Delay(40, stoppingToken);
            await factory.CreateClient("down").GetAsync($"http://down.local/{label}-worker", stoppingToken);
        }
    }
}
