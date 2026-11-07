using System.Net;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Logging;

namespace Kronikol.Tests.Tracking;

/// <summary>
/// The ways a handler hands a call on other than as a delegating handler (#219, plans/WHAT_ANSWERS_PLAN.md), as suites
/// write them: the issue's two forwarders and a router, and an app hosted on TestServer for them to reach.
/// </summary>
internal static class ForwardingShapes
{
    /// <summary>
    /// The issue's first forwarder, as its library wrote it: the rest of the pipeline behind an invoker it holds, as the
    /// pipeline disposes that handler itself.
    /// </summary>
    internal sealed class Onward(HttpMessageHandler next) : HttpMessageHandler
    {
        private readonly HttpMessageInvoker _next = new(next, disposeHandler: false);

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            _next.SendAsync(request, cancellationToken);
    }

    /// <summary>The issue's second: a copy of each request through a client the test already holds.</summary>
    internal sealed class SendingThrough(HttpClient client) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var copy = new HttpRequestMessage(request.Method, request.RequestUri) { Content = request.Content };
            foreach (var header in request.Headers)
                copy.Headers.TryAddWithoutValidation(header.Key, header.Value);
            return client.SendAsync(copy, cancellationToken);
        }
    }

    /// <summary>A router of two ways on that sends every call down the first.</summary>
    internal sealed class Router(HttpMessageHandler first, HttpMessageHandler second) : HttpMessageHandler
    {
        private readonly HttpMessageInvoker _first = new(first, disposeHandler: false);
        private readonly HttpMessageInvoker _second = new(second, disposeHandler: false);

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            _first.SendAsync(request, cancellationToken);
    }

    /// <summary><see cref="Onward"/> or <see cref="SendingThrough"/>, by name, over <paramref name="next"/>.</summary>
    internal static HttpMessageHandler Forwarder(string forwarder, HttpMessageHandler next) =>
        forwarder == nameof(Onward) ? new Onward(next) : new SendingThrough(new HttpClient(next));

    /// <summary>An app hosted on TestServer that answers every request with an empty 200, as an app under test does in process.</summary>
    internal static async Task<WebApplication> InProcess()
    {
        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseTestServer();
        builder.Logging.ClearProviders();
        var app = builder.Build();
        app.Run(context =>
        {
            context.Response.StatusCode = (int)HttpStatusCode.OK;
            return Task.CompletedTask;
        });
        await app.StartAsync();
        return app;
    }
}
