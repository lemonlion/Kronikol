using System.Net;
using System.Net.Sockets;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Kronikol.Parties;
using Kronikol.Tracking;
using static Kronikol.Tests.Tracking.ForwardingShapes;

namespace Kronikol.Tests.Parties;

/// <summary>
/// #190 R3: the tracking handler marks a call the handler at the end of its chain answered when that handler is no
/// transport (plans/PARTY_IDENTITY_PLAN.md §5.11): a stub, a fake or a mock library's handler answered in the service's
/// place. A call a transport carried is the service's answer and is not marked.
/// </summary>
[Collection("DiagramsFetcher")]
public class HandlerTestDoubleTests
{
    private readonly string _testId = Guid.NewGuid().ToString();

    private sealed class CannedCowService : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("milk") });
    }

    private sealed class RefusingCowService : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            throw new HttpRequestException("refused");
    }

    private sealed class Passing : DelegatingHandler;

    private sealed class CertificateHandler : HttpClientHandler;

    private TestTrackingMessageHandler Tracker() => new(new TestTrackingMessageHandlerOptions
    {
        CallerName = "Breakfast Provider",
        FixedNameForReceivingService = "Cow Service",
        CurrentTestInfoFetcher = () => ("Order pancakes", _testId),
    });

    private RequestResponseLog[] Logs() => RequestResponseLogger.RequestAndResponseLogs.Where(l => l.TestId == _testId).ToArray();

    private async Task<RequestResponseLog[]> Send(HttpMessageHandler inner, string url = "http://cows.example/milk")
    {
        var handler = Tracker();
        handler.InnerHandler = inner;
        using var invoker = new HttpMessageInvoker(handler);
        try
        {
            using var response = await invoker.SendAsync(new HttpRequestMessage(HttpMethod.Get, url), CancellationToken.None);
        }
        catch (HttpRequestException)
        {
        }
        return Logs();
    }

    private sealed class Looping : HttpMessageHandler
    {
        private readonly HttpMessageHandler _self;

        public Looping() => _self = this;

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(new HttpResponseMessage(_self is null ? HttpStatusCode.Gone : HttpStatusCode.OK));
    }

    private sealed class Responding(Func<HttpRequestMessage, HttpResponseMessage> respond) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(respond(request));
    }

    // A stub that keeps a transport it never sends through.
    private sealed class Hoarder : HttpMessageHandler
    {
        private readonly SocketsHttpHandler _unused = new();

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(new HttpResponseMessage(_unused is null ? HttpStatusCode.Gone : HttpStatusCode.OK));
    }

    // An interceptor used on its own, as JustEat's is from CreateHttpMessageHandler(): a delegating handler with no inner
    // handler, which can only answer the call itself.
    private sealed class Intercepting : DelegatingHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK));
    }

    // The issue's library: a delegating handler in a factory pipeline that makes its tracker on the first request, over
    // the rest of the pipeline through Onward.
    private sealed class TrackingLate(Func<TestTrackingMessageHandler> track) : DelegatingHandler
    {
        private HttpMessageInvoker? _tracked;

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            if (_tracked is null)
            {
                var tracker = track();
                tracker.InnerHandler = new Onward(InnerHandler!);
                _tracked = new HttpMessageInvoker(tracker);
            }
            return _tracked.SendAsync(request, cancellationToken);
        }
    }

    /// <summary>A server on this machine that answers one request with an empty 200, so a transport has somewhere to go.</summary>
    private static (string Url, Task Served) OneAnswer()
    {
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        var served = Task.Run(async () =>
        {
            try
            {
                using var client = await listener.AcceptTcpClientAsync();
                using var stream = client.GetStream();
                var buffer = new byte[8192];
                _ = await stream.ReadAsync(buffer);
                await stream.WriteAsync("HTTP/1.1 200 OK\r\nContent-Length: 0\r\nConnection: close\r\n\r\n"u8.ToArray());
            }
            finally
            {
                listener.Stop();
            }
        });
        return ($"http://127.0.0.1:{port}/milk", served);
    }

    [Fact]
    public async Task A_call_a_stub_answered_is_marked_with_the_stubs_type_on_both_halves()
    {
        var logs = await Send(new CannedCowService());

        Assert.Equal([RequestResponseType.Request, RequestResponseType.Response], logs.Select(l => l.Type));
        Assert.All(logs, l => Assert.Equal("CannedCowService", l.TestDouble));
    }

    [Fact]
    public async Task The_handler_looks_past_delegating_handlers_to_the_end_of_the_chain()
    {
        var logs = await Send(new Passing { InnerHandler = new Passing { InnerHandler = new CannedCowService() } });

        Assert.Equal(2, logs.Length);
        Assert.All(logs, l => Assert.Equal("CannedCowService", l.TestDouble));
    }

    [Fact]
    public async Task A_call_that_threw_in_a_stub_is_marked_too()
    {
        var logs = await Send(new RefusingCowService());

        Assert.Equal([RequestResponseType.Request, RequestResponseType.Response], logs.Select(l => l.Type));
        Assert.All(logs, l => Assert.Equal("RefusingCowService", l.TestDouble));
    }

    // A transport carries the call to the service: the socket handler, the client handler and any handler built on it.
    [Theory]
    [InlineData("sockets")]
    [InlineData("client")]
    [InlineData("derived")]
    public async Task A_call_a_transport_carried_is_not_marked(string transport)
    {
        HttpMessageHandler inner = transport switch
        {
            "sockets" => new SocketsHttpHandler(),
            "client" => new HttpClientHandler(),
            _ => new CertificateHandler(),
        };
        var (url, served) = OneAnswer();

        var logs = await Send(new Passing { InnerHandler = inner }, url);
        await served;

        Assert.Equal([RequestResponseType.Request, RequestResponseType.Response], logs.Select(l => l.Type));
        Assert.Equal(HttpStatusCode.OK, logs[1].StatusCode?.Value);
        Assert.All(logs, l => Assert.Null(l.TestDouble));
    }

    // The calls a stub answered make their party a double, as the capturers' marks do (R2), with nothing declared.
    [Fact]
    public async Task A_service_only_a_stub_answered_is_a_double_observed()
    {
        var logs = await Send(new CannedCowService());

        var table = PartyResolver.Resolve(PartiesReport.CallsOf(logs), PartyDeclarations.None, PartyObservations.None);

        var cow = table.Parties.Single(p => p.Name == "Cow Service");
        Assert.Equal((PartyKind.Double, (PartyKindSource?)PartyKindSource.Observed), (cow.Kind, cow.KindSource));
    }

    // #219: a forwarder hands the call on through an invoker or a client it holds, and the handler follows it there.
    [Theory]
    [InlineData(nameof(Onward))]
    [InlineData(nameof(SendingThrough))]
    public async Task A_call_a_forwarder_hands_to_a_server_on_this_machine_is_not_marked(string forwarder)
    {
        var (url, served) = OneAnswer();

        var logs = await Send(Forwarder(forwarder, new SocketsHttpHandler()), url);
        await served;

        Assert.Equal([RequestResponseType.Request, RequestResponseType.Response], logs.Select(l => l.Type));
        Assert.Equal(HttpStatusCode.OK, logs[1].StatusCode?.Value);
        Assert.All(logs, l => Assert.Null(l.TestDouble));
    }

    [Theory]
    [InlineData(nameof(Onward))]
    [InlineData(nameof(SendingThrough))]
    public async Task A_call_a_forwarder_hands_to_TestServer_is_not_marked(string forwarder)
    {
        await using var app = await InProcess();
        var server = app.GetTestServer();

        // The factory-style client: delegating handlers over TestServer's, as WebApplicationFactory.CreateClient() builds.
        var logs = await Send(forwarder == nameof(Onward)
            ? new Onward(server.CreateHandler())
            : new SendingThrough(new HttpClient(new Passing { InnerHandler = new Passing { InnerHandler = server.CreateHandler() } })),
            "http://localhost/milk");

        Assert.Equal(HttpStatusCode.OK, logs[1].StatusCode?.Value);
        Assert.All(logs, l => Assert.Null(l.TestDouble));
    }

    [Theory]
    [InlineData(nameof(Onward))]
    [InlineData(nameof(SendingThrough))]
    public async Task A_call_a_forwarder_hands_to_a_stub_is_marked_with_the_stubs_type(string forwarder)
    {
        var logs = await Send(Forwarder(forwarder, new CannedCowService()));

        Assert.Equal([RequestResponseType.Request, RequestResponseType.Response], logs.Select(l => l.Type));
        Assert.All(logs, l => Assert.Equal("CannedCowService", l.TestDouble));
    }

    [Fact]
    public async Task A_forwarder_over_an_IHttpClientFactory_pipeline_is_followed_through_its_delegating_handlers()
    {
        var (url, served) = OneAnswer();
        var services = new ServiceCollection();
        services.AddHttpClient("cows").AddHttpMessageHandler(() => new TrackingLate(Tracker));
        await using var provider = services.BuildServiceProvider();

        using var response = await provider.GetRequiredService<IHttpClientFactory>().CreateClient("cows").GetAsync(url);
        await served;

        var logs = Logs();
        Assert.Equal([RequestResponseType.Request, RequestResponseType.Response], logs.Select(l => l.Type));
        Assert.All(logs, l => Assert.Null(l.TestDouble));
    }

    // Every way on is followed: a mark only when every end answers the call itself, then the first end's name.
    [Theory]
    [InlineData("two stubs", "CannedCowService")]
    [InlineData("a stub and sockets", null)]
    public async Task A_call_with_two_ways_on_is_marked_only_when_both_answer_and_then_by_the_first(string ways, string? marked)
    {
        var (url, served) = OneAnswer();
        var router = ways == "two stubs"
            ? new Router(new CannedCowService(), new RefusingCowService())
            : new Router(new SocketsHttpHandler(), new CannedCowService());

        var logs = await Send(router, url);
        if (marked is null)
            await served;

        Assert.Equal(2, logs.Length);
        Assert.All(logs, l => Assert.Equal(marked, l.TestDouble));
    }

    // A handler the walk has passed is no way onward, so a stub that holds itself still answers.
    [Fact]
    public async Task A_handler_that_holds_only_itself_answers_the_call()
    {
        var logs = await Send(new Looping());

        Assert.Equal(2, logs.Length);
        Assert.All(logs, l => Assert.Equal("Looping", l.TestDouble));
    }

    // Delegates are not followed (plans/WHAT_ANSWERS_PLAN.md 5.8 d): a responder that closes over the test class would
    // read as a way on to the test's own client.
    [Fact]
    public async Task A_stub_whose_answer_is_a_delegate_is_still_marked()
    {
        using var client = new HttpClient();
        var logs = await Send(new Responding(_ => new HttpResponseMessage(client.Timeout > TimeSpan.Zero ? HttpStatusCode.OK : HttpStatusCode.Gone)));

        Assert.Equal(2, logs.Length);
        Assert.All(logs, l => Assert.Equal("Responding", l.TestDouble));
    }

    [Fact]
    public async Task A_stub_that_holds_a_transport_is_read_as_handing_calls_to_it()
    {
        var logs = await Send(new Hoarder());

        Assert.Equal(2, logs.Length);
        Assert.All(logs, l => Assert.Null(l.TestDouble));
    }

    [Fact]
    public async Task A_chain_longer_than_the_walk_follows_marks_nothing()
    {
        HttpMessageHandler chain = new CannedCowService();
        for (var i = 0; i < 65; i++)
            chain = new Onward(chain);

        var logs = await Send(chain);

        Assert.Equal(2, logs.Length);
        Assert.All(logs, l => Assert.Null(l.TestDouble));
    }

    // Q4: a delegating handler with no inner handler is the end of its chain and can only answer the call itself.
    [Fact]
    public async Task A_delegating_handler_with_no_inner_handler_answers_the_call()
    {
        var logs = await Send(new Intercepting());

        Assert.Equal([RequestResponseType.Request, RequestResponseType.Response], logs.Select(l => l.Type));
        Assert.All(logs, l => Assert.Equal("Intercepting", l.TestDouble));
    }

    [Fact]
    public async Task A_service_reached_only_through_a_forwarder_to_a_real_server_is_not_a_double()
    {
        var (url, served) = OneAnswer();
        var logs = await Send(new Onward(new SocketsHttpHandler()), url);
        await served;

        var table = PartyResolver.Resolve(PartiesReport.CallsOf(logs), PartyDeclarations.None, PartyObservations.None);

        // Unmarked, the one service the run reached is what the inference reads as the subject; marked, it was a double.
        Assert.NotEqual(PartyKind.Double, table.Parties.Single(p => p.Name == "Cow Service").Kind);
    }
}
