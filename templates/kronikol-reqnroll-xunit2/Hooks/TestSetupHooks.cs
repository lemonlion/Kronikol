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
    /// Hosts the placeholder API with no entry point and no solution: the test project's entry point is its test
    /// framework's, and WebApplicationFactory looks for an app's content root beside a solution file, which a project
    /// created outside a solution does not have, so the factory names the test's output directory instead.
    /// TODO: Once you reference your real API project, delete this class and Program.cs, and use
    /// <c>new WebApplicationFactory&lt;YourApi.Program&gt;()</c> directly.
    /// </summary>
    private sealed class PlaceholderApiFactory : WebApplicationFactory<Program>
    {
        public PlaceholderApiFactory() => Environment.SetEnvironmentVariable(
            $"ASPNETCORE_TEST_CONTENTROOT_{typeof(Program).Assembly.GetName().Name!.ToUpperInvariant().Replace('.', '_')}",
            AppContext.BaseDirectory);

        protected override IHostBuilder CreateHostBuilder() =>
            new HostBuilder().ConfigureWebHost(web => web.Configure(app =>
                app.Run(async context => await context.Response.WriteAsync("Hello from SERVICE_NAME"))));
    }
}
