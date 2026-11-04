// Stamped by templates/stamp_lifecycle.py: edit that.
using Kronikol;
using Kronikol.Tracking;
using Kronikol.NUnit4;
using PhaseProbe;
using NUnit.Framework;

namespace PhaseProbe.Suite;

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

[FixtureLifeCycle(LifeCycle.InstancePerTestCase)]
public class L01_SetUp_async : LifecycleBase
{
    [SetUp] public async Task Init() { await GivenAsync("lifecycle-given"); await Calls.Make("lifecycle-after-given"); }

    /// <summary>Later calls and a When in the body. Is the boundary at the When, and what did the calls before it read?</summary>
    [Test]
    public async Task L01_SetUp_async__then_a_When()
    {
        await Calls.Make("body-first");
        await WhenAsync("body-when");
        await Calls.Make("body-after-when");
        await ThenAsync("body-then");
    }

    /// <summary>Calls alone in the body.</summary>
    [Test]
    public async Task L01_SetUp_async__plain_calls_only()
    {
        await Calls.Make("body-first");
        await Calls.Make("body-second");
    }

    /// <summary>A step that says nothing in the body: it continues the phase, as X06's does.</summary>
    [Test]
    public async Task L01_SetUp_async__a_keyword_free_step()
    {
        await Calls.Make("body-first");
        await PlainAsync("body-keyword-free");
        await Calls.Make("body-after-keyword-free");
    }

    /// <summary>The body's work is a step that says nothing, on trackers with TrackDuringSetup off: is it recorded?</summary>
    [Test]
    public async Task L01_SetUp_async__a_keyword_free_action_with_setup_tracking_off()
    {
        Calls = ProbeEnv.NewCalls(trackDuringSetup: false);
        await Calls.Make("body-first");
        await PlainAsync("body-keyword-free-action");
        await Calls.Make("body-after-keyword-free");
    }

    /// <summary>The body has a Given of its own.</summary>
    [Test]
    public async Task L01_SetUp_async__a_Given_then_a_When()
    {
        await Calls.Make("body-first");
        await GivenAsync("body-given");
        await Calls.Make("body-after-given");
        await WhenAsync("body-when");
        await Calls.Make("body-after-when");
    }
}

[FixtureLifeCycle(LifeCycle.InstancePerTestCase)]
public class L02_SetUp_returns_the_Task : LifecycleBase
{
    [SetUp] public Task Init() => GivenTask("lifecycle-given");

    /// <summary>Later calls and a When in the body. Is the boundary at the When, and what did the calls before it read?</summary>
    [Test]
    public async Task L02_SetUp_returns_the_Task__then_a_When()
    {
        await Calls.Make("body-first");
        await WhenAsync("body-when");
        await Calls.Make("body-after-when");
        await ThenAsync("body-then");
    }

    /// <summary>Calls alone in the body.</summary>
    [Test]
    public async Task L02_SetUp_returns_the_Task__plain_calls_only()
    {
        await Calls.Make("body-first");
        await Calls.Make("body-second");
    }

    /// <summary>A step that says nothing in the body: it continues the phase, as X06's does.</summary>
    [Test]
    public async Task L02_SetUp_returns_the_Task__a_keyword_free_step()
    {
        await Calls.Make("body-first");
        await PlainAsync("body-keyword-free");
        await Calls.Make("body-after-keyword-free");
    }

    /// <summary>The body's work is a step that says nothing, on trackers with TrackDuringSetup off: is it recorded?</summary>
    [Test]
    public async Task L02_SetUp_returns_the_Task__a_keyword_free_action_with_setup_tracking_off()
    {
        Calls = ProbeEnv.NewCalls(trackDuringSetup: false);
        await Calls.Make("body-first");
        await PlainAsync("body-keyword-free-action");
        await Calls.Make("body-after-keyword-free");
    }

    /// <summary>The body has a Given of its own.</summary>
    [Test]
    public async Task L02_SetUp_returns_the_Task__a_Given_then_a_When()
    {
        await Calls.Make("body-first");
        await GivenAsync("body-given");
        await Calls.Make("body-after-given");
        await WhenAsync("body-when");
        await Calls.Make("body-after-when");
    }
}

[FixtureLifeCycle(LifeCycle.InstancePerTestCase)]
public class L03_SetUp_sync : LifecycleBase
{
    [SetUp] public void Init() { GivenSync("lifecycle-given"); Calls.Make("lifecycle-after-given").GetAwaiter().GetResult(); }

