using System.Net;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Kronikol.Tracking;
using Kronikol.xUnit3;

namespace ProbeService;

/// <summary>An in-process service, on TestServer's defaults (no execution context carried across), that makes an onward call for each request.</summary>
public static class Service
{
    public static readonly HttpClient Client = Build();

    private static HttpClient Build()
    {
        var host = new HostBuilder().ConfigureWebHost(web => web
            .UseTestServer()
            .ConfigureServices(services =>
            {
                services.AddRouting();
                services.TrackDependenciesForDiagrams(new XUnitTestTrackingMessageHandlerOptions { CallerName = "Svc", FixedNameForReceivingService = "Downstream" });
                services.AddHttpClient("onward").ConfigurePrimaryHttpMessageHandler(() => new Answers());
            })
            .Configure(app =>
            {
                app.UseRouting();
                app.UseEndpoints(e =>
                {
                    e.MapGet("/call/{name}", async (string name, IHttpClientFactory clients, HttpContext context) =>
                    {
                        // Long enough that a sibling's onward call comes in between.
                        await Task.Delay(name.Contains("slow") ? 120 : 30);
                        await clients.CreateClient("onward").GetAsync($"http://downstream/onward-of-{name}");
                        return "ok";
                    });
                    e.MapGet("/start/{name}", (string name, IHttpClientFactory clients) =>
                    {
                        // Answers at once, and goes on working: the call it makes is made after this one has been answered.
                        _ = Task.Run(async () =>
                        {
                            await Task.Delay(150);
                            await clients.CreateClient("onward").GetAsync($"http://downstream/late-of-{name}");
                        });
                        return Results.Accepted();
                    });
                });
            })).Build();
        host.Start();
        var handler = new XUnitTestTrackingMessageHandlerOptions { CallerName = "Test", FixedNameForReceivingService = "Svc" };
        return new HttpClient(new TestTrackingMessageHandler(handler) { InnerHandler = host.GetTestServer().CreateHandler() }) { BaseAddress = new Uri("http://svc") };
    }

    private sealed class Answers : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("{}") });
    }
}
