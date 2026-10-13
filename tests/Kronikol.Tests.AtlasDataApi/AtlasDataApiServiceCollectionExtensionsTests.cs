using Microsoft.AspNetCore.Http;
using Kronikol.Tests.Tracking;
using Kronikol.Tracking;
using Microsoft.Extensions.DependencyInjection;
using Kronikol.Extensions.AtlasDataApi;

namespace Kronikol.Tests.AtlasDataApi;

public class AtlasDataApiServiceCollectionExtensionsTests
{
    [Fact]
    public void AddAtlasDataApiTestTracking_RegistersHandler()
    {
        var services = new ServiceCollection();

        services.AddAtlasDataApiTestTracking();

        var provider = services.BuildServiceProvider();
        var handler = provider.GetService<AtlasDataApiTrackingMessageHandler>();
        Assert.NotNull(handler);
    }

    [Fact]
    public void AddAtlasDataApiTestTracking_RegistersOptions()
    {
        var services = new ServiceCollection();

        services.AddAtlasDataApiTestTracking(o => o.ServiceName = "Custom");

        var provider = services.BuildServiceProvider();
        var options = provider.GetRequiredService<AtlasDataApiTrackingMessageHandlerOptions>();
        Assert.Equal("Custom", options.ServiceName);
    }

    [Fact]
    public void AddAtlasDataApiTestTracking_RegistersAsSingleton()
    {
        var services = new ServiceCollection();

        services.AddAtlasDataApiTestTracking();

        var descriptor = services.Single(s => s.ServiceType == typeof(AtlasDataApiTrackingMessageHandler));
        Assert.Equal(ServiceLifetime.Singleton, descriptor.Lifetime);
    }

    [Fact]
    public void AddAtlasDataApiTestTracking_ReturnsServicesForChaining()
    {
        var services = new ServiceCollection();

        var result = services.AddAtlasDataApiTestTracking();

        Assert.Same(services, result);
    }

    // ─── HttpContextAccessor: the options' first, then the container's ───

    [Fact]
    public async Task Registration_prefers_the_options_accessor_to_the_containers()
    {
        var optionsTestId = Guid.NewGuid().ToString();
        var containerTestId = Guid.NewGuid().ToString();
        var services = new ServiceCollection();
        services.AddSingleton<IHttpContextAccessor>(RequestHeaderAccessor.For("Container Test", containerTestId));
        services.AddAtlasDataApiTestTracking(o => o.HttpContextAccessor = RequestHeaderAccessor.For("Options Test", optionsTestId));

        await SendFindThroughRegisteredHandler(services.BuildServiceProvider());

        Assert.Contains(RequestResponseLogger.RequestAndResponseLogs,
            l => l.TestId == optionsTestId && l.AttributionSource == AttributionSource.RequestHeader);
        Assert.DoesNotContain(RequestResponseLogger.RequestAndResponseLogs, l => l.TestId == containerTestId);
    }

    [Fact]
    public async Task Registration_uses_the_containers_accessor_when_the_options_carry_none()
    {
        var containerTestId = Guid.NewGuid().ToString();
        var services = new ServiceCollection();
        services.AddSingleton<IHttpContextAccessor>(RequestHeaderAccessor.For("Container Test", containerTestId));
        services.AddAtlasDataApiTestTracking();

        await SendFindThroughRegisteredHandler(services.BuildServiceProvider());

        Assert.Contains(RequestResponseLogger.RequestAndResponseLogs,
            l => l.TestId == containerTestId && l.AttributionSource == AttributionSource.RequestHeader);
    }

    // The wiki's IHttpClientFactory recipe. The handler sets its own inner handler, which AddHttpMessageHandler refuses
    // (InvalidOperationException when the client is created), so it goes in as the primary handler.
    [Fact]
    public void The_wikis_HttpClientFactory_recipe_builds_a_client()
    {
        var trackingOptions = new AtlasDataApiTrackingMessageHandlerOptions { ServiceName = "Atlas Data API" };
        var services = new ServiceCollection();
        services.AddHttpClient("AtlasDataApi")
            .ConfigurePrimaryHttpMessageHandler(() => new AtlasDataApiTrackingMessageHandler(trackingOptions));

        var client = services.BuildServiceProvider().GetRequiredService<IHttpClientFactory>().CreateClient("AtlasDataApi");

        Assert.NotNull(client);
    }

    private static async Task SendFindThroughRegisteredHandler(IServiceProvider provider)
    {
        var handler = provider.GetRequiredService<AtlasDataApiTrackingMessageHandler>();
        handler.InnerHandler = new OkHandler();
        using var client = new HttpClient(handler, disposeHandler: false);
        var request = new HttpRequestMessage(HttpMethod.Post, "https://data.mongodb-api.com/app/myapp/endpoint/data/v1/action/find")
        {
            Content = new StringContent(@"{""dataSource"":""C"",""database"":""db"",""collection"":""col""}", System.Text.Encoding.UTF8, "application/json")
        };
        await client.SendAsync(request, TestContext.Current.CancellationToken);
    }

    private sealed class OkHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            => Task.FromResult(new HttpResponseMessage(System.Net.HttpStatusCode.OK) { Content = new StringContent("{}") });
    }
}
