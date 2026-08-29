using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Reqnroll;
using Reqnroll.BoDi;
using Kronikol;
using Kronikol.ReqNRoll;
using Kronikol.Tracking;

namespace KronikolComponentTests.Hooks;

[Binding]
public class TestSetupHooks
{
    private const string ServiceUnderTestName = "SERVICE_NAME";
    private static WebApplicationFactory<Program>? _factory;

    [BeforeTestRun]
    public static void BeforeTestRun()
    {
        _factory = new PlaceholderApiFactory().WithWebHostBuilder(builder =>
        {
            builder.ConfigureTestServices(services =>
            {
                services.TrackDependenciesForDiagrams(new ReqNRollTestTrackingMessageHandlerOptions
                {
                    CallerName = ServiceUnderTestName,
                    PortsToServiceNames = { { 15050, "DOWNSTREAM_SERVICE" } }
                });
                services.TrackMessagesForDiagrams(ServiceUnderTestName);
            });
        });
    }

    [BeforeScenario]
    public void BeforeScenario(IObjectContainer objectContainer)
    {
        var client = _factory!.CreateTestTrackingClient(
            new ReqNRollTestTrackingMessageHandlerOptions { FixedNameForReceivingService = ServiceUnderTestName });
        objectContainer.RegisterInstanceAs(client);
    }

    [AfterScenario]
    public void AfterScenario(IObjectContainer objectContainer)
    {
        var client = objectContainer.Resolve<HttpClient>();
        client.Dispose();
    }

    [AfterTestRun]
    public static void AfterTestRun()
    {
        ReqNRollReportGenerator.CreateStandardReportsWithDiagrams(new ReportConfigurationOptions
        {
            SpecificationsTitle = "SERVICE_NAME Specifications",
            SeparateSetup = true,
        });

        _factory?.Dispose();
    }

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
