using System.Net;
using System.Net.Sockets;
using Kronikol.Parties;
using Kronikol.Tracking;

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

    private async Task<RequestResponseLog[]> Send(HttpMessageHandler inner, string url = "http://cows.example/milk")
    {
        var handler = new TestTrackingMessageHandler(new TestTrackingMessageHandlerOptions
        {
            CallerName = "Breakfast Provider",
            FixedNameForReceivingService = "Cow Service",
            CurrentTestInfoFetcher = () => ("Order pancakes", _testId),
        }) { InnerHandler = inner };
        using var invoker = new HttpMessageInvoker(handler);
        try
        {
            using var response = await invoker.SendAsync(new HttpRequestMessage(HttpMethod.Get, url), CancellationToken.None);
        }
        catch (HttpRequestException)
        {
        }
        return RequestResponseLogger.RequestAndResponseLogs.Where(l => l.TestId == _testId).ToArray();
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
}
