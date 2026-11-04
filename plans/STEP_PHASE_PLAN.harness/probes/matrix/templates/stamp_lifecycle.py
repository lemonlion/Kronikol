#!/usr/bin/env python
"""Stamps each adapter's lifecycle shapes: a step in a set-up or tear-down method or a test constructor, then test bodies that say what later calls read.
Usage: python -I templates/stamp_lifecycle.py (from probes/)."""
import pathlib

PROBES = pathlib.Path(__file__).resolve().parent.parent

# Keep the lifecycle's own call and step on one flow, as a reader would write them.
GIVEN_ASYNC = 'await GivenAsync("lifecycle-given"); await Calls.Make("lifecycle-after-given");'
GIVEN_SYNC = 'GivenSync("lifecycle-given"); Calls.Make("lifecycle-after-given").GetAwaiter().GetResult();'
WHEN_ASYNC = 'await WhenAsync("teardown-when"); await Calls.Make("teardown-after-when");'
WHEN_SYNC = 'WhenSync("teardown-when"); Calls.Make("teardown-after-when").GetAwaiter().GetResult();'

ADAPTERS = {
    'nunit4': dict(
        ns='Kronikol.NUnit4', using='using NUnit.Framework;', test='[Test]',
        cls='[FixtureLifeCycle(LifeCycle.InstancePerTestCase)]\n', base='LifecycleBase',
        setups=[
            ('L01_SetUp_async', f'[SetUp] public async Task Init() {{ {GIVEN_ASYNC} }}'),
            ('L02_SetUp_returns_the_Task', '[SetUp] public Task Init() => GivenTask("lifecycle-given");'),
            ('L03_SetUp_sync', f'[SetUp] public void Init() {{ {GIVEN_SYNC} }}'),
            ('L04_constructor', f'public L04_constructor() {{ {GIVEN_SYNC} }}'),
        ],
        teardowns=[
            ('L11_TearDown_async', f'[TearDown] public async Task Down() {{ {WHEN_ASYNC} }}'),
            ('L12_TearDown_sync', f'[TearDown] public void Down() {{ {WHEN_SYNC} }}'),
        ]),
    'mstest': dict(
        ns='Kronikol.MSTest', using='using Microsoft.VisualStudio.TestTools.UnitTesting;', test='[TestMethod]',
        cls='[TestClass]\n', base='LifecycleBase',
        setups=[
            ('L01_TestInitialize_async', f'[TestInitialize] public async Task Init() {{ {GIVEN_ASYNC} }}'),
            ('L02_TestInitialize_returns_the_Task', '[TestInitialize] public Task Init() => GivenTask("lifecycle-given");'),
            ('L03_TestInitialize_sync', f'[TestInitialize] public void Init() {{ {GIVEN_SYNC} }}'),
            ('L04_constructor', f'public L04_constructor() {{ {GIVEN_SYNC} }}'),
        ],
        teardowns=[
            ('L11_TestCleanup_async', f'[TestCleanup] public async Task Down() {{ {WHEN_ASYNC} }}'),
            ('L12_TestCleanup_sync', f'[TestCleanup] public void Down() {{ {WHEN_SYNC} }}'),
        ]),
    'tunit': dict(
        ns='Kronikol.TUnit', using='using TUnit.Core;', test='[Test]',
        cls='', base='LifecycleBase',
        setups=[
            ('L01_Before_Test_async', f'[Before(Test)] public async Task Init() {{ {GIVEN_ASYNC} }}'),
            ('L02_Before_Test_returns_the_Task', '[Before(Test)] public Task Init() => GivenTask("lifecycle-given");'),
            ('L03_Before_Test_sync', f'[Before(Test)] public void Init() {{ {GIVEN_SYNC} }}'),
            ('L04_constructor', f'public L04_constructor() {{ {GIVEN_SYNC} }}'),
            ('L05_Before_Test_async_AddAsyncLocalValues', f'[Before(Test)] public async Task Init(TestContext context) {{ {GIVEN_ASYNC} context.AddAsyncLocalValues(); }}'),
            ('L06_Before_Test_sync_AddAsyncLocalValues', f'[Before(Test)] public void Init(TestContext context) {{ {GIVEN_SYNC} context.AddAsyncLocalValues(); }}'),
        ],
        teardowns=[
            ('L11_After_Test_async', f'[After(Test)] public async Task Down() {{ {WHEN_ASYNC} }}'),
            ('L12_After_Test_sync', f'[After(Test)] public void Down() {{ {WHEN_SYNC} }}'),
        ]),
    'xunit3': dict(
        ns='Kronikol.xUnit3', using='using Xunit;', test='[Fact]',
        cls='', base='LifecycleBase, IAsyncLifetime',
        setups=[
            ('L01_InitializeAsync_async', f'public async ValueTask InitializeAsync() {{ {GIVEN_ASYNC} }}\n    public ValueTask DisposeAsync() => default;'),
            ('L02_InitializeAsync_returns_the_Task', 'public ValueTask InitializeAsync() => new(GivenTask("lifecycle-given"));\n    public ValueTask DisposeAsync() => default;'),
            ('L03_InitializeAsync_sync_step', f'public ValueTask InitializeAsync() {{ {GIVEN_SYNC} return default; }}\n    public ValueTask DisposeAsync() => default;'),
            ('L04_constructor', f'public L04_constructor() {{ {GIVEN_SYNC} }}\n    public ValueTask InitializeAsync() => default;\n    public ValueTask DisposeAsync() => default;'),
        ],
        teardowns=[
            ('L11_DisposeAsync_async', f'public ValueTask InitializeAsync() => default;\n    public async ValueTask DisposeAsync() {{ {WHEN_ASYNC} }}'),
            ('L12_DisposeAsync_sync_step', f'public ValueTask InitializeAsync() => default;\n    public ValueTask DisposeAsync() {{ {WHEN_SYNC} return default; }}'),
            ('L13_Dispose', 'NOLIFETIME'),
        ]),
    'xunit2': dict(
        ns='Kronikol.xUnit2', using='using Xunit;', test='[Fact]',
        cls='', base='LifecycleBase, IAsyncLifetime',
        setups=[
            ('L01_InitializeAsync_async', f'public async Task InitializeAsync() {{ {GIVEN_ASYNC} }}\n    public Task DisposeAsync() => Task.CompletedTask;'),
            ('L02_InitializeAsync_returns_the_Task', 'public Task InitializeAsync() => GivenTask("lifecycle-given");\n    public Task DisposeAsync() => Task.CompletedTask;'),
            ('L04_constructor', f'public L04_constructor() {{ {GIVEN_SYNC} }}\n    public Task InitializeAsync() => Task.CompletedTask;\n    public Task DisposeAsync() => Task.CompletedTask;'),
        ],
        teardowns=[
            ('L11_DisposeAsync_async', f'public Task InitializeAsync() => Task.CompletedTask;\n    public async Task DisposeAsync() {{ {WHEN_ASYNC} }}'),
            ('L12_Dispose_sync', 'NOLIFETIME'),
        ]),
}