    /// <summary>Later calls and a When in the body. Is the boundary at the When, and what did the calls before it read?</summary>
    [Test]
    public async Task L03_SetUp_sync__then_a_When()
    {
        await Calls.Make("body-first");
        await WhenAsync("body-when");
        await Calls.Make("body-after-when");
        await ThenAsync("body-then");
    }

    /// <summary>Calls alone in the body.</summary>
    [Test]
    public async Task L03_SetUp_sync__plain_calls_only()
    {
        await Calls.Make("body-first");
        await Calls.Make("body-second");
    }

    /// <summary>A step that says nothing in the body: it continues the phase, as X06's does.</summary>
    [Test]
    public async Task L03_SetUp_sync__a_keyword_free_step()
    {
        await Calls.Make("body-first");
        await PlainAsync("body-keyword-free");
        await Calls.Make("body-after-keyword-free");
    }

    /// <summary>The body's work is a step that says nothing, on trackers with TrackDuringSetup off: is it recorded?</summary>
    [Test]
    public async Task L03_SetUp_sync__a_keyword_free_action_with_setup_tracking_off()
    {
        Calls = ProbeEnv.NewCalls(trackDuringSetup: false);
        await Calls.Make("body-first");
        await PlainAsync("body-keyword-free-action");
        await Calls.Make("body-after-keyword-free");
    }

    /// <summary>The body has a Given of its own.</summary>
    [Test]
    public async Task L03_SetUp_sync__a_Given_then_a_When()
    {
        await Calls.Make("body-first");
        await GivenAsync("body-given");
        await Calls.Make("body-after-given");
        await WhenAsync("body-when");
        await Calls.Make("body-after-when");
    }
}

[FixtureLifeCycle(LifeCycle.InstancePerTestCase)]
public class L04_constructor : LifecycleBase
{
    public L04_constructor() { GivenSync("lifecycle-given"); Calls.Make("lifecycle-after-given").GetAwaiter().GetResult(); }

    /// <summary>Later calls and a When in the body. Is the boundary at the When, and what did the calls before it read?</summary>
    [Test]
    public async Task L04_constructor__then_a_When()
    {
        await Calls.Make("body-first");
        await WhenAsync("body-when");
        await Calls.Make("body-after-when");
        await ThenAsync("body-then");
    }

    /// <summary>Calls alone in the body.</summary>
    [Test]
    public async Task L04_constructor__plain_calls_only()
    {
        await Calls.Make("body-first");
        await Calls.Make("body-second");
    }

    /// <summary>A step that says nothing in the body: it continues the phase, as X06's does.</summary>
    [Test]
    public async Task L04_constructor__a_keyword_free_step()
    {
        await Calls.Make("body-first");
        await PlainAsync("body-keyword-free");
        await Calls.Make("body-after-keyword-free");
    }

    /// <summary>The body's work is a step that says nothing, on trackers with TrackDuringSetup off: is it recorded?</summary>
    [Test]
    public async Task L04_constructor__a_keyword_free_action_with_setup_tracking_off()
    {
        Calls = ProbeEnv.NewCalls(trackDuringSetup: false);
        await Calls.Make("body-first");
        await PlainAsync("body-keyword-free-action");
        await Calls.Make("body-after-keyword-free");
    }

    /// <summary>The body has a Given of its own.</summary>
    [Test]
    public async Task L04_constructor__a_Given_then_a_When()
    {
        await Calls.Make("body-first");
        await GivenAsync("body-given");
        await Calls.Make("body-after-given");
        await WhenAsync("body-when");
        await Calls.Make("body-after-when");
    }
}

[FixtureLifeCycle(LifeCycle.InstancePerTestCase)]
public class L11_TearDown_async : LifecycleBase
{
    [TearDown] public async Task Down() { await WhenAsync("teardown-when"); await Calls.Make("teardown-after-when"); }

    /// <summary>The body has its Given and its When: the teardown's When comes after the boundary.</summary>
    [Test]
    public async Task L11_TearDown_async__Given_and_When_in_the_body()
    {
        await GivenAsync("body-given");
        await Calls.Make("body-after-given");
        await WhenAsync("body-when");
        await Calls.Make("body-after-when");
    }

    /// <summary>The body has a Given, then does its real work with no When: the only When is the teardown's.</summary>
    [Test]
    public async Task L11_TearDown_async__Given_then_calls_in_the_body()
    {
        await GivenAsync("body-given");
        await Calls.Make("body-the-real-work");
    }

    /// <summary>The body has no steps.</summary>
    [Test]
    public async Task L11_TearDown_async__no_steps_in_the_body()
    {
        await Calls.Make("body-first");
    }
}

