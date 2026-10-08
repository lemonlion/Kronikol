using System.Net;
using System.Text;
using Kronikol.Tracking;
using Kronikol.xUnit2;
using Xunit.Abstractions;

namespace Probe;

/// <summary>
/// One tracked HttpClient for every probe test: Kronikol's TestTrackingMessageHandler in front of an
/// in-process stub. Each test GETs a path that names it, so the calls under a scenario say which test made them.
/// </summary>
public static class ProbeHttp
{
    private static readonly HttpClient Client = new(
        new TestTrackingMessageHandler(new XUnit2TestTrackingMessageHandlerOptions { FixedNameForReceivingService = "Svc" })
        {
            InnerHandler = new StubHandler()
        })
    {
        BaseAddress = new Uri("http://svc.probe/")
    };

    /// <summary>Writes the path to the test's output (the TRX keeps it), then makes the tracked call.</summary>
    public static Task GetAsync(string path, ITestOutputHelper output)
    {
        output.WriteLine($"probe-call: GET {path}");
        return GetAsync(path);
    }

    public static async Task GetAsync(string path)
    {
        ProbeHooks.InBody?.Invoke(path);
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

/// <summary>Instrumentation seam: the prototype (C, D.Perf) sets it to log what the test body can see. Null elsewhere.</summary>
public static class ProbeHooks
{
    public static Action<string>? InBody;
}
