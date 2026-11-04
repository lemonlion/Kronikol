"""Stamps the review's adapter suites: common shapes plus each adapter's own class-level, repeat and tear-down shapes.
Takes Run.cs and the csproj from shapes3's suites (copied, never built there). Usage: python -I stamp_adapters.py <probes> <shapes3 probes>."""
import pathlib
import re
import shutil
import sys

P = pathlib.Path(sys.argv[1])
SHAPES3 = pathlib.Path(sys.argv[2])
(P / 'adapters-shared').mkdir(exist_ok=True)
for f in (SHAPES3 / 'shared').glob('*.cs'):
    shutil.copy(f, P / 'adapters-shared' / f.name)

COMMON = r'''
__CLS__public class Shapes5 : DiagrammedComponentTest
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

    __TEST__
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

    __TEST__
    public async Task E02_Ambient_bypass_in_a_sync_Given_then_When_Then()
    {
        GivenWithAmbientBypass("e02");
        await WhenAsync("e02-act");
        await ThenAsync("e02-then");
    }

    __TEST__
    public async Task E03_Hand_Given_never_completed_then_When_Then()
    {
        StepCollector.StartStep(Id, "Given", "e03-hand", null, null);
        await _calls.Make("e03-seed");
        await WhenAsync("e03-act");
        await ThenAsync("e03-then");
    }

    __TEST__
    public async Task E04_A_SkipIf_getter_that_throws_is_caught_then_When_Then()
    {
        try { await GivenSkipThrows("e04-given"); } catch (InvalidOperationException) { }
        await WhenAsync("e04-act");
        await ThenAsync("e04-then");
    }

    __TEST__
    public async Task E05_WhenAll_of_Givens_inside_a_When()
    {
        await GivenAsync("e05-seed");
        await WhenWhenAllGivens("e05");
        await ThenAsync("e05-then");
    }

    __TEST__
    public async Task E06_Parallel_ForEach_of_sync_Givens_inside_a_When()
    {
        await GivenAsync("e06-seed");
        WhenParallelForEachGivens("e06");
        await ThenAsync("e06-then");
    }

    __TEST__
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

__CLS__public class FailingBody5 : Lifecycle5
{
    __TEARDOWN__

    __TEST__
    public async Task F01_body_fails_in_its_When_then_a_Then_in_tear_down()
    {
        await GivenAsync("f01-seed");
        await WhenThrows("f01-act");
    }

    __TEST__
    public async Task F02_body_fails_in_its_Given_then_a_Then_in_tear_down()
    {
        await GivenThrowsAsync("f02-seed");
        await WhenAsync("f02-act");
    }

    [GivenStep] public async Task GivenThrowsAsync(string label) { await C.Make(label); throw new InvalidOperationException("boom"); }
}
'''

