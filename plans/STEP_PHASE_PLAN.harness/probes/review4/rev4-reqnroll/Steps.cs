using Kronikol;
using Kronikol.ReqNRoll;
using Kronikol.Tracking;
using Reqnroll;
using Reqnroll.BoDi;
using System.Threading.Channels;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Rev4;

[assembly: TrackSteps]
[assembly: Xunit.CollectionBehavior(DisableTestParallelization = true)]

namespace Rev4.Reqnroll;

public sealed class Calls(HttpClient client)
{
    public async Task Make(string label) => (await client.GetAsync($"http://probe.local/{label}")).EnsureSuccessStatusCode();
}

public sealed record Transformed(string Label);

public static class RealHost
{
    public sealed record Item(string Name, string Id, string Label, TaskCompletionSource Done);
    public static readonly Channel<Item> Work = Channel.CreateUnbounded<Item>();
    private static IHost? _host;
    private static readonly object Gate = new();

    public static HttpClient NewClient(bool trackDuringAction = true) => new(new TestTrackingMessageHandler(new ReqNRollTestTrackingMessageHandlerOptions
    {
        CallerName = "Probe",
        FixedNameForReceivingService = "Shop",
        TrackDuringAction = trackDuringAction,
    }) { InnerHandler = new Answer() });

    public sealed class Worker : BackgroundService
    {
        private readonly HttpClient _client = NewClient();
        private readonly HttpClient _quiet = NewClient(trackDuringAction: false);

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            await foreach (var item in Work.Reader.ReadAllAsync(stoppingToken))
            {
                using (TestIdentityScope.Begin(item.Name, item.Id))
                {
                    await _client.GetAsync($"http://probe.local/{item.Label}", stoppingToken);
                    await _quiet.GetAsync($"http://probe.local/{item.Label}-quiet", stoppingToken);
                }
                item.Done.SetResult();
            }
        }
    }

    public static void EnsureStarted()
    {
        lock (Gate)
        {
            if (_host is not null) return;
            var builder = Host.CreateApplicationBuilder();
            builder.Services.AddHostedService<Worker>();
            builder.Services.DetachHostedServicesFromTestIdentity();
            _host = builder.Build();
            _host.StartAsync().GetAwaiter().GetResult();
        }
    }
}

[Binding]
public class Rev4Steps(Calls calls)
{
    [StepDefinition("the worker handles (.*)")]
    public async Task WorkerHandles(string label)
    {
        var info = ReqNRollTestContext.CurrentTestInfo!.Value;
        var done = new TaskCompletionSource();
        await RealHost.Work.Writer.WriteAsync(new RealHost.Item(info.Name, info.Id, label, done));
        await done.Task;
    }

    [StepDefinition("a call named (.*)")]
    public Task ACall(string label) => calls.Make(label);

    [When("a marking act named (.*)")]
    public async Task MarkingAct(string label)
    {
        TrackingDiagramOverride.StartAction();
        await calls.Make(label);
    }

    [StepDefinition("a hooked call named (.*)")]
    public Task HookedCall(string label) => calls.Make(label);

    [StepArgumentTransformation("<(.*)>")]
    public async Task<Transformed> Transform(string label)
    {
        await calls.Make(label + "-in-transformation");
        return new Transformed(label);
    }

    [Given("a transformed call (.*)")]
    public Task TransformedCall(Transformed t) => calls.Make(t.Label);

    [When("a pending step")]
    public void Pending() => throw new PendingStepException();

    [When("a failing step named (.*)")]
    public async Task Failing(string label)
    {
        await calls.Make(label);
        throw new InvalidOperationException("failing step");
    }
}

[Binding]
public class Hooks(ScenarioContext scenario)
{
    private static HttpClient Client() => new(new TestTrackingMessageHandler(new ReqNRollTestTrackingMessageHandlerOptions
    {
        CallerName = "Probe",
        FixedNameForReceivingService = "Shop",
    }) { InnerHandler = new Answer() });

    [BeforeScenario(Order = 0)]
    public void Register(IObjectContainer container)
    {
        container.RegisterInstanceAs(new Calls(Client()));
        RealHost.EnsureStarted();
    }

    [BeforeStep(Order = 100)]
    public async Task BeforeEach(Calls calls)
    {
        if (!scenario.ScenarioInfo.Title.StartsWith("Q03")) return;
        await Task.Yield();
        await calls.Make("hook-before-" + scenario.StepContext.StepInfo.Text.Split(' ').Last());
    }

    [AfterStep(Order = 100)]
    public async Task AfterEach(Calls calls)
    {
        if (!scenario.ScenarioInfo.Title.StartsWith("Q03")) return;
        await Task.Yield();
        await calls.Make("hook-after-" + scenario.StepContext.StepInfo.Text.Split(' ').Last());
    }

    [AfterScenario(Order = 0)]
    public Task After(Calls calls) => calls.Make(scenario.ScenarioInfo.Title[..3].ToLowerInvariant() + "-after-scenario");

    [AfterTestRun]
    public static void Report()
    {
        Skeleton.Write("rev4-reqnroll");
    }
}
