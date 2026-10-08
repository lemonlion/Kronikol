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

        _factory = new PlaceholderApiFactory().WithWebHostBuilder(builder =>
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
