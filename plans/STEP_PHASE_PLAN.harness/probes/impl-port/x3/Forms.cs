using Kronikol;
using Kronikol.Tracking;
using PhaseProbe;
using Xunit;

namespace PhaseProbe.X3;

/// <summary>The method shapes a step attribute sits on, each as the test's boundary step.</summary>
public class Forms : Steps
{
    private static bool ByRef => Environment.GetEnvironmentVariable("PROBE_BYREF") == "on";

    [WhenStep] public ValueTask WhenValueTask(string l) => new(C.Make(l));
    [WhenStep] public async ValueTask WhenAsyncValueTask(string l) => await C.Make(l);
    [WhenStep] public async ValueTask<int> WhenAsyncValueTaskT(string l) { await C.Make(l); return 1; }
    [WhenStep] public async Task<int> WhenAsyncTaskT(string l) { await C.Make(l); return 1; }
    [WhenStep] public Task WhenTaskNoAsync(string l) => C.Make(l);
    [WhenStep] public IEnumerable<int> WhenIterator(string l) { C.Make(l).GetAwaiter().GetResult(); yield return 1; C.Make(l + "-2").GetAwaiter().GetResult(); yield return 2; }
    [WhenStep] public async IAsyncEnumerable<int> WhenAsyncIterator(string l) { await C.Make(l); yield return 1; await C.Make(l + "-2"); yield return 2; }
    [WhenStep] public async void WhenAsyncVoid(string l, TaskCompletionSource done) { await C.Make(l); done.SetResult(); }
    [WhenStep] public static async Task WhenStatic(Calls c, string l) => await c.Make(l);
    [WhenStep] public async Task WhenGeneric<T>(string l, T value) => await C.Make(l);
    [WhenStep] public Task WhenThrowsSynchronously(string l) => throw new InvalidOperationException("sync");
    [WhenStep] public async Task WhenThrowsAfterAwait(string l) { await C.Make(l); throw new InvalidOperationException("after"); }
    [WhenStep] public virtual async Task WhenVirtual(string l) => await C.Make(l);
    [WhenStep] public async Task WhenRecursive(string l, int depth) { await C.Make($"{l}-{depth}"); if (depth > 0) await WhenRecursive(l, depth - 1); }
    [WhenStep] public void WhenByRef(ref int count, string l) { C.Make(l).GetAwaiter().GetResult(); count++; }
    [WhenStep] public void WhenOut(out int count, string l) { C.Make(l).GetAwaiter().GetResult(); count = 1; }
    [WhenStep] public T WhenReturnsValue<T>(T value, string l) { C.Make(l).GetAwaiter().GetResult(); return value; }

    [Fact] public async Task W01_value_task() { await GivenCall("w01-g"); await WhenValueTask("w01-w"); await ThenCall("w01-t"); }
    [Fact] public async Task W02_async_value_task() { await GivenCall("w02-g"); await WhenAsyncValueTask("w02-w"); await ThenCall("w02-t"); }
    [Fact] public async Task W03_async_value_task_t() { await GivenCall("w03-g"); await WhenAsyncValueTaskT("w03-w"); await ThenCall("w03-t"); }
    [Fact] public async Task W04_async_task_t() { await GivenCall("w04-g"); await WhenAsyncTaskT("w04-w"); await ThenCall("w04-t"); }
    [Fact] public async Task W05_task_no_async() { await GivenCall("w05-g"); await WhenTaskNoAsync("w05-w"); await ThenCall("w05-t"); }
    [Fact] public async Task W06_iterator_enumerated_later() { await GivenCall("w06-g"); var items = WhenIterator("w06-w"); await C.Make("w06-before-enumeration"); _ = items.ToList(); await ThenCall("w06-t"); }
    [Fact] public async Task W07_iterator_never_enumerated() { await GivenCall("w07-g"); _ = WhenIterator("w07-w"); await C.Make("w07-after"); await ThenCall("w07-t"); }
    [Fact] public async Task W08_async_iterator() { await GivenCall("w08-g"); await foreach (var _ in WhenAsyncIterator("w08-w")) { } await ThenCall("w08-t"); }
    [Fact] public async Task W09_async_void() { await GivenCall("w09-g"); var done = new TaskCompletionSource(); WhenAsyncVoid("w09-w", done); await done.Task; await ThenCall("w09-t"); }
    [Fact] public async Task W10_static() { await GivenCall("w10-g"); await WhenStatic(C, "w10-w"); await ThenCall("w10-t"); }
    [Fact] public async Task W11_generic() { await GivenCall("w11-g"); await WhenGeneric("w11-w", 1); await ThenCall("w11-t"); }
    [Fact] public async Task W12_throws_synchronously() { await GivenCall("w12-g"); try { await WhenThrowsSynchronously("w12-w"); } catch (InvalidOperationException) { } await C.Make("w12-after"); await ThenCall("w12-t"); }
    [Fact] public async Task W13_throws_after_await() { await GivenCall("w13-g"); try { await WhenThrowsAfterAwait("w13-w"); } catch (InvalidOperationException) { } await C.Make("w13-after"); await ThenCall("w13-t"); }
    [Fact] public async Task W14_virtual() { await GivenCall("w14-g"); await WhenVirtual("w14-w"); await ThenCall("w14-t"); }
    [Fact] public async Task W15_recursive() { await GivenCall("w15-g"); await WhenRecursive("w15-w", 2); await ThenCall("w15-t"); }
    [Fact] public async Task W16_by_ref() { if (!ByRef) return; await GivenCall("w16-g"); var n = 0; WhenByRef(ref n, "w16-w"); await ThenCall("w16-t"); }
    [Fact] public async Task W17_out() { if (!ByRef) return; await GivenCall("w17-g"); WhenOut(out var n, "w17-w"); await ThenCall("w17-t"); }
    [Fact] public async Task W18_generic_value() { await GivenCall("w18-g"); _ = WhenReturnsValue(5, "w18-w"); _ = WhenReturnsValue("x", "w18-w2"); await ThenCall("w18-t"); }
    [Fact] public async Task W19_on_a_new_thread() { await GivenCall("w19-g"); var t = new Thread(() => WhenCallSync("w19-w")); t.Start(); t.Join(); await C.Make("w19-after"); await ThenCall("w19-t"); }
    [Fact] public async Task W20_lambda_in_step_fork() { await GivenCall("w20-g"); await Task.WhenAll(Enumerable.Range(0, 3).Select(i => Task.Run(() => WhenCall($"w20-w{i}")))); await C.Make("w20-after"); await ThenCall("w20-t"); }
}
