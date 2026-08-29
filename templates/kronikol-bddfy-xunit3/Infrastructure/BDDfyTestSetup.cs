using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Kronikol;
using Kronikol.BDDfy.xUnit3;
using Kronikol.Tracking;
using Xunit;

[assembly: AssemblyFixture(typeof(KronikolComponentTests.Infrastructure.BDDfyTestSetup))]

namespace KronikolComponentTests.Infrastructure;

public class BDDfyTestSetup : IAsyncLifetime
{
    private const string ServiceUnderTestName = "SERVICE_NAME";
    private static WebApplicationFactory<Program>? _factory;

    public static WebApplicationFactory<Program> Factory => _factory!;

    public ValueTask InitializeAsync()
    {
        BDDfyDiagramsConfigurator.Configure();
        BDDfyScenarioCollector.StartRunTime = DateTime.UtcNow;

        _factory = new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.ConfigureTestServices(services =>
            {
                services.TrackDependenciesForDiagrams(new BDDfyTestTrackingMessageHandlerOptions
                {
                    CallerName = ServiceUnderTestName,
                    PortsToServiceNames = { { 15050, "DOWNSTREAM_SERVICE" } }
                });
                services.TrackMessagesForDiagrams(ServiceUnderTestName);
            });
        });

        return ValueTask.CompletedTask;
    }

    public ValueTask DisposeAsync()
    {
        BDDfyScenarioCollector.EndRunTime = DateTime.UtcNow;

        BDDfyReportGenerator.CreateStandardReportsWithDiagrams(new ReportConfigurationOptions
        {
            SpecificationsTitle = "SERVICE_NAME Specifications",
            SeparateSetup = true,
        });

        _factory?.Dispose();
        return ValueTask.CompletedTask;
    }

    public static HttpClient CreateTrackingClient()
    {
        return _factory!.CreateTestTrackingClient(
            new BDDfyTestTrackingMessageHandlerOptions { FixedNameForReceivingService = ServiceUnderTestName });
    }
}