SETUP_BODIES = '''
    /// <summary>Later calls and a When in the body. Is the boundary at the When, and what did the calls before it read?</summary>
    {test}
    public async Task {cls}__then_a_When()
    {{
        await Calls.Make("body-first");
        await WhenAsync("body-when");
        await Calls.Make("body-after-when");
        await ThenAsync("body-then");
    }}

    /// <summary>Calls alone in the body.</summary>
    {test}
    public async Task {cls}__plain_calls_only()
    {{
        await Calls.Make("body-first");
        await Calls.Make("body-second");
    }}

    /// <summary>A step that says nothing in the body: it continues the phase, as X06's does.</summary>
    {test}
    public async Task {cls}__a_keyword_free_step()
    {{
        await Calls.Make("body-first");
        await PlainAsync("body-keyword-free");
        await Calls.Make("body-after-keyword-free");
    }}

    /// <summary>The body's work is a step that says nothing, on trackers with TrackDuringSetup off: is it recorded?</summary>
    {test}
    public async Task {cls}__a_keyword_free_action_with_setup_tracking_off()
    {{
        Calls = ProbeEnv.NewCalls(trackDuringSetup: false);
        await Calls.Make("body-first");
        await PlainAsync("body-keyword-free-action");
        await Calls.Make("body-after-keyword-free");
    }}

    /// <summary>The body has a Given of its own.</summary>
    {test}
    public async Task {cls}__a_Given_then_a_When()
    {{
        await Calls.Make("body-first");
        await GivenAsync("body-given");
        await Calls.Make("body-after-given");
        await WhenAsync("body-when");
        await Calls.Make("body-after-when");
    }}
'''

