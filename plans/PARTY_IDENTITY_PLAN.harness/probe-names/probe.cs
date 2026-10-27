#:project ../../../src/Kronikol/Kronikol.csproj
#:property TargetFramework=net10.0
#:property TreatWarningsAsErrors=false
#:property Nullable=enable

// Probe for PARTY_IDENTITY_PLAN.md §2: the names TestTrackingMessageHandler gives a party when nothing maps it.
// Sends requests through the real handler to a stub inner handler (no network), then prints the ServiceName and
// CallerName each request record carries. Run: dotnet run --file probe.cs (from this folder).

using System.Net;
using Kronikol.Tracking;

var testId = "probe-" + Guid.NewGuid().ToString("N")[..8];

async Task<(string Service, string Caller)> Send(string uri, TestTrackingMessageHandlerOptions options, string? clientName = null)
{
    options.CurrentTestInfoFetcher = () => ("probe", testId);
    using var handler = new TestTrackingMessageHandler(options, httpContextAccessor: null, clientName: clientName) { InnerHandler = new Stub() };
    using var invoker = new HttpMessageInvoker(handler);
    using var response = await invoker.SendAsync(new HttpRequestMessage(HttpMethod.Get, uri), CancellationToken.None);
    var log = RequestResponseLogger.RequestAndResponseLogs.Last(l => l.TestId == testId && l.Type == RequestResponseType.Request && l.Uri.ToString() == new Uri(uri).ToString());
    return (log.ServiceName, log.CallerName);
}

var cases = new (string Label, string Uri, Func<TestTrackingMessageHandlerOptions> Options, string? Client)[]
{
    ("unmapped remote host, https", "https://pricing.example.com/v1/prices", () => new(), null),
    ("a second remote host on the same port", "https://api.openai.example/v1/chat", () => new(), null),
    ("unmapped loopback, dynamic port", "http://localhost:54321/stub", () => new(), null),
    ("unmapped loopback, another run's port", "http://127.0.0.1:54987/stub", () => new(), null),
    ("port mapped, host differs", "http://pricing.internal:5141/x", () => new() { PortsToServiceNames = { { 5141, "Cow Service" } } }, null),
    ("same port mapped, other host", "http://other.internal:5141/x", () => new() { PortsToServiceNames = { { 5141, "Cow Service" } } }, null),
    ("client name unmapped", "https://pricing.example.com/v1/prices", () => new() { ClientNamesToServiceNames = { { "billing", "Billing" } } }, "IPricingApi"),
    ("CallerName left at its default", "http://localhost:5000/x", () => new() { FixedNameForReceivingService = "Breakfast Provider" }, null),
};

foreach (var (label, uri, options, client) in cases)
{
    var (service, caller) = await Send(uri, options(), client);
    Console.WriteLine($"{label,-42} {uri,-42} -> service \"{service}\", caller \"{caller}\"");
}

sealed class Stub : HttpMessageHandler
{
    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
        Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("{}") });
}