ADAPTERS = {
    'nunit4': dict(
        ns='Kronikol.NUnit4', using='using NUnit.Framework;', test='[Test]',
        cls='[FixtureLifeCycle(LifeCycle.InstancePerTestCase)]\n',
        teardown='[TearDown] public async Task Down() { await ThenAsync("teardown-then"); await C.Make("teardown-after"); }',
        own=r'''
[TestFixture]
public class ClassLevel5 : Lifecycle5
{
    private static readonly Calls Fixture = ProbeEnv.NewCalls(trackDuringSetup: false);
    private static TestPhase _afterSyncFixture;

    [Test]
    public async Task C01_a_test_after_one_time_set_up()
    {
        await C.Make("c01-first-in-test");
        await NoSetup.Make("c01-first-nosetup-in-test");
        await GivenAsync("c01-seed");
        await WhenAsync("c01-act");
    }
    [GivenStep] public void GivenSyncFixture(string label) => Fixture.Make(label).GetAwaiter().GetResult();
    [GivenStep] public async Task GivenFixture(string label) => await Fixture.Make(label);

    [OneTimeSetUp]
    public async Task Seed()
    {
        using (TestIdentityScope.Begin("C01 one time set up", "c01-one-time"))
        {
            GivenSyncFixture("c01-sync-seed");
            await GivenFixture("c01-async-seed");
            await Fixture.Make("c01-after-seeds");
        }
        _afterSyncFixture = TestPhaseContext.Current;
    }
}

[FixtureLifeCycle(LifeCycle.InstancePerTestCase)]
public class Repeated5 : Lifecycle5
{
    [Test, Repeat(2)]
    public async Task R01_Repeat_two_Given_When_with_nested_Given_Then()
    {
        await GivenAsync("r01-seed");
        await WhenWithSub("r01");
        await ThenAsync("r01-then");
    }

    [WhenStep] public async Task WhenWithSub(string l) { await C.Make(l + "-before"); await GivenAsync(l + "-sub"); await C.Make(l + "-after"); }

    [Test, Repeat(2)]
    public void R02_Repeat_sync_a_hand_And_first_in_each_run()
    {
        StepCollector.StartStep(Track.TestIdResolver!()!, "And", "r02-and", null, null);
        C.Make("r02-in-and").GetAwaiter().GetResult();
        StepCollector.CompleteStep(Track.TestIdResolver!()!, true);
        GivenSync("r02-seed");
        WhenAsync("r02-act").GetAwaiter().GetResult();
    }
}
'''),
    'mstest': dict(
        ns='Kronikol.MSTest', using='using Microsoft.VisualStudio.TestTools.UnitTesting;', test='[TestMethod]',
        cls='[TestClass]\n',
        teardown='[TestCleanup] public async Task Down() { await ThenAsync("teardown-then"); await C.Make("teardown-after"); }',
        own=r'''
[TestClass]
public class ClassLevel5 : Lifecycle5
{
    private static readonly Calls Fixture = ProbeEnv.NewCalls(trackDuringSetup: false);

    [ClassInitialize]
    public static async Task Seed(TestContext context)
    {
        using (TestIdentityScope.Begin("C01 class initialize", "c01-class"))
        {
            await new ClassLevelSteps(Fixture).GivenFixture("c01-async-seed");
            new ClassLevelSteps(Fixture).GivenSyncFixture("c01-sync-seed");
            await Fixture.Make("c01-after-seeds");
        }
    }

    [TestMethod]
    public async Task C01_a_test_after_class_initialize()
    {
        await C.Make("c01-first-in-test");
        await NoSetup.Make("c01-first-nosetup-in-test");
        await GivenAsync("c01-seed");
        await WhenAsync("c01-act");
    }
}

public class ClassLevelSteps(Calls calls)
{
    [GivenStep] public void GivenSyncFixture(string label) => calls.Make(label).GetAwaiter().GetResult();
    [GivenStep] public async Task GivenFixture(string label) => await calls.Make(label);
}

[TestClass]
public class Rows5 : Lifecycle5
{
    [TestMethod]
    [DataRow("a")]
    [DataRow("b")]
    public async Task R01_rows_Given_When(string row)
    {
        await GivenAsync($"r01-{row}-seed");
        await WhenAsync($"r01-{row}-act");
    }
}
'''),
    'tunit': dict(
        ns='Kronikol.TUnit', using='using TUnit.Core;', test='[Test]',
        cls='',
        teardown='[After(Test)] public async Task Down() { await ThenAsync("teardown-then"); await C.Make("teardown-after"); }',
        own=r'''
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
'''),
    'xunit2': dict(
        ns='Kronikol.xUnit2', using='using Xunit;', test='[Fact]',
        cls='[Collection(DiagrammedComponentTest.DiagrammedTestCollectionName)]\n',
        teardown='public override void Dispose_() { }',
        own=r'''
public class SeedFixture5 : IAsyncLifetime
{
    private static readonly Calls Fixture = ProbeEnv.NewCalls(trackDuringSetup: false);

    public async Task InitializeAsync()
    {
        using (TestIdentityScope.Begin("C01 class fixture", "c01-class"))
        {
            await new ClassLevelSteps(Fixture).GivenFixture("c01-async-seed");
            new ClassLevelSteps(Fixture).GivenSyncFixture("c01-sync-seed");
            await Fixture.Make("c01-after-seeds");
        }
    }

    public Task DisposeAsync() => Task.CompletedTask;
}

public class ClassLevelSteps(Calls calls)
{
    [GivenStep] public void GivenSyncFixture(string label) => calls.Make(label).GetAwaiter().GetResult();
    [GivenStep] public async Task GivenFixture(string label) => await calls.Make(label);
}

[Collection(DiagrammedComponentTest.DiagrammedTestCollectionName)]
public class ClassLevel5 : Lifecycle5, IClassFixture<SeedFixture5>
{
    [Fact]
    public async Task C01_a_test_after_a_class_fixture()
    {
        await C.Make("c01-first-in-test");
        await GivenAsync("c01-seed");
        await WhenAsync("c01-act");
    }
}
'''),
}

for name, a in ADAPTERS.items():
    src = SHAPES3 / name
    d = P / f'adapter-{name}'
    d.mkdir(exist_ok=True)
    csproj = next(src.glob('*.csproj')).read_text(encoding='utf-8').replace('..\\shared\\*.cs', '..\\adapters-shared\\*.cs')
    (d / f'Probe.{name}.csproj').write_text(csproj, encoding='utf-8', newline='\n')
    run = (src / 'Run.cs').read_text(encoding='utf-8').replace('\r\n', '\n')
    run = re.sub(r'bool trackDuringSetup = true\)', 'bool trackDuringSetup = true)', run)
    (d / 'Run.cs').write_text(run, encoding='utf-8', newline='\n')
    teardown = a['teardown']
    body = COMMON.replace('__CLS__', a['cls']).replace('__TEST__', a['test']).replace('__TEARDOWN__', teardown if name != 'xunit2' else '')
    header = (f"// Stamped by stamp_adapters.py: edit that.\nusing Kronikol;\nusing Kronikol.Tracking;\nusing {a['ns']};\nusing PhaseProbe;\n{a['using']}\n\n"
              f"namespace PhaseProbe.Suite;\n")
    (d / 'Shapes5.g.cs').write_text(header + body + a['own'], encoding='utf-8', newline='\n')
    print('stamped', d.name)
