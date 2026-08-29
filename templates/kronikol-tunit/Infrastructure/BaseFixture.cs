using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Kronikol.Tracking;
using Kronikol.TUnit;

namespace KronikolComponentTests.Infrastructure;

public abstract class BaseFixture : DiagrammedComponentTest, IDisposable
{
    private static readonly WebApplicationFactory<Program> SFactory;
    protected HttpClient Client { get; }

    private const string ServiceUnderTestName = "SERVICE_NAME";

    static BaseFixture()
    {
        SFactory = new PlaceholderApiFactory().WithWebHostBuilder(builder =>
        {
            builder.ConfigureTestServices(services =>
            {
                services.TrackDependenciesForDiagrams(new TUnitTestTrackingMessageHandlerOptions
                {
                    CallerName = ServiceUnderTestName,
                    PortsToServiceNames = { { 15050, "DOWNSTREAM_SERVICE" } }
                });
                services.TrackMessagesForDiagrams(ServiceUnderTestName);
            });
        });
    }

    protected BaseFixture()
    {
        Client = SFactory.CreateTestTrackingClient(new TUnitTestTrackingMessageHandlerOptions
        {
            FixedNameForReceivingService = ServiceUnderTestName
        });
    }

    public void Dispose() => Client.Dispose();

    /// <summary>
    /// Hosts the placeholder API without a Main method (TUnit owns the process entry point).
    /// TODO: Once you reference your real API project, delete this class and use
    /// <c>new WebApplicationFactory&lt;YourApi.Program&gt;()</c> directly.
    /// </summary>
    private sealed class PlaceholderApiFactory : WebApplicationFactory<Program>
    {
        protected override IWebHostBuilder CreateWebHostBuilder() =>
            new WebHostBuilder().Configure(app =>
                app.Run(async context => await context.Response.WriteAsync("Hello from SERVICE_NAME")));
    }
}
