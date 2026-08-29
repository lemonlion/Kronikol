using LightBDD.XUnit2;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Kronikol.LightBDD;
using Kronikol.LightBDD.xUnit2;
using Kronikol.Tracking;

namespace KronikolComponentTests.Infrastructure;

public abstract class BaseFixture : FeatureFixture, IDisposable
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
                services.TrackDependenciesForDiagrams(new LightBddTestTrackingMessageHandlerOptions
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
        Client = SFactory.CreateTestTrackingClient(new LightBddTestTrackingMessageHandlerOptions
        {
            FixedNameForReceivingService = ServiceUnderTestName
        });
    }

    public void Dispose() => Client.Dispose();
    public static void DisposeFactory() => SFactory?.Dispose();
}
