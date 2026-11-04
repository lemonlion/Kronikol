// dotnet run --file restore-at-complete.cs
//
// Could the phase be set back when a sub-step ends, rather than not moved when it starts? This is the woven shape with no
// Kronikol in it: the step's stub runs in the caller's flow, its state machine in a copy, and the completion in an async
// method of its own (StepCollector.CompleteStepAsync). Each variant writes "Setup" as the step starts and tries to put the
// caller's value back. An AsyncLocal write made inside an async method does not reach its caller, so a write when the step
// completes never gets back to the flow that needs it; only one made in the stub, in the caller's flow, does.

using System.Runtime.CompilerServices;

Current.Value = "Action";
await new Variants().Run();

class Variants
{
    public async Task Run()
    {
        Print("before any step");

        Sync_restore_in_finally();
        Print("after a sync step that restores in a finally (works)");

        await Async_restore_at_completion();
        Print("after an async step that restores when it completes, as CompleteStep would (does NOT reach the caller)");

        Current.Value = "Action";
        await Async_no_restore();
        Print("after an async step that restores nothing, today's shape");

        Current.Value = "Action";
        await Async_restore_in_stub();
        Print("after an async step whose stub restores before it returns (reaches the caller; CompleteStepAsync's first line runs there, so no weaver change)");
    }

    private static void Print(string where) => Console.WriteLine($"{Current.Value,-7} {where}");

    private void Sync_restore_in_finally()
    {
        var saved = Current.Value;
        Current.Value = "Setup";
        try { }
        finally { Current.Value = saved; }
    }

    // The woven async shape: stub writes, body runs, completion is its own async method.
    private Task Async_restore_at_completion()
    {
        var saved = Current.Value;
        Current.Value = "Setup";
        return CompleteAndRestore(Body(), saved);
    }

    private static async Task CompleteAndRestore(Task body, string? saved)
    {
        await body.ConfigureAwait(false);
        Current.Value = saved;
    }

    private Task Async_no_restore()
    {
        Current.Value = "Setup";
        return Complete(Body());
    }

    private static async Task Complete(Task body) => await body.ConfigureAwait(false);

    private Task Async_restore_in_stub()
    {
        var saved = Current.Value;
        Current.Value = "Setup";
        var task = Complete(Body());
        Current.Value = saved;
        return task;
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static async Task Body()
    {
        await Task.Yield();
        if (Current.Value != "Setup")
            throw new InvalidOperationException("the body should see the step's own phase");
    }
}

static class Current
{
    private static readonly AsyncLocal<string?> Local = new();
    public static string? Value { get => Local.Value; set => Local.Value = value; }
}
