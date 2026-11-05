using Kronikol;
using Kronikol.BDDfy.xUnit3;
using Kronikol.Tracking;
using Probe;
using TestStack.BDDfy;
using Xunit;

[assembly: AssemblyFixture(typeof(Probe.Bddfy.Setup))]
[assembly: CollectionBehavior(DisableTestParallelization = true)]
[assembly: TrackSteps]

namespace Probe.Bddfy;

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
        Skeleton.Write(Environment.GetEnvironmentVariable("PROBE_NAME") ?? "bddfy");
        return ValueTask.CompletedTask;
    }
}

public class Scenarios
{
    private static HttpClient New(bool s = true, bool a = true) => new(new TestTrackingMessageHandler(new BDDfyTestTrackingMessageHandlerOptions
    {
        CallerName = "Probe", FixedNameForReceivingService = "Shop", TrackDuringSetup = s, TrackDuringAction = a
    }) { InnerHandler = new Answer() });

    private readonly HttpClient _http = New();
    private readonly HttpClient _noSetup = New(s: false);
    private readonly HttpClient _noAction = New(a: false);

    private async Task Call(string label)
    {
        (await _http.GetAsync($"http://probe.local/{label}")).EnsureSuccessStatusCode();
        await _noSetup.GetAsync($"http://probe.local/{label}-nosetup");
        await _noAction.GetAsync($"http://probe.local/{label}-noaction");
    }
    private void CallSync(string label) => Call(label).GetAwaiter().GetResult();

    [GivenStep] public async Task WovenGiven(string label) => await Call(label);
    [WhenStep] public async Task WovenWhen(string label) => await Call(label);

    public void Given_a_call(string label) => CallSync(label);
    public void When_a_call(string label) => CallSync(label);
    public void Then_a_call(string label) => CallSync(label);
    public void Then_a_failing_call(string label) { CallSync(label); throw new InvalidOperationException("planned"); }
    public void When_a_woven_Given_runs(string label) { WovenGiven(label).GetAwaiter().GetResult(); CallSync(label + "-after-woven"); }
    public void Given_a_woven_When_runs(string label) { WovenWhen(label).GetAwaiter().GetResult(); CallSync(label + "-after-woven"); }
    public void Given_a_hand_step_is_left_open(string label)
    {
        CallSync(label + "-first");
        StepCollector.StartStep(Xunit.TestContext.Current.Test!.UniqueID, null, "a sub-step never closed", null, null);
        CallSync(label + "-in-open-sub");
    }
    public void When_a_nested_story_runs(string label)
    {
        CallSync(label + "-before-story");
        new Scenarios().Given(x => x.Given_a_call(label + "-story-given"))
            .When(x => x.When_a_call(label + "-story-when"))
            .BDDfy("nested " + label);
        CallSync(label + "-after-story");
    }

    public void When_two_plain_calls(string label)
    {
        _http.GetAsync($"http://probe.local/{label}-1").GetAwaiter().GetResult();
        _http.GetAsync($"http://probe.local/{label}-2").GetAwaiter().GetResult();
    }

    public void Given_a_nested_story_with_a_When(string label)
    {
        CallSync(label + "-before-story");
        new NestedSteps(_http).When(x => x.When_two_plain_calls(label + "-story-when")).BDDfy("nested in a given " + label);
        CallSync(label + "-after-story");
    }

    [Fact]
    public void D08_a_nested_story_with_a_When_in_a_Given()
    {
        this.Given(_ => Given_a_nested_story_with_a_When("d08"))
            .And(_ => Given_a_call("d08-and"))
            .When(_ => When_a_call("d08-when"))
            .BDDfy();
    }

    [Fact]
    public void D01_a_woven_Given_in_a_When()
    {
        this.Given(_ => Given_a_call("d01-given"))
            .When(_ => When_a_woven_Given_runs("d01-when"))
            .Then(_ => Then_a_call("d01-then"))
            .BDDfy();
    }

    [Fact]
    public void D02_a_hand_step_left_open_in_a_Given()
    {
        this.Given(_ => Given_a_hand_step_is_left_open("d02"))
            .When(_ => When_a_call("d02-when"))
            .Then(_ => Then_a_call("d02-then"))
            .BDDfy();
    }

