using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Kronikol.Tracking;
using Kronikol.NUnit4;

namespace KronikolComponentTests.Infrastructure;

public abstract class BaseFixture : DiagrammedComponentTest, IDisposable
{
    private static readonly WebApplicationFactory<Program> SFactory;
    protected HttpClient Client { get; }

    private const string ServiceUnderTestName = "SERVICE_NAME";

    static BaseFixture()
    {
        SFactory = new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.ConfigureTestServices(services =>
            {
                services.TrackDependenciesForDiagrams(new NUnitTestTrackingMessageHandlerOptions
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
        Client = SFactory.CreateTestTrackingClient(new NUnitTestTrackingMessageHandlerOptions
        {
            FixedNameForReceivingService = ServiceUnderTestName
        });
    }

    public void Dispose() => Client.Dispose();
}
