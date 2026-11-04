using System.Net;
using Kronikol;
using Kronikol.BDDfy.xUnit3;
using Kronikol.Tracking;
using ProbeBddfy;
using TestStack.BDDfy;
using Xunit;

[assembly: TrackSteps]
[assembly: AssemblyFixture(typeof(Setup))]

namespace ProbeBddfy;

public class Setup : IAsyncLifetime
{
    public ValueTask InitializeAsync()
    {
        BDDfyDiagramsConfigurator.Configure();
        BDDfyScenarioCollector.StartRunTime = DateTime.UtcNow;
        return ValueTask.CompletedTask;
    }

    public ValueTask DisposeAsync()
    {
        BDDfyScenarioCollector.EndRunTime = DateTime.UtcNow;
        BDDfyReportGenerator.CreateStandardReportsWithDiagrams(new ReportConfigurationOptions());
        return ValueTask.CompletedTask;
    }
}

public static class Http
{
    public static readonly HttpClient Client = new(new TestTrackingMessageHandler(
        new BDDfyTestTrackingMessageHandlerOptions { FixedNameForReceivingService = "Svc", CallerName = "Tests" })
    { InnerHandler = new Stub() })
    { BaseAddress = new Uri("http://localhost/") };

    private class Stub : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token) =>
            Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("{}") });
    }
}

// BDDfy's reflective convention: Setup and TearDown run through the executor and are not reported.
public class Story_with_setup_and_teardown
{
    public void Setup() => Http.Client.GetAsync("setup").GetAwaiter().GetResult();
    public void GivenAUser() => Http.Client.GetAsync("given").GetAwaiter().GetResult();
    public void WhenTheyOrder() => Http.Client.GetAsync("when").GetAwaiter().GetResult();
    public void ThenItShips() => Http.Client.GetAsync("then").GetAwaiter().GetResult();
    public void TearDown() => Http.Client.GetAsync("teardown").GetAwaiter().GetResult();

    [Fact]
    public void Reflective_story_with_setup() => this.BDDfy();
}

// #122: a step closes at its first await, so a [Step] after it is top-level.
public class Story_async_with_sub_step
{
    public async Task An_async_given_with_a_sub_step_after_its_await()
    {
        await Http.Client.GetAsync("given/before-await");
        await Task.Delay(30);
        await A_sub_step_after_the_await();
        await Http.Client.GetAsync("given/after-sub-step");
    }

    [Step(Description = "a sub step after the await")]
    public async Task A_sub_step_after_the_await()
    {
        await Http.Client.GetAsync("given/sub-step");
        await Task.Yield();
    }

    public void A_when() => Http.Client.GetAsync("when").GetAwaiter().GetResult();
    public void A_then() => Http.Client.GetAsync("then").GetAwaiter().GetResult();

    [Fact]
    public void Async_story_with_sub_step() =>
        this.Given(x => x.An_async_given_with_a_sub_step_after_its_await())
            .When(x => x.A_when())
            .Then(x => x.A_then())
            .BDDfy();
}

// A control: no setup, no awaits.
public class Story_plain
{
    public void A_given() => Http.Client.GetAsync("given").GetAwaiter().GetResult();
    public void A_when() { Inner(); Http.Client.GetAsync("when").GetAwaiter().GetResult(); }
    [Step(Description = "inner")] public void Inner() => Http.Client.GetAsync("when/inner").GetAwaiter().GetResult();
    public void A_then() => Http.Client.GetAsync("then").GetAwaiter().GetResult();

    [Fact]
    public void Plain_story() =>
        this.Given(x => x.A_given()).When(x => x.A_when()).Then(x => x.A_then()).BDDfy();
}
