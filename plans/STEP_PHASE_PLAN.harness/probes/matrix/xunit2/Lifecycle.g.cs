// Stamped by templates/stamp_lifecycle.py: edit that.
using Kronikol;
using Kronikol.Tracking;
using Kronikol.xUnit2;
using PhaseProbe;
using Xunit;

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

public class L01_InitializeAsync_async : LifecycleBase, IAsyncLifetime
{
    public async Task InitializeAsync() { await GivenAsync("lifecycle-given"); await Calls.Make("lifecycle-after-given"); }
    public Task DisposeAsync() => Task.CompletedTask;

    /// <summary>Later calls and a When in the body. Is the boundary at the When, and what did the calls before it read?</summary>
    [Fact]
    public async Task L01_InitializeAsync_async__then_a_When()
    {
        await Calls.Make("body-first");
        await WhenAsync("body-when");
        await Calls.Make("body-after-when");
        await ThenAsync("body-then");
    }

    /// <summary>Calls alone in the body.</summary>
    [Fact]
    public async Task L01_InitializeAsync_async__plain_calls_only()
    {
        await Calls.Make("body-first");
        await Calls.Make("body-second");
    }

    /// <summary>A step that says nothing in the body: it continues the phase, as X06's does.</summary>
    [Fact]
    public async Task L01_InitializeAsync_async__a_keyword_free_step()
    {
        await Calls.Make("body-first");
        await PlainAsync("body-keyword-free");
        await Calls.Make("body-after-keyword-free");
    }

    /// <summary>The body's work is a step that says nothing, on trackers with TrackDuringSetup off: is it recorded?</summary>
    [Fact]
    public async Task L01_InitializeAsync_async__a_keyword_free_action_with_setup_tracking_off()
    {
        Calls = ProbeEnv.NewCalls(trackDuringSetup: false);
        await Calls.Make("body-first");
        await PlainAsync("body-keyword-free-action");
        await Calls.Make("body-after-keyword-free");
    }

    /// <summary>The body has a Given of its own.</summary>
    [Fact]
    public async Task L01_InitializeAsync_async__a_Given_then_a_When()
    {
        await Calls.Make("body-first");
        await GivenAsync("body-given");
        await Calls.Make("body-after-given");
        await WhenAsync("body-when");
        await Calls.Make("body-after-when");
    }
}

public class L02_InitializeAsync_returns_the_Task : LifecycleBase, IAsyncLifetime
{
    public Task InitializeAsync() => GivenTask("lifecycle-given");
    public Task DisposeAsync() => Task.CompletedTask;

    /// <summary>Later calls and a When in the body. Is the boundary at the When, and what did the calls before it read?</summary>
    [Fact]
    public async Task L02_InitializeAsync_returns_the_Task__then_a_When()
    {
        await Calls.Make("body-first");
        await WhenAsync("body-when");
        await Calls.Make("body-after-when");
        await ThenAsync("body-then");
    }

    /// <summary>Calls alone in the body.</summary>
    [Fact]
    public async Task L02_InitializeAsync_returns_the_Task__plain_calls_only()
    {
        await Calls.Make("body-first");
        await Calls.Make("body-second");
    }

    /// <summary>A step that says nothing in the body: it continues the phase, as X06's does.</summary>
    [Fact]
    public async Task L02_InitializeAsync_returns_the_Task__a_keyword_free_step()
    {
        await Calls.Make("body-first");
        await PlainAsync("body-keyword-free");
        await Calls.Make("body-after-keyword-free");
    }

    /// <summary>The body's work is a step that says nothing, on trackers with TrackDuringSetup off: is it recorded?</summary>
    [Fact]
    public async Task L02_InitializeAsync_returns_the_Task__a_keyword_free_action_with_setup_tracking_off()
    {
        Calls = ProbeEnv.NewCalls(trackDuringSetup: false);
        await Calls.Make("body-first");
        await PlainAsync("body-keyword-free-action");
        await Calls.Make("body-after-keyword-free");
    }

    /// <summary>The body has a Given of its own.</summary>
    [Fact]
    public async Task L02_InitializeAsync_returns_the_Task__a_Given_then_a_When()
    {
        await Calls.Make("body-first");
        await GivenAsync("body-given");
        await Calls.Make("body-after-given");
        await WhenAsync("body-when");
        await Calls.Make("body-after-when");
    }
}

