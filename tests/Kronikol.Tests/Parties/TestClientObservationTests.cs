using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Hosting;
using Kronikol.Parties;
using Kronikol.Tracking;

namespace Kronikol.Tests.Parties;

/// <summary>
/// #190 R1, O1 (plans/PARTY_IDENTITY_PLAN.md §5.3): a tracking client Kronikol builds from a
/// <c>WebApplicationFactory</c> is the test's own, so its caller name is the test and whom it calls is the service
/// under test, both observed rather than guessed. A handler made by hand says nothing. The registry is one for the
/// process, so each fact names its parties uniquely and never clears it.
/// </summary>
// Reads the process-global request log back (#190 R3), so it runs beside the ingests that clear it.
[Collection("DiagramsFetcher")]
public class TestClientObservationTests
{
    private static string Unique(string name) => $"{name} {Guid.NewGuid():N}"[..(name.Length + 9)];

    private static TestClientObservation? Observed(string caller) =>
        TestClientRegistry.Observations().TestClients.SingleOrDefault(o => o.Caller == caller);

    [Fact]
    public async Task A_client_built_from_a_factory_is_observed_as_the_test_and_whom_it_called_as_the_subject()
    {
        var (caller, app) = (Unique("Observer"), Unique("Observed App"));
        using var factory = new ObservedAppFactory();
        using var client = factory.CreateTestTrackingClient(new TestTrackingMessageHandlerOptions
        {
            CallerName = caller,
            FixedNameForReceivingService = app,
            CurrentTestInfoFetcher = () => ("Observe a test client", "observe-a-test-client"),
        });

        Assert.Equal("ok", await client.GetStringAsync("/"));

        var observed = Observed(caller);
        Assert.NotNull(observed);
        Assert.Equal([app], observed.Callees);
        Assert.Equal(typeof(ObservedAppFactory).Assembly.GetName().Name, observed.Application);

        var table = PartyResolver.Resolve([new PartyCall(caller, app, "HTTP")], PartyDeclarations.None, new PartyObservations([observed]));
        Assert.Equal(new Party(caller, null, PartyKind.Test, PartyKindSource.Observed, null), table.Parties.Single(p => p.Name == caller));
        Assert.Equal(new Party(app, null, PartyKind.UnderTest, PartyKindSource.Observed, observed.Application), table.Parties.Single(p => p.Name == app));
    }

    [Fact]
    public async Task A_client_built_with_handlers_of_its_own_is_observed_too()
    {
        var (caller, app) = (Unique("Observer"), Unique("Observed App"));
        using var factory = new ObservedAppFactory();
        using var client = factory.CreateTestTrackingClient(new TestTrackingMessageHandlerOptions
        {
            CallerName = caller,
            FixedNameForReceivingService = app,
            CurrentTestInfoFetcher = () => ("Observe a test client", "observe-a-test-client"),
        }, new PassOn());

        await client.GetStringAsync("/");

        Assert.Equal([app], Observed(caller)?.Callees);
    }

    // #190 R3: TestServer's handler is the service under test answering in process, never a test double.
    [Fact]
    public async Task A_call_the_test_server_answered_is_not_marked_as_a_test_double()
    {
        var testId = "test-server-" + Guid.NewGuid().ToString("N");
        using var factory = new ObservedAppFactory();
        using var client = factory.CreateTestTrackingClient(new TestTrackingMessageHandlerOptions
        {
            CallerName = Unique("Caller"),
            FixedNameForReceivingService = Unique("Observed App"),
            CurrentTestInfoFetcher = () => ("Not a double", testId),
        });

        await client.GetAsync("/");

        var logs = RequestResponseLogger.RequestAndResponseLogs.Where(l => l.TestId == testId).ToArray();
        Assert.Equal(2, logs.Length);
        Assert.All(logs, l => Assert.Null(l.TestDouble));
    }

    [Fact]
    public void A_client_that_never_called_is_not_observed()
    {
        var caller = Unique("Observer");
        using var factory = new ObservedAppFactory();
        using var client = factory.CreateTestTrackingClient(new TestTrackingMessageHandlerOptions { CallerName = caller, FixedNameForReceivingService = Unique("Observed App") });

        Assert.Null(Observed(caller));
    }

    [Fact]
    public async Task A_tracking_handler_made_by_hand_is_not_observed()
    {
        var caller = Unique("Observer");
        using var client = new HttpClient(new TestTrackingMessageHandler(new TestTrackingMessageHandlerOptions
        {
            CallerName = caller,
            FixedNameForReceivingService = Unique("Observed App"),
            CurrentTestInfoFetcher = () => ("Call by hand", "call-by-hand"),
        })
        { InnerHandler = new Answer() });

        await client.GetAsync("http://localhost/");

        Assert.Null(Observed(caller));
    }

    /// <summary>A factory for an app made here, answering "ok" to everything.</summary>
    private sealed class ObservedAppFactory : WebApplicationFactory<ObservedAppFactory>
    {
        // the content root the factory looks for first, named after the entry point's assembly
        static ObservedAppFactory() => Environment.SetEnvironmentVariable("ASPNETCORE_TEST_CONTENTROOT_KRONIKOL_TESTS", AppContext.BaseDirectory);

        protected override IHostBuilder CreateHostBuilder() =>
            Host.CreateDefaultBuilder().ConfigureWebHostDefaults(web => web.Configure(app => app.Run(context => context.Response.WriteAsync("ok"))));
    }

    private sealed class PassOn : DelegatingHandler;

    private sealed class Answer : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(new HttpResponseMessage(System.Net.HttpStatusCode.OK));
    }
}
