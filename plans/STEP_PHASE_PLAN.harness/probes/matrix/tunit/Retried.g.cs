// Stamped by templates/stamp_retry.py: edit that.
using System.Collections.Concurrent;
using System.Runtime.CompilerServices;
using Kronikol;
using Kronikol.Tracking;
using Kronikol.TUnit;
using PhaseProbe;
using TUnit.Core;

namespace PhaseProbe.Suite;

public class Retried : DiagrammedComponentTest
{
    private static readonly ConcurrentDictionary<string, int> Attempts = new();
    private static readonly AsyncLocal<string?> Stage = new();

    // The handler a fixture or a static keeps, and a handler with a step type fetcher as a consumer can set: today's implicit boundary is the only one a handler can draw.
    private static readonly Calls SharedCalls = ProbeEnv.NewCalls();
    private static readonly Calls SharedCallsWithFetcher = ProbeEnv.NewCalls(() => Stage.Value);

    private Calls _calls = null!;

    private static int Attempt([CallerMemberName] string test = "") => Attempts.AddOrUpdate(test, 1, (_, n) => n + 1);
    private bool _handRolled;
    private void Fail(string message) { if (_handRolled) throw new InvalidOperationException(message); throw new InvalidOperationException(message); }

    [GivenStep] public async Task GivenAsync(string label) => await _calls.Make(label);
    [GivenStep] public void GivenSync(string label) => _calls.Make(label).GetAwaiter().GetResult();
    [WhenStep] public async Task WhenAsync(string label) => await _calls.Make(label);
    [WhenStep] public void WhenSync(string label) => _calls.Make(label).GetAwaiter().GetResult();
    [ThenStep] public async Task ThenAsync(string label) => await _calls.Make(label);
    [ThenStep] public void ThenSync(string label) => _calls.Make(label).GetAwaiter().GetResult();
    [Step] public async Task PlainAsync(string label) => await _calls.Make(label);
    [WhenStep] public async Task WhenFailsOnAttemptOne(string label, int attempt) { await _calls.Make(label); if (attempt == 1) Fail("the first attempt fails in its When"); }
    [GivenStep] public async Task GivenOpen(string label, TaskCompletionSource gate) { await _calls.Make(label); await gate.Task; }

    /// <summary>A Given and a When per attempt, a call before either and after each. The first attempt fails after its When.</summary>
    private async Task Attempting(int n)
    {
        await _calls.Make($"a{n}-first");
        Stage.Value = "Given";
        await GivenAsync($"a{n}-given");
        await _calls.Make($"a{n}-after-given");
        Stage.Value = "When";
        await WhenAsync($"a{n}-when");
        await _calls.Make($"a{n}-after-when");
        if (n == 1) Fail("the first attempt fails");
        Stage.Value = "Then";
        await ThenAsync($"a{n}-then");
        await _calls.Make($"a{n}-after-then");
    }

    private void AttemptingSync(int n)
    {
        _calls.Make($"a{n}-first").GetAwaiter().GetResult();
        GivenSync($"a{n}-given");
        _calls.Make($"a{n}-after-given").GetAwaiter().GetResult();
        WhenSync($"a{n}-when");
        _calls.Make($"a{n}-after-when").GetAwaiter().GetResult();
        if (n == 1) Fail("the first attempt fails");
        ThenSync($"a{n}-then");
        _calls.Make($"a{n}-after-then").GetAwaiter().GetResult();
    }

    [Test, Retry(2)]
    public async Task R01_handler_built_per_attempt()
    {
        _calls = ProbeEnv.NewCalls();
        await Attempting(Attempt());
    }

    [Test, Retry(2)]
    public async Task R02_shared_handler()
    {
        _calls = SharedCalls;
        await Attempting(Attempt());
    }

    [Test, Retry(2)]
    public void R03_sync_test_and_steps_handler_built_per_attempt()
    {
        _calls = ProbeEnv.NewCalls();
        AttemptingSync(Attempt());
    }

    [Test, Retry(2)]
    public async Task R04_the_first_attempt_fails_inside_its_When()
    {
        var n = Attempt();
        _calls = ProbeEnv.NewCalls();
        await GivenAsync($"a{n}-given");
        await WhenFailsOnAttemptOne($"a{n}-when", n);
        await _calls.Make($"a{n}-after-when");
        await ThenAsync($"a{n}-then");
    }

    private static readonly TaskCompletionSource StaysOpen = new();