[FixtureLifeCycle(LifeCycle.InstancePerTestCase)]
public class L12_TearDown_sync : LifecycleBase
{
    [TearDown] public void Down() { WhenSync("teardown-when"); Calls.Make("teardown-after-when").GetAwaiter().GetResult(); }

    /// <summary>The body has its Given and its When: the teardown's When comes after the boundary.</summary>
    [Test]
    public async Task L12_TearDown_sync__Given_and_When_in_the_body()
    {
        await GivenAsync("body-given");
        await Calls.Make("body-after-given");
        await WhenAsync("body-when");
        await Calls.Make("body-after-when");
    }

    /// <summary>The body has a Given, then does its real work with no When: the only When is the teardown's.</summary>
    [Test]
    public async Task L12_TearDown_sync__Given_then_calls_in_the_body()
    {
        await GivenAsync("body-given");
        await Calls.Make("body-the-real-work");
    }

    /// <summary>The body has no steps.</summary>
    [Test]
    public async Task L12_TearDown_sync__no_steps_in_the_body()
    {
        await Calls.Make("body-first");
    }
}

[FixtureLifeCycle(LifeCycle.InstancePerTestCase)]
public class L14_teardown_keyword_free_step : LifecycleBase
{
    [TearDown] public async Task Down() { await PlainAsync("teardown-keyword-free"); await Calls.Make("teardown-after-keyword-free"); }

    /// <summary>The body has its Given and its When: the teardown's When comes after the boundary.</summary>
    [Test]
    public async Task L14_teardown_keyword_free_step__Given_and_When_in_the_body()
    {
        await GivenAsync("body-given");
        await Calls.Make("body-after-given");
        await WhenAsync("body-when");
        await Calls.Make("body-after-when");
    }

    /// <summary>The body has a Given, then does its real work with no When: the only When is the teardown's.</summary>
    [Test]
    public async Task L14_teardown_keyword_free_step__Given_then_calls_in_the_body()
    {
        await GivenAsync("body-given");
        await Calls.Make("body-the-real-work");
    }

    /// <summary>The body has no steps.</summary>
    [Test]
    public async Task L14_teardown_keyword_free_step__no_steps_in_the_body()
    {
        await Calls.Make("body-first");
    }

    /// <summary>The trackers have TrackDuringSetup off, and the body has a Given: does the tear-down's work go too?</summary>
    [Test]
    public async Task L14_teardown_keyword_free_step__Given_with_setup_tracking_off()
    {
        Calls = ProbeEnv.NewCalls(trackDuringSetup: false);
        await GivenAsync("body-given");
        await Calls.Make("body-the-real-work");
    }
}

public class L07_constructor_shared_fixture : LifecycleBase
{
    public L07_constructor_shared_fixture() { GivenSync("lifecycle-given"); Calls.Make("lifecycle-after-given").GetAwaiter().GetResult(); }

    /// <summary>Later calls and a When in the body. Is the boundary at the When, and what did the calls before it read?</summary>
    [Test]
    public async Task L07_constructor_shared_fixture__then_a_When()
    {
        await Calls.Make("body-first");
        await WhenAsync("body-when");
        await Calls.Make("body-after-when");
        await ThenAsync("body-then");
    }

    /// <summary>Calls alone in the body.</summary>
    [Test]
    public async Task L07_constructor_shared_fixture__plain_calls_only()
    {
        await Calls.Make("body-first");
        await Calls.Make("body-second");
    }

    /// <summary>A step that says nothing in the body: it continues the phase, as X06's does.</summary>
    [Test]
    public async Task L07_constructor_shared_fixture__a_keyword_free_step()
    {
        await Calls.Make("body-first");
        await PlainAsync("body-keyword-free");
        await Calls.Make("body-after-keyword-free");
    }

    /// <summary>The body's work is a step that says nothing, on trackers with TrackDuringSetup off: is it recorded?</summary>
    [Test]
    public async Task L07_constructor_shared_fixture__a_keyword_free_action_with_setup_tracking_off()
    {
        Calls = ProbeEnv.NewCalls(trackDuringSetup: false);
        await Calls.Make("body-first");
        await PlainAsync("body-keyword-free-action");
        await Calls.Make("body-after-keyword-free");
    }

    /// <summary>The body has a Given of its own.</summary>
    [Test]
    public async Task L07_constructor_shared_fixture__a_Given_then_a_When()
    {
        await Calls.Make("body-first");
        await GivenAsync("body-given");
        await Calls.Make("body-after-given");
        await WhenAsync("body-when");
        await Calls.Make("body-after-when");
    }
}
