using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Http;
using Kronikol.InternalFlow;
using Kronikol.Tracking;

namespace Kronikol.Tests.Tracking;

[Collection("DiagramsFetcher")]
public class ServiceCollectionTrackingExtensionsTests
{
    [Fact]
    public void AddTestTrackingContextPropagation_called_twice_registers_only_one_startup_filter()
    {
        var services = new ServiceCollection();

        services.AddTestTrackingContextPropagation();
        services.AddTestTrackingContextPropagation();

        var count = services.Count(s =>
            s.ServiceType == typeof(IStartupFilter) &&
            s.ImplementationType == typeof(TestTrackingContextStartupFilter));
        Assert.Equal(1, count);
    }

    [Fact]
    public void AddTestTrackingContextPropagation_registers_when_other_startup_filters_exist()
    {
        var services = new ServiceCollection();
        // Simulate ASP.NET Core registering other startup filters first
        services.AddSingleton<IStartupFilter, DummyStartupFilter>();

        services.AddTestTrackingContextPropagation();

        var count = services.Count(s =>
            s.ServiceType == typeof(IStartupFilter) &&
            s.ImplementationType == typeof(TestTrackingContextStartupFilter));
        Assert.Equal(1, count);
    }

    [Fact]
    public void TrackDependenciesForDiagrams_registers_IHttpMessageHandlerBuilderFilter()
    {
        var services = new ServiceCollection();
        services.AddHttpClient(); // register DefaultHttpClientFactory

        ServiceCollectionHelper.TrackDependenciesForDiagrams(services,
            new TestTrackingMessageHandlerOptions { CallerName = "Test" });

        var filterDescriptors = services.Where(s =>
            s.ServiceType == typeof(IHttpMessageHandlerBuilderFilter)).ToList();
        Assert.Contains(filterDescriptors, d =>
            d.ImplementationType == typeof(TrackingHttpMessageHandlerBuilderFilter));
    }

    [Fact]
    public void TrackDependenciesForDiagrams_does_not_replace_IHttpClientFactory()
    {
        var services = new ServiceCollection();
        services.AddHttpClient(); // register DefaultHttpClientFactory

        ServiceCollectionHelper.TrackDependenciesForDiagrams(services,
            new TestTrackingMessageHandlerOptions { CallerName = "Test" });

        var factoryDescriptor = services.LastOrDefault(s =>
            s.ServiceType == typeof(IHttpClientFactory));
        // Should NOT be TestTrackingHttpClientFactory
        Assert.NotNull(factoryDescriptor);
        Assert.NotEqual(typeof(TestTrackingHttpClientFactory), factoryDescriptor.ImplementationType);
    }

    [Fact]
    public void TrackDependenciesForDiagrams_coexists_with_custom_filters()
    {
        var services = new ServiceCollection();
        services.AddHttpClient();
        services.AddSingleton<IHttpMessageHandlerBuilderFilter, StubCustomFilter>();

        ServiceCollectionHelper.TrackDependenciesForDiagrams(services,
            new TestTrackingMessageHandlerOptions { CallerName = "Test" });

        var provider = services.BuildServiceProvider();
        var filters = provider.GetServices<IHttpMessageHandlerBuilderFilter>().ToList();
        Assert.Contains(filters, f => f is TrackingHttpMessageHandlerBuilderFilter);
        Assert.Contains(filters, f => f is StubCustomFilter);
    }

    private class StubCustomFilter : IHttpMessageHandlerBuilderFilter
    {
        public Action<HttpMessageHandlerBuilder> Configure(Action<HttpMessageHandlerBuilder> next) => next;
    }

    private class DummyStartupFilter : IStartupFilter
    {
        public Action<IApplicationBuilder> Configure(Action<IApplicationBuilder> next) => next;
    }
}
