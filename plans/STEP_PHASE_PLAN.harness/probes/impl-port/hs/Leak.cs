using System.Runtime.CompilerServices;
using Kronikol;
using Kronikol.Tracking;
using Kronikol.xUnit3;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Xunit;

namespace PhaseProbe.Hs;

/// <summary>
/// What a handler on the fetcher path keeps alive once its test is over: a host per test, and a client per test on one host.
/// Only on with PROBE_LEAK=on, as it starts many hosts.
/// </summary>
public class Leak
{
    private static bool Off => Environment.GetEnvironmentVariable("PROBE_LEAK") != "on";

    private static long Memory()
    {
        for (var i = 0; i < 3; i++)
        {
            GC.Collect();
            GC.WaitForPendingFinalizers();
        }
        return GC.GetTotalMemory(forceFullCollection: true);
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static async Task<(WeakReference Server, WeakReference Services)> OneTestOnItsOwnHost(int i, Func<string?>? fetcher)
    {
        using var scope = TestIdentityScope.Begin($"leak-{i}", $"leak-{i}-{Guid.NewGuid():N}");
        var factory = new WebApplicationFactory<Program>().WithWebHostBuilder(b => b.ConfigureTestServices(_ => { }));
        var client = factory.CreateTestTrackingClient(new XUnitTestTrackingMessageHandlerOptions
        {
            CallerName = "Probe",
            FixedNameForReceivingService = "Shop",
            CurrentStepTypeFetcher = fetcher,
        });
        (await client.GetAsync("/nocall/x")).EnsureSuccessStatusCode();
        var server = new WeakReference(factory.Server);
        var services = new WeakReference(factory.Services);
        client.Dispose();
        await factory.DisposeAsync();
        return (server, services);
    }

    private static async Task HostPerTest(string label, Func<string?>? fetcher)
    {
        const int hosts = 40;
        var before = Memory();
        var refs = new List<(WeakReference Server, WeakReference Services)>();
        for (var i = 0; i < hosts; i++)
            refs.Add(await OneTestOnItsOwnHost(i, fetcher));
        var after = Memory();
        var line = $"{label}: hosts={hosts} serversAlive={refs.Count(r => r.Server.IsAlive)} servicesAlive={refs.Count(r => r.Services.IsAlive)} bytesPerHost={(after - before) / hosts}";
        File.AppendAllText(Path.Combine(Environment.GetEnvironmentVariable("PHASE_PROBE_OUT")!, "leak.txt"), line + Environment.NewLine);
    }

    [Fact]
    public async Task L01_a_host_per_test_on_the_fetcher_path_and_without_it()
    {
        if (Off) return;
        // One warm-up, so the first host's one-time costs are not charged to the count.
        await OneTestOnItsOwnHost(-1, null);
        await HostPerTest("no fetcher", null);
        await HostPerTest("fetcher says Given", () => "Given");
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static async Task OneTestOnASharedHost(int i, WebApplicationFactory<Program> factory)
    {
        using var scope = TestIdentityScope.Begin($"leak2-{i}", $"leak2-{i}-{Guid.NewGuid():N}");
        var client = factory.CreateTestTrackingClient(new XUnitTestTrackingMessageHandlerOptions
        {
            CallerName = "Probe",
            FixedNameForReceivingService = "Shop",
            CurrentStepTypeFetcher = () => "Given",
        });
        (await client.GetAsync("/nocall/x")).EnsureSuccessStatusCode();
        client.Dispose();
    }

    [Fact]
    public async Task L02_a_client_per_test_on_one_host_on_the_fetcher_path()
    {
        if (Off) return;
        await using var factory = new WebApplicationFactory<Program>().WithWebHostBuilder(b => b.ConfigureTestServices(_ => { }));
        await OneTestOnASharedHost(-1, factory);
        const int tests = 5000;
        var before = Memory();
        for (var i = 0; i < tests; i++)
            await OneTestOnASharedHost(i, factory);
        var after = Memory();
        File.AppendAllText(Path.Combine(Environment.GetEnvironmentVariable("PHASE_PROBE_OUT")!, "leak.txt"), $"client per test on one host: tests={tests} bytesPerTest={(after - before) / tests}{Environment.NewLine}");
    }
}