    [Fact]
    public void D03_a_failing_Then_with_a_second_Then()
    {
        var thrown = Record.Exception(() => this
            .Given(_ => Given_a_call("d03-given"))
            .When(_ => When_a_call("d03-when"))
            .Then(_ => Then_a_failing_call("d03-then-1"))
            .And(_ => Then_a_call("d03-then-2"))
            .BDDfy());
        Assert.NotNull(thrown);
    }

    [Fact]
    public void D04_a_nested_story_in_a_When()
    {
        this.Given(_ => Given_a_call("d04-given"))
            .When(_ => When_a_nested_story_runs("d04"))
            .Then(_ => Then_a_call("d04-then"))
            .BDDfy();
    }

    [Fact]
    public void D05_a_woven_When_in_a_Given_then_And_steps()
    {
        this.Given(_ => Given_a_woven_When_runs("d05-given"))
            .And(_ => Given_a_call("d05-and"))
            .When(_ => When_a_call("d05-when"))
            .And(_ => When_a_call("d05-when-and"))
            .Then(_ => Then_a_call("d05-then"))
            .And(_ => Then_a_call("d05-then-and"))
            .But(_ => Then_a_call("d05-then-but"))
            .BDDfy();
    }

    [Fact]
    public async Task D06_two_stories_and_calls_between_them()
    {
        await Call("d06-first");
        this.Given(_ => Given_a_call("d06-a-given")).When(_ => When_a_call("d06-a-when")).BDDfy("story one");
        await Call("d06-between");
        this.Given(_ => Given_a_call("d06-b-given")).When(_ => When_a_call("d06-b-when")).BDDfy("story two");
        await Call("d06-after");
    }

    [Fact]
    public async Task D07_a_story_inside_a_woven_When()
    {
        await RunStoryInAWhen("d07");
        await Call("d07-after");
    }

    [WhenStep]
    public async Task RunStoryInAWhen(string label)
    {
        await Call(label + "-before-story");
        this.Given(_ => Given_a_call(label + "-story-given")).When(_ => When_a_call(label + "-story-when")).BDDfy("story in a when");
        await Call(label + "-after-story");
    }

    private static string TestId => Xunit.TestContext.Current.Test!.UniqueID;

    [Step] public async Task WovenSlow(string label, Task gate) { await gate; await Call(label); }

    public void Then_a_hand_step_left_open_and_it_fails(string label)
    {
        CallSync(label + "-first");
        StepCollector.StartStep(TestId, null, "a sub-step never closed", null, null);
        CallSync(label + "-in-open-sub");
        throw new InvalidOperationException("planned");
    }

    public void Then_a_sub_step_outlives_a_timeout(string label)
    {
        CallSync(label + "-first");
        WovenSlow(label + "-slow", new TaskCompletionSource().Task).WaitAsync(TimeSpan.FromMilliseconds(20)).GetAwaiter().GetResult();
    }

    [Fact]
    public void D09_a_failing_Then_leaves_a_hand_step_open_and_a_second_Then_runs()
    {
        Assert.NotNull(Record.Exception(() => this
            .Given(_ => Given_a_call("d09-given"))
            .When(_ => When_a_call("d09-when"))
            .Then(_ => Then_a_hand_step_left_open_and_it_fails("d09-then-1"))
            .And(_ => Then_a_call("d09-then-2"))
            .BDDfy()));
    }

    [Fact]
    public void D10_a_Then_times_out_with_a_woven_sub_step_running_and_a_second_Then_runs()
    {
        Assert.NotNull(Record.Exception(() => this
            .Given(_ => Given_a_call("d10-given"))
            .When(_ => When_a_call("d10-when"))
            .Then(_ => Then_a_sub_step_outlives_a_timeout("d10-then-1"))
            .And(_ => Then_a_call("d10-then-2"))
            .BDDfy()));
    }
}

public class NestedSteps(HttpClient http)
{
    public void When_two_plain_calls(string label)
    {
        http.GetAsync($"http://probe.local/{label}-1").GetAwaiter().GetResult();
        http.GetAsync($"http://probe.local/{label}-2").GetAwaiter().GetResult();
    }
}
