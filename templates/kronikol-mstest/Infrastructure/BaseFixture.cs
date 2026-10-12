using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Kronikol.Tracking;
using Kronikol.MSTest;

namespace KronikolComponentTests.Infrastructure;

public abstract class BaseFixture : DiagrammedComponentTest
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
                services.TrackDependenciesForDiagrams(new MSTestTestTrackingMessageHandlerOptions
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
        Client = SFactory.CreateTestTrackingClient(new MSTestTestTrackingMessageHandlerOptions
        {
            FixedNameForReceivingService = ServiceUnderTestName
        });
    }

    /// <summary>
    /// Hosts the placeholder API with no entry point and no solution: the test project's entry point is its test
    /// framework's, and WebApplicationFactory looks for an app's content root beside a solution file, which a project
    /// created outside a solution does not have, so the factory names the test's output directory instead.
    /// TODO: Once you reference your real API project, delete this class and Program.cs, and use
    /// <c>new WebApplicationFactory&lt;Program&gt;()</c> with your API's <c>Program</c> (an API written with top-level
    /// statements declares it in the global namespace, and before .NET 10 needs <c>public partial class Program;</c>).
    /// </summary>
    private sealed class PlaceholderApiFactory : WebApplicationFactory<Program>
    {
        public PlaceholderApiFactory()
        {
            // Program is the placeholder's only while Program.cs is: given your API's Program, this would host the
            // placeholder in your API's place, and the tests would pass without ever reaching your API.
            if (typeof(Program).Assembly != typeof(PlaceholderApiFactory).Assembly)
                throw new InvalidOperationException(
                    "Program is your API's now: delete PlaceholderApiFactory and use new WebApplicationFactory<Program>() (see its TODO).");
            Environment.SetEnvironmentVariable(
                $"ASPNETCORE_TEST_CONTENTROOT_{typeof(Program).Assembly.GetName().Name!.ToUpperInvariant().Replace('.', '_')}",
                AppContext.BaseDirectory);
        }

        protected override IHostBuilder CreateHostBuilder() =>
            new HostBuilder().ConfigureWebHost(web => web.Configure(app =>
                app.Run(async context => await context.Response.WriteAsync("Hello from SERVICE_NAME"))));
    }
}