    [Test, Retry(2)]
    public async Task R05_the_first_attempt_fails_with_a_Given_left_running()
    {
        var n = Attempt();
        _calls = ProbeEnv.NewCalls();
        if (n == 1)
        {
            _ = GivenOpen("a1-given-left-running", StaysOpen);
            await _calls.Make("a1-while-running");
            Fail("the first attempt fails with its Given running");
        }
        await _calls.Make($"a{n}-first");
        await GivenAsync($"a{n}-given");
        await _calls.Make($"a{n}-after-given");
        await WhenAsync($"a{n}-when");
        await _calls.Make($"a{n}-after-when");
        StaysOpen.TrySetResult();
    }

    [Test, Retry(2)]
    public async Task R06_handler_per_attempt_with_a_step_type_fetcher()
    {
        _calls = ProbeEnv.NewCalls(() => Stage.Value);
        await Attempting(Attempt());
    }

    [Test, Retry(2)]
    public async Task R07_shared_handler_with_a_step_type_fetcher()
    {
        _calls = SharedCallsWithFetcher;
        await Attempting(Attempt());
    }

    [Test, Retry(2)]
    public async Task R08_a_keyword_free_step_first_in_each_attempt()
    {
        var n = Attempt();
        _calls = ProbeEnv.NewCalls();
        await PlainAsync($"a{n}-keyword-free-first");
        await _calls.Make($"a{n}-after-keyword-free");
        await GivenAsync($"a{n}-given");
        await WhenAsync($"a{n}-when");
        await _calls.Make($"a{n}-after-when");
        if (n == 1) Fail("the first attempt fails");
    }

    [Test, Retry(2)]
    public async Task R09_a_keyword_free_action_with_setup_tracking_off_after_a_first_attempt()
    {
        var n = Attempt();
        _calls = ProbeEnv.NewCalls(trackDuringSetup: false);
        await GivenAsync($"a{n}-given");
        await PlainAsync($"a{n}-keyword-free-action");
        await _calls.Make($"a{n}-after-keyword-free");
        if (n == 1) Fail("the first attempt fails");
    }

    [Test]
    public async Task R20_hand_rolled_retry_handler_per_attempt()
    {
        _handRolled = true;
        for (var n = 1; ; n++)
        {
            _calls = ProbeEnv.NewCalls();
            try { await Attempting(n); return; }
            catch (Exception) when (n == 1) { }
        }
    }

    [Test]
    public async Task R21_hand_rolled_retry_shared_handler()
    {
        _handRolled = true;
        _calls = ProbeEnv.NewCalls();
        for (var n = 1; ; n++)
        {
            try { await Attempting(n); return; }
            catch (Exception) when (n == 1) { }
        }
    }

    [Test]
    public void R22_hand_rolled_retry_sync_steps()
    {
        _handRolled = true;
        _calls = ProbeEnv.NewCalls();
        for (var n = 1; ; n++)
        {
            try { AttemptingSync(n); return; }
            catch (Exception) when (n == 1) { }
        }
    }

    [Test]
    public async Task R23_hand_rolled_retry_with_a_step_type_fetcher_handler_per_attempt()
    {
        _handRolled = true;
        for (var n = 1; ; n++)
        {
            _calls = ProbeEnv.NewCalls(() => Stage.Value);
            Stage.Value = null;
            try { await Attempting(n); return; }
            catch (Exception) when (n == 1) { }
        }
    }
}

public class RetriedWithLifecycle : DiagrammedComponentTest
{
    private static int _attempts;
    private Calls _calls = null!;
    private int _n;

    [GivenStep] public async Task GivenAsync(string label) => await _calls.Make(label);
    [WhenStep] public async Task WhenAsync(string label) => await _calls.Make(label);

    private async Task Starting()
    {
        _n = Interlocked.Increment(ref _attempts);
        _calls = ProbeEnv.NewCalls();
        await GivenAsync($"a{_n}-lifecycle-given");
    }

    [Before(Test)] public async Task Init() { await Starting(); }

    private static void Fail(string message) { throw new InvalidOperationException(message); }

    [Test, Retry(2)]
    public async Task R30_a_Given_in_a_lifecycle_method_of_each_attempt()
    {
        await _calls.Make($"a{_n}-first");
        await WhenAsync($"a{_n}-when");
        await _calls.Make($"a{_n}-after-when");
        if (_n == 1) Fail("the first attempt fails");
    }
}
