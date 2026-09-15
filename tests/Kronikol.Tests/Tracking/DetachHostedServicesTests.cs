using Kronikol.Tracking;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Kronikol.Tests.Tracking;

/// <summary>
/// Hosted services run detached from the test that built their host (BACKGROUND_ATTRIBUTION_PLAN, option
/// G). The test's execution context reaches every service the host starts, so without this a background
/// loop resolves the test's identity for every call it makes, for as long as the host lives.
/// </summary>
public class DetachHostedServicesTests
{
    /// <summary>Records what the identity resolution chain says from inside a running loop.</summary>
    private sealed class Probe : BackgroundService
    {
        public readonly TaskCompletionSource Observed = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public bool DetachedAtStart;
        public bool DetachedAfterAwait;
        public TestIdentity? ResolvedInLoop;
        public TestIdentity? ResolvedInMessageScope;
        public bool DetachedAtStop;

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            DetachedAtStart = TestIdentityScope.IsDetached;
            await Task.Yield();
            DetachedAfterAwait = TestIdentityScope.IsDetached;
            ResolvedInLoop = TestInfoResolver.ResolveWithSource(null, () => ("Outer test", "outer-id"));
            using (TestIdentityScope.Begin("Correlated message", "msg-id"))
                ResolvedInMessageScope = TestInfoResolver.ResolveWithSource(null, () => ("Outer test", "outer-id"));
            Observed.TrySetResult();
        }

        public override Task StopAsync(CancellationToken cancellationToken)
        {
            DetachedAtStop = TestIdentityScope.IsDetached;
            return base.StopAsync(cancellationToken);
        }
    }

    private sealed class LifecycleProbe : IHostedLifecycleService
    {
        public readonly List<(string Stage, bool Detached)> Stages = [];
        public Task StartingAsync(CancellationToken cancellationToken) => Record("starting");
        public Task StartAsync(CancellationToken cancellationToken) => Record("start");
        public Task StartedAsync(CancellationToken cancellationToken) => Record("started");
        public Task StoppingAsync(CancellationToken cancellationToken) => Record("stopping");
        public Task StopAsync(CancellationToken cancellationToken) => Record("stop");
        public Task StoppedAsync(CancellationToken cancellationToken) => Record("stopped");
        private Task Record(string stage)
        {
            Stages.Add((stage, TestIdentityScope.IsDetached));
            return Task.CompletedTask;
        }
    }

    private static IHostedService Single(IServiceCollection services) =>
        Assert.Single(services.BuildServiceProvider().GetServices<IHostedService>());

    [Fact]
    public async Task A_hosted_service_runs_detached_from_the_flow_that_started_it()
    {
        var services = new ServiceCollection();
        services.AddHostedService<Probe>();

        services.DetachHostedServicesFromTestIdentity();

        var wrapped = Assert.IsType<DetachedHostedService>(Single(services));
        var probe = Assert.IsType<Probe>(wrapped.Inner);
        using (TestIdentityScope.Begin("Outer test", "outer-id"))
        {
            await wrapped.StartAsync(CancellationToken.None);
            // The caller's own flow keeps its attachment: only the service is detached.
            Assert.False(TestIdentityScope.IsDetached);
            Assert.Equal("outer-id", TestIdentityScope.Current?.Id);
        }
        await probe.Observed.Task.WaitAsync(TimeSpan.FromSeconds(10));

        Assert.True(probe.DetachedAtStart);
        Assert.True(probe.DetachedAfterAwait);
        // Detached: neither the framework fetcher nor the outer scope names the loop's calls.
        Assert.Null(probe.ResolvedInLoop);
        // A scope that names its scenario inside the loop still wins: a correlated message is that scenario's work.
        Assert.Equal(("Correlated message", "msg-id", AttributionSource.Scope), (probe.ResolvedInMessageScope!.Value.Name, probe.ResolvedInMessageScope.Value.Id, probe.ResolvedInMessageScope.Value.Source));

        await wrapped.StopAsync(CancellationToken.None);
        Assert.True(probe.DetachedAtStop);
    }

    [Fact]
    public void An_instance_and_a_factory_registration_are_wrapped_too_and_keep_their_lifetime()
    {
        var services = new ServiceCollection();
        var instance = new Probe();
        services.AddSingleton<IHostedService>(instance);
        services.AddSingleton<IHostedService>(_ => new Probe());
        services.AddTransient<IHostedService, Probe>();

        services.DetachHostedServicesFromTestIdentity();

        Assert.Equal([ServiceLifetime.Singleton, ServiceLifetime.Singleton, ServiceLifetime.Transient], services.Select(d => d.Lifetime));
        var resolved = services.BuildServiceProvider().GetServices<IHostedService>().ToArray();
        Assert.Equal(3, resolved.Length);
        Assert.All(resolved, s => Assert.IsType<DetachedHostedService>(s));
        Assert.Same(instance, ((DetachedHostedService)resolved[0]).Inner);
    }

    [Fact]
    public void Detaching_twice_wraps_once()
    {
        var services = new ServiceCollection();
        services.AddHostedService<Probe>();

        services.DetachHostedServicesFromTestIdentity();
        services.DetachHostedServicesFromTestIdentity();

        var wrapped = Assert.IsType<DetachedHostedService>(Single(services));
        Assert.IsType<Probe>(wrapped.Inner);
    }

    [Fact]
    public void Other_registrations_are_left_alone()
    {
        var services = new ServiceCollection();
        services.AddSingleton<Probe>();
        services.AddKeyedSingleton<IHostedService, Probe>("keyed");

        services.DetachHostedServicesFromTestIdentity();

        var provider = services.BuildServiceProvider();
        Assert.IsType<Probe>(provider.GetRequiredService<Probe>());
        Assert.IsType<Probe>(provider.GetRequiredKeyedService<IHostedService>("keyed"));
    }

    [Fact]
    public async Task Every_lifecycle_stage_is_forwarded_and_detached()
    {
        var services = new ServiceCollection();
        var probe = new LifecycleProbe();
        services.AddSingleton<IHostedService>(probe);
        services.DetachHostedServicesFromTestIdentity();
        var wrapped = Assert.IsType<IHostedLifecycleService>(Single(services), exactMatch: false);

        await wrapped.StartingAsync(CancellationToken.None);
        await wrapped.StartAsync(CancellationToken.None);
        await wrapped.StartedAsync(CancellationToken.None);
        await wrapped.StoppingAsync(CancellationToken.None);
        await wrapped.StopAsync(CancellationToken.None);
        await wrapped.StoppedAsync(CancellationToken.None);

        Assert.Equal(["starting", "start", "started", "stopping", "stop", "stopped"], probe.Stages.Select(s => s.Stage));
        Assert.All(probe.Stages, s => Assert.True(s.Detached, s.Stage));
        Assert.False(TestIdentityScope.IsDetached);
    }

    [Fact]
    public void Context_propagation_detaches_the_hosted_services_as_part_of_the_deal()
    {
        var services = new ServiceCollection();
        services.AddHostedService<Probe>();

        services.AddTestTrackingContextPropagation();

        Assert.IsType<DetachedHostedService>(Single(services));
    }
}