public class L04_constructor : LifecycleBase, IAsyncLifetime
{
    public L04_constructor() { GivenSync("lifecycle-given"); Calls.Make("lifecycle-after-given").GetAwaiter().GetResult(); }
    public Task InitializeAsync() => Task.CompletedTask;
    public Task DisposeAsync() => Task.CompletedTask;

    /// <summary>Later calls and a When in the body. Is the boundary at the When, and what did the calls before it read?</summary>
    [Fact]
    public async Task L04_constructor__then_a_When()
    {
        await Calls.Make("body-first");
        await WhenAsync("body-when");
        await Calls.Make("body-after-when");
        await ThenAsync("body-then");
    }

    /// <summary>Calls alone in the body.</summary>
    [Fact]
    public async Task L04_constructor__plain_calls_only()
    {
        await Calls.Make("body-first");
        await Calls.Make("body-second");
    }

    /// <summary>A step that says nothing in the body: it continues the phase, as X06's does.</summary>
    [Fact]
    public async Task L04_constructor__a_keyword_free_step()
    {
        await Calls.Make("body-first");
        await PlainAsync("body-keyword-free");
        await Calls.Make("body-after-keyword-free");
    }

    /// <summary>The body's work is a step that says nothing, on trackers with TrackDuringSetup off: is it recorded?</summary>
    [Fact]
    public async Task L04_constructor__a_keyword_free_action_with_setup_tracking_off()
    {
        Calls = ProbeEnv.NewCalls(trackDuringSetup: false);
        await Calls.Make("body-first");
        await PlainAsync("body-keyword-free-action");
        await Calls.Make("body-after-keyword-free");
    }

    /// <summary>The body has a Given of its own.</summary>
    [Fact]
    public async Task L04_constructor__a_Given_then_a_When()
    {
        await Calls.Make("body-first");
        await GivenAsync("body-given");
        await Calls.Make("body-after-given");
        await WhenAsync("body-when");
        await Calls.Make("body-after-when");
    }
}

public class L11_DisposeAsync_async : LifecycleBase, IAsyncLifetime
{
    public Task InitializeAsync() => Task.CompletedTask;
    public async Task DisposeAsync() { await WhenAsync("teardown-when"); await Calls.Make("teardown-after-when"); }

    /// <summary>The body has its Given and its When: the teardown's When comes after the boundary.</summary>
    [Fact]
    public async Task L11_DisposeAsync_async__Given_and_When_in_the_body()
    {
        await GivenAsync("body-given");
        await Calls.Make("body-after-given");
        await WhenAsync("body-when");
        await Calls.Make("body-after-when");
    }

    /// <summary>The body has a Given, then does its real work with no When: the only When is the teardown's.</summary>
    [Fact]
    public async Task L11_DisposeAsync_async__Given_then_calls_in_the_body()
    {
        await GivenAsync("body-given");
        await Calls.Make("body-the-real-work");
    }

    /// <summary>The body has no steps.</summary>
    [Fact]
    public async Task L11_DisposeAsync_async__no_steps_in_the_body()
    {
        await Calls.Make("body-first");
    }
}

public class L12_Dispose_sync : LifecycleBase
{
    protected override void Dispose(bool disposing) { WhenSync("teardown-when"); Calls.Make("teardown-after-when").GetAwaiter().GetResult(); }

    /// <summary>The body has its Given and its When: the teardown's When comes after the boundary.</summary>
    [Fact]
    public async Task L12_Dispose_sync__Given_and_When_in_the_body()
    {
        await GivenAsync("body-given");
        await Calls.Make("body-after-given");
        await WhenAsync("body-when");
        await Calls.Make("body-after-when");
    }

    /// <summary>The body has a Given, then does its real work with no When: the only When is the teardown's.</summary>
    [Fact]
    public async Task L12_Dispose_sync__Given_then_calls_in_the_body()
    {
        await GivenAsync("body-given");
        await Calls.Make("body-the-real-work");
    }

    /// <summary>The body has no steps.</summary>
    [Fact]
    public async Task L12_Dispose_sync__no_steps_in_the_body()
    {
        await Calls.Make("body-first");
    }
}
