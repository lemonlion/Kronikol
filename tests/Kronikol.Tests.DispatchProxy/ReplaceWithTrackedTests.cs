using Microsoft.Extensions.DependencyInjection;
using Kronikol.Extensions.DispatchProxy;
using Kronikol.Tracking;

namespace Kronikol.Tests.DispatchProxy;

/// <summary>
/// <see cref="ServiceCollectionTrackingExtensions.ReplaceWithTracked{TService}(IServiceCollection, TService, TrackingProxyOptions)"/>
/// and its simplified overload replace a service's registration with a proxy that draws each call.
/// </summary>
public class ReplaceWithTrackedTests
{
    private readonly string _testId = Guid.NewGuid().ToString();
    private const string TestName = "ServiceCollectionTrackingExtensionsTest";

    private RequestResponseLog[] GetLogsForTest()
        => RequestResponseLogger.RequestAndResponseLogs.Where(l => l.TestId == _testId).ToArray();

    [Fact]
    public void Simplified_overload_passes_dependency_category_through()
    {
        var services = new ServiceCollection();
        var implementation = new FakeService();
        services.AddSingleton<IFakeService>(implementation);

        services.ReplaceWithTracked<IFakeService>(
            implementation,
            "SmtpRelay",
            testInfoFetcher: () => (TestName, _testId),
            dependencyCategory: "Email");

        var provider = services.BuildServiceProvider();
        var resolved = provider.GetRequiredService<IFakeService>();
        resolved.DoWork();

        var logs = GetLogsForTest();
        Assert.Equal(2, logs.Length);
        Assert.All(logs, l => Assert.Equal("Email", l.DependencyCategory));
    }

    [Fact]
    public void Simplified_overload_dependency_category_defaults_to_null()
    {
        var services = new ServiceCollection();
        var implementation = new FakeService();
        services.AddSingleton<IFakeService>(implementation);

        services.ReplaceWithTracked<IFakeService>(
            implementation,
            "Calculator",
            testInfoFetcher: () => (TestName, _testId));

        var provider = services.BuildServiceProvider();
        var resolved = provider.GetRequiredService<IFakeService>();
        resolved.DoWork();

        var logs = GetLogsForTest();
        Assert.Equal(2, logs.Length);
        Assert.All(logs, l => Assert.Null(l.DependencyCategory));
    }

    [Fact]
    public void The_overload_with_options_draws_each_call_under_the_service_it_names()
    {
        var services = new ServiceCollection();
        var implementation = new FakeService();
        services.AddSingleton<IFakeService>(implementation);

        services.ReplaceWithTracked<IFakeService>(implementation, new TrackingProxyOptions
        {
            ServiceName = "Calculator",
            CurrentTestInfoFetcher = () => (TestName, _testId),
        });

        Assert.Equal("done", services.BuildServiceProvider().GetRequiredService<IFakeService>().DoWork());
        var logs = GetLogsForTest();
        Assert.Equal(2, logs.Length);
        Assert.All(logs, l => Assert.Equal("Calculator", l.ServiceName));
    }

    [Fact]
    public void Replacing_a_service_replaces_each_of_its_registrations()
    {
        var services = new ServiceCollection();
        services.AddSingleton<IFakeService>(new FakeService());
        var implementation = new FakeService();
        services.AddSingleton<IFakeService>(implementation);

        services.ReplaceWithTracked<IFakeService>(implementation, "Calculator", testInfoFetcher: () => (TestName, _testId));

        var resolved = Assert.Single(services.BuildServiceProvider().GetServices<IFakeService>());
        resolved.DoWork();
        Assert.Equal(2, GetLogsForTest().Length);
    }

    [Fact]
    public void Replacing_a_service_leaves_its_keyed_registrations_alone()
    {
        var services = new ServiceCollection();
        var implementation = new FakeService();
        var keyed = new FakeService();
        services.AddSingleton<IFakeService>(implementation);
        services.AddKeyedSingleton<IFakeService>("other", keyed);

        services.ReplaceWithTracked<IFakeService>(implementation, "Calculator", testInfoFetcher: () => (TestName, _testId));

        var provider = services.BuildServiceProvider();
        Assert.Same(keyed, provider.GetRequiredKeyedService<IFakeService>("other"));
        Assert.NotSame(implementation, provider.GetRequiredService<IFakeService>());
    }

    public interface IFakeService
    {
        string DoWork();
    }

    private class FakeService : IFakeService
    {
        public string DoWork() => "done";
    }
}