TEARDOWN_BODIES = '''
    /// <summary>The body has its Given and its When: the teardown's When comes after the boundary.</summary>
    {test}
    public async Task {cls}__Given_and_When_in_the_body()
    {{
        await GivenAsync("body-given");
        await Calls.Make("body-after-given");
        await WhenAsync("body-when");
        await Calls.Make("body-after-when");
    }}

    /// <summary>The body has a Given, then does its real work with no When: the only When is the teardown's.</summary>
    {test}
    public async Task {cls}__Given_then_calls_in_the_body()
    {{
        await GivenAsync("body-given");
        await Calls.Make("body-the-real-work");
    }}

    /// <summary>The body has no steps.</summary>
    {test}
    public async Task {cls}__no_steps_in_the_body()
    {{
        await Calls.Make("body-first");
    }}
'''

BASE = '''
/// <summary>The steps the lifecycle shapes call.</summary>
public abstract class LifecycleBase : DiagrammedComponentTest
{
    protected Calls Calls = ProbeEnv.NewCalls();

    [GivenStep] public async Task GivenAsync(string label) => await Calls.Make(label);
    [GivenStep] public Task GivenTask(string label) => Calls.Make(label);
    [GivenStep] public void GivenSync(string label) => Calls.Make(label).GetAwaiter().GetResult();
    [WhenStep] public async Task WhenAsync(string label) => await Calls.Make(label);
    [WhenStep] public void WhenSync(string label) => Calls.Make(label).GetAwaiter().GetResult();
    [ThenStep] public async Task ThenAsync(string label) => await Calls.Make(label);
    [Step] public async Task PlainAsync(string label) => await Calls.Make(label);
}
'''

for name, a in ADAPTERS.items():
    out = [f"// Stamped by templates/stamp_lifecycle.py: edit that.\nusing Kronikol;\nusing Kronikol.Tracking;\nusing {a['ns']};\nusing PhaseProbe;\n{a['using']}\n\nnamespace PhaseProbe.Suite;\n", BASE]
    for cls, code in a['setups']:
        out.append(f"\n{a['cls']}public class {cls} : {a['base']}\n{{\n    {code}\n{SETUP_BODIES.format(test=a['test'], cls=cls)}}}\n")
    for cls, code in a['teardowns']:
        base = a['base']
        if code == 'NOLIFETIME' and name == 'xunit3':
            # xUnit v3 calls Dispose only on a class with no DisposeAsync. The base's Dispose captures the test, so this comes first.
            base = 'LifecycleBase, IDisposable'
            code = f'public new void Dispose() {{ {WHEN_SYNC} base.Dispose(); }}'
        elif code == 'NOLIFETIME':
            base = 'LifecycleBase'
            code = f'protected override void Dispose(bool disposing) {{ {WHEN_SYNC} }}'
        out.append(f"\n{a['cls']}public class {cls} : {base}\n{{\n    {code}\n{TEARDOWN_BODIES.format(test=a['test'], cls=cls)}}}\n")
    # A keyword-free step in an async tear-down: its flow has no phase of its own unless the framework carries the test's.
    TEARDOWN_PLAIN = 'await PlainAsync("teardown-keyword-free"); await Calls.Make("teardown-after-keyword-free");'
    down = dict(nunit4=f'[TearDown] public async Task Down() {{ {TEARDOWN_PLAIN} }}', mstest=f'[TestCleanup] public async Task Down() {{ {TEARDOWN_PLAIN} }}',
                tunit=f'[After(Test)] public async Task Down() {{ {TEARDOWN_PLAIN} }}',
                xunit3=f'public ValueTask InitializeAsync() => default;\n    public async ValueTask DisposeAsync() {{ {TEARDOWN_PLAIN} }}').get(name)
    if down:
        cls = 'L14_teardown_keyword_free_step'
        bodies = TEARDOWN_BODIES.format(test=a['test'], cls=cls) + f'''
    /// <summary>The trackers have TrackDuringSetup off, and the body has a Given: does the tear-down's work go too?</summary>
    {a['test']}
    public async Task {cls}__Given_with_setup_tracking_off()
    {{
        Calls = ProbeEnv.NewCalls(trackDuringSetup: false);
        await GivenAsync("body-given");
        await Calls.Make("body-the-real-work");
    }}
'''
        out.append(f"\n{a['cls']}public class {cls} : {a['base']}\n{{\n    {down}\n{bodies}}}\n")
    if name == 'nunit4':
        # NUnit's default: one instance for the fixture's tests, built before any of them, so a step in its constructor has no test.
        cls = 'L07_constructor_shared_fixture'
        out.append(f"\npublic class {cls} : {a['base']}\n{{\n    public {cls}() {{ {GIVEN_SYNC} }}\n{SETUP_BODIES.format(test=a['test'], cls=cls)}}}\n")
    (PROBES / name / 'Lifecycle.g.cs').write_text(''.join(out), encoding='utf-8', newline='\n')
    print('stamped lifecycle', name)
