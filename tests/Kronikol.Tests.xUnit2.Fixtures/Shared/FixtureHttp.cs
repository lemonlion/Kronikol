using System.Net;
using System.Text;
using Kronikol.Tracking;
using Kronikol.xUnit2;
using Xunit.Abstractions;

namespace Kronikol.Tests.xUnit2.Fixtures;

/// <summary>
/// One tracked HttpClient for every fixture test: Kronikol's TestTrackingMessageHandler in front of an
/// in-process stub. Each test GETs a path that names it, so the calls under a scenario say which test made
/// them, and writes the path to its output first, which the TRX keeps.
/// </summary>
public static class FixtureHttp
{
    public const string CallLinePrefix = "fixture-call: GET ";

    private static readonly HttpClient Client = new(
        new TestTrackingMessageHandler(new XUnit2TestTrackingMessageHandlerOptions { FixedNameForReceivingService = "Svc" })
        {
            InnerHandler = new StubHandler()
        })
    {
        BaseAddress = new Uri("http://svc.fixture/")
    };

    public static Task GetAsync(string path, ITestOutputHelper output)
    {
        output.WriteLine(CallLinePrefix + path);
        return GetAsync(path);
    }

    public static async Task GetAsync(string path)
    {
        using var response = await Client.GetAsync(path.TrimStart('/'));
        response.EnsureSuccessStatusCode();
    }

    private sealed class StubHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                RequestMessage = request,
                Content = new StringContent($"{{\"path\":\"{request.RequestUri!.AbsolutePath}\"}}", Encoding.UTF8, "application/json")
            });
    }
}
