// Stamped by stamp_adapters.py: edit that.
using Kronikol;
using Kronikol.Tracking;
using Kronikol.TUnit;
using PhaseProbe;
using TUnit.Core;

namespace PhaseProbe.Suite;

public class Shapes5 : DiagrammedComponentTest
{
    private readonly Calls _calls = ProbeEnv.NewCalls();
    private readonly Calls _noSetup = ProbeEnv.NewCalls(trackDuringSetup: false);

    private static string Id => Track.TestIdResolver!()!;

    [GivenStep] public async Task GivenAsync(string label) => await _calls.Make(label);
    [GivenStep] public void GivenSync(string label) => _calls.Make(label).GetAwaiter().GetResult();
    [WhenStep] public async Task WhenAsync(string label) => await _calls.Make(label);
    [ThenStep] public async Task ThenAsync(string label) => await _calls.Make(label);

    public bool Throws => throw new InvalidOperationException("skip getter");
    [GivenStep(SkipIf = nameof(Throws))] public async Task GivenSkipThrows(string label) => await _calls.Make(label);

    [GivenStep]
    public void GivenWithAmbientBypass(string label)
    {
        StepCollector.StartStep(null, label + "-manual", null, null);
        StepCollector.BypassStep("manual");
    }

    [WhenStep]
    public async Task WhenWhenAllGivens(string label)
    {
        await Task.WhenAll(GivenAsync(label + "-x"), GivenAsync(label + "-y"));
        await _calls.Make(label + "-after");
    }

    [WhenStep]
    public void WhenParallelForEachGivens(string label)
    {
        Parallel.ForEach(new[] { "a", "b", "c" }, new ParallelOptions { MaxDegreeOfParallelism = 3 }, l => GivenSync($"{label}-{l}"));
        _calls.Make(label + "-after").GetAwaiter().GetResult();
    }

    [Test]
    public async Task E01_Then_poller_forked_at_the_start_fires_during_the_Given()
    {
        var gate = new TaskCompletionSource();
        var done = new TaskCompletionSource();
        var background = Task.Run(async () =>
        {
            await gate.Task;
            await ThenAsync("e01-poll");
            done.SetResult();
        });
        await GivenAsync("e01-seed1");
        gate.SetResult();
        await done.Task;
        await GivenAsync("e01-seed2");
        await WhenAsync("e01-act");
        await background;
    }

    [Test]
    public async Task E02_Ambient_bypass_in_a_sync_Given_then_When_Then()
    {
        GivenWithAmbientBypass("e02");
        await WhenAsync("e02-act");
        await ThenAsync("e02-then");
    }

    [Test]
    public async Task E03_Hand_Given_never_completed_then_When_Then()
    {
        StepCollector.StartStep(Id, "Given", "e03-hand", null, null);
        await _calls.Make("e03-seed");
        await WhenAsync("e03-act");
        await ThenAsync("e03-then");
    }

    [Test]
    public async Task E04_A_SkipIf_getter_that_throws_is_caught_then_When_Then()
    {
        try { await GivenSkipThrows("e04-given"); } catch (InvalidOperationException) { }
        await WhenAsync("e04-act");
        await ThenAsync("e04-then");
    }

    [Test]
    public async Task E05_WhenAll_of_Givens_inside_a_When()
    {
        await GivenAsync("e05-seed");
        await WhenWhenAllGivens("e05");
        await ThenAsync("e05-then");
    }

    [Test]
    public async Task E06_Parallel_ForEach_of_sync_Givens_inside_a_When()
    {
        await GivenAsync("e06-seed");
        WhenParallelForEachGivens("e06");
        await ThenAsync("e06-then");
    }

    [Test]
    public async Task E07_A_flow_forked_before_the_Given_runs_a_consumer_with_SetFromMessage()
    {
        var name = "E07";
        var id = Id;
        var gate = new TaskCompletionSource();
        var done = new TaskCompletionSource();
        var consumer = Task.Run(async () =>
        {
            await gate.Task;
            TestIdentityScope.SetFromMessage(name, id);
            await WhenAsync("e07-consumer-when");
            await _noSetup.Make("e07-consumer-after");
            done.SetResult();
        });
        await GivenAsync("e07-seed1");
        gate.SetResult();
        await done.Task;
        await GivenAsync("e07-seed2");
        await WhenAsync("e07-act");
        await consumer;
    }
}

public abstract class Lifecycle5 : DiagrammedComponentTest
{
    protected readonly Calls C = ProbeEnv.NewCalls();
    protected readonly Calls NoSetup = ProbeEnv.NewCalls(trackDuringSetup: false);
    [GivenStep] public async Task GivenAsync(string label) => await C.Make(label);
    [GivenStep] public void GivenSync(string label) => C.Make(label).GetAwaiter().GetResult();
    [WhenStep] public async Task WhenAsync(string label) => await C.Make(label);
    [WhenStep] public async Task WhenThrows(string label) { await C.Make(label); throw new InvalidOperationException("boom"); }
    [ThenStep] public async Task ThenAsync(string label) => await C.Make(label);
    [ThenStep] public void ThenSync(string label) => C.Make(label).GetAwaiter().GetResult();
}

public class FailingBody5 : Lifecycle5
{
    [After(Test)] public async Task Down() { await ThenAsync("teardown-then"); await C.Make("teardown-after"); }

    [Test]
    public async Task F01_body_fails_in_its_When_then_a_Then_in_tear_down()
    {
        await GivenAsync("f01-seed");
        await WhenThrows("f01-act");
    }

    [Test]
    public async Task F02_body_fails_in_its_Given_then_a_Then_in_tear_down()
    {
        await GivenThrowsAsync("f02-seed");
        await WhenAsync("f02-act");
    }

    [GivenStep] public async Task GivenThrowsAsync(string label) { await C.Make(label); throw new InvalidOperationException("boom"); }
}

public class ClassLevel5 : Lifecycle5
{
    private static readonly Calls Fixture = ProbeEnv.NewCalls(trackDuringSetup: false);

    [Before(Class)]
    public static async Task Seed(ClassHookContext context)
    {
        using (TestIdentityScope.Begin("C01 before class", "c01-class"))
        {
            await new ClassLevelSteps(Fixture).GivenFixture("c01-async-seed");
            new ClassLevelSteps(Fixture).GivenSyncFixture("c01-sync-seed");
            await Fixture.Make("c01-after-seeds");
        }
    }

    [Test]
    public async Task C01_a_test_after_before_class()
    {
        await C.Make("c01-first-in-test");
        await GivenAsync("c01-seed");
        await WhenAsync("c01-act");
    }
}

public class ClassLevelSteps(Calls calls)
{
    [GivenStep] public void GivenSyncFixture(string label) => calls.Make(label).GetAwaiter().GetResult();
    [GivenStep] public async Task GivenFixture(string label) => await calls.Make(label);
}

public class Repeated5 : Lifecycle5
{
    [Test, Repeat(1)]
    public async Task R01_Repeat_one_Given_When_with_nested_Given_Then()
    {
        await GivenAsync("r01-seed");
        await WhenWithSub("r01");
        await ThenAsync("r01-then");
    }

    [WhenStep] public async Task WhenWithSub(string l) { await C.Make(l + "-before"); await GivenAsync(l + "-sub"); await C.Make(l + "-after"); }
}
