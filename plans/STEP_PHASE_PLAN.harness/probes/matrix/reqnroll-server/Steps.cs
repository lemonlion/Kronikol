using Kronikol;
using Kronikol.ReqNRoll;
using Kronikol.Tracking;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using PhaseProbe;
using ProbeService;
using Reqnroll;
using Reqnroll.BoDi;

[assembly: TrackSteps]
[assembly: Xunit.CollectionBehavior(DisableTestParallelization = true)]

namespace PhaseProbe.Server;

/// <summary>The service's events, through a tracker the service resolves: its caller is the service, and its identity comes from the request's headers.</summary>
public sealed class TrackedEvents(MessageTracker tracker) : IEvents
{
    public void Published(string label) => tracker.TrackSendEvent("bus", label, new Uri($"bus://probe/{label}"));
}

/// <summary>An event handler that lives in the test assembly, so it is woven, and runs on the server's flow: no step chain, no ambient phase, and an identity from TestTrackingContextMiddleware's scope.</summary>
public sealed class WovenEvents(MessageTracker tracker) : IEvents
{
    public void Published(string label)
    {
        if (label.Contains("woven"))
            HandleTheEvent(label);
        else
            tracker.TrackSendEvent("bus", label, new Uri($"bus://probe/{label}"));
    }

    [WhenStep]
    public void HandleTheEvent(string label) => tracker.TrackSendEvent("bus", label, new Uri($"bus://probe/{label}"));
}

/// <summary>One call to the server per point, and one message from the test.</summary>
public sealed class ServerCalls(HttpClient client, MessageTracker tracker)
{
    public async Task Make(string label)
    {
        FlowTrace.Note(label);
        (await client.GetAsync(label)).EnsureSuccessStatusCode();
        tracker.TrackSendEvent("bus", label, new Uri($"bus://probe/{label}"));
    }
}

public class SubSteps(ServerCalls calls)
{
    [GivenStep] public async Task GivenAsync(string label) => await calls.Make(label);
    [WhenStep] public async Task WhenAsync(string label) => await calls.Make(label);
}

[Binding]
public class ServerSteps(ServerCalls calls, SubSteps sub, ITestRunner runner)
{
    [StepDefinition("a server call named (.*)")]
    public Task AServerCall(string label) => calls.Make(label);

    [When("a When binding calls the server around a nested GivenStep, as (.*)")]
    public async Task WhenAroundGivenStep(string label)
    {
        await calls.Make($"{label}-before-sub");
        await sub.GivenAsync($"{label}-in-sub");
        await calls.Make($"{label}-after-sub");
    }

    [When("a When binding calls the server around a nested Gherkin Given, as (.*)")]
    public async Task WhenAroundGherkin(string label)
    {
        await calls.Make($"{label}-before-nested");
        await runner.GivenAsync($"a server call named {label}-nested-given");
        await calls.Make($"{label}-after-nested");
    }

    [Given("a Given binding calls the server around a nested WhenStep, as (.*)")]
    public async Task GivenAroundWhenStep(string label)
    {
        await calls.Make($"{label}-before-sub");
        await sub.WhenAsync($"{label}-in-sub");
        await calls.Make($"{label}-after-sub");
    }
}

[Binding]
public class Hooks
{
    private static WebApplicationFactory<Program> NewFactory() => new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        builder.ConfigureTestServices(services =>
        {
            services.TrackDependenciesForDiagrams(new ReqNRollTestTrackingMessageHandlerOptions
            {
                CallerName = "Shop",
                FixedNameForReceivingService = "Downstream",
            });
            services.TrackMessagesForDiagrams("Shop");
            services.AddSingleton<IEvents, TrackedEvents>();
            services.ConfigureHttpClientDefaults(http => http.ConfigurePrimaryHttpMessageHandler(() => new AnswerEverything()));
        }));

    private static readonly Lazy<WebApplicationFactory<Program>> Default = new(NewFactory);

    private static readonly Lazy<WebApplicationFactory<Program>> WithWovenEvents = new(() => NewFactory().WithWebHostBuilder(builder =>
        builder.ConfigureTestServices(services =>
        {
            services.AddTestTrackingContextPropagation();
            services.AddSingleton<IEvents, WovenEvents>();
        })));

    private static readonly Lazy<WebApplicationFactory<Program>> Preserving = new(() =>
    {
        var factory = NewFactory();
        factory.Server.PreserveExecutionContext = true;
        return factory;
    });

    private static readonly Lazy<WebApplicationFactory<Program>> OnKestrel = new(() =>
    {
        var factory = NewFactory();
        factory.UseKestrel(options => options.Listen(System.Net.IPAddress.Loopback, 0));
        factory.StartServer();
        return factory;
    });

    [BeforeScenario(Order = 0)]
    public void Register(IObjectContainer container, ScenarioContext scenario)
    {
        var tags = scenario.ScenarioInfo.Tags;
        var factory = tags.Contains("kestrel") ? OnKestrel.Value : tags.Contains("preserve-context") ? Preserving.Value : tags.Contains("woven-events") ? WithWovenEvents.Value : Default.Value;
        var client = factory.CreateTestTrackingClient(new ReqNRollTestTrackingMessageHandlerOptions
        {
            CallerName = "Probe",
            FixedNameForReceivingService = "Shop",
        });
        if (tags.Contains("kestrel"))
            client.BaseAddress = new Uri(factory.Services.GetRequiredService<Microsoft.AspNetCore.Hosting.Server.IServer>()
                .Features.Get<Microsoft.AspNetCore.Hosting.Server.Features.IServerAddressesFeature>()!.Addresses.First());
        container.RegisterInstanceAs(client);
        container.RegisterInstanceAs(new MessageTracker(new MessageTrackerOptions
        {
            CallerName = "Probe",
            ServiceName = "Bus",
            CurrentTestInfoFetcher = ReqNRollTestTrackingMessageHandlerOptions.TestInfoFetcher,
        }));
    }

    [AfterTestRun]
    public static void Report()
    {
        Dump.Write("reqnroll-server");
        ReqNRollReportGenerator.CreateStandardReportsWithDiagrams(new ReportConfigurationOptions { SpecificationsTitle = "Phase probe", SeparateSetup = true });
        if (WithWovenEvents.IsValueCreated) WithWovenEvents.Value.Dispose();
        if (Default.IsValueCreated) Default.Value.Dispose();
        if (Preserving.IsValueCreated) Preserving.Value.Dispose();
        if (OnKestrel.IsValueCreated) OnKestrel.Value.Dispose();
    }
}
