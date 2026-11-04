using Kronikol;
using Kronikol.ReqNRoll;
using Kronikol.Tracking;
using Reqnroll;
using Reqnroll.BoDi;
using Rev5;

[assembly: TrackSteps]
[assembly: Xunit.CollectionBehavior(MaxParallelThreads = 8)]

namespace Rev5.ReqnrollPar;

public static class Shared
{
    public static HttpClient NewClient() => new(new TestTrackingMessageHandler(new ReqNRollTestTrackingMessageHandlerOptions
    {
        CallerName = "Probe",
        FixedNameForReceivingService = "Shop",
    }) { InnerHandler = new Answer() });

    public static readonly HttpClient Client = NewClient();
}

public sealed class Woven(HttpClient http)
{
    public async Task Call(string label) => (await http.GetAsync($"http://probe.local/{label}")).EnsureSuccessStatusCode();
    [GivenStep] public async Task GivenSub(string label) => await Call(label);
    [WhenStep] public async Task WhenSub(string label) => await Call(label);
}

[Binding]
public class Steps(Woven woven)
{
    [StepDefinition("a call named (.*)")]
    public async Task ACall(string label) { await woven.Call(label); await Task.Delay(Random.Shared.Next(3)); }

    [When("a binding calls a woven Given named (.*)")]
    public async Task WovenGiven(string label)
    {
        await woven.Call(label + "-before");
        await woven.GivenSub(label + "-sub");
        await woven.Call(label + "-after");
    }

    [Given("a binding calls a woven When named (.*)")]
    public async Task WovenWhen(string label)
    {
        await woven.Call(label + "-before");
        await woven.WhenSub(label + "-sub");
        await woven.Call(label + "-after");
    }
}

[Binding]
public class Hooks(IObjectContainer container)
{
    [BeforeScenario(Order = 0)]
    public void Register() => container.RegisterInstanceAs(new Woven(Shared.Client));

    [AfterTestRun]
    public static void Report() => Skeleton.Write("rev5-reqnroll-par");
}
