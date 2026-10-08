using LightBDD.XUnit3;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Kronikol.LightBDD;
using Kronikol.LightBDD.xUnit3;
using Kronikol.Tracking;

namespace KronikolComponentTests.Infrastructure;

public abstract class BaseFixture : FeatureFixture, IDisposable
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
