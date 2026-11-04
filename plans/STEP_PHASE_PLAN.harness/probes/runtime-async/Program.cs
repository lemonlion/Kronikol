using System.Reflection;

static class P
{
    static readonly AsyncLocal<string?> L = new();

    static async Task ChildSync() { L.Value = "written in child, no await"; await Task.CompletedTask; }
    static async Task ChildYield() { L.Value = "written in child, before await"; await Task.Yield(); L.Value = "written in child, after await"; }

    static async Task Main()
    {
        var m = typeof(P).GetMethod(nameof(ChildYield), BindingFlags.NonPublic | BindingFlags.Static)!;
        Console.WriteLine($"ChildYield flags: {m.MethodImplementationFlags}; state machine attribute: {m.GetCustomAttributes().Any(a => a.GetType().Name == "AsyncStateMachineAttribute")}");

        L.Value = "caller";
        await ChildSync();
        Console.WriteLine($"after awaiting a child that never yields: {L.Value}");

        L.Value = "caller";
        var t = ChildYield();
        Console.WriteLine($"after a yielding child returned its task: {L.Value}");
        await t;
        Console.WriteLine($"after awaiting it: {L.Value}");

        L.Value = "caller";
        var first = ChildYield();
        var second = ChildSync();
        Console.WriteLine($"after two children started: {L.Value}");
        await Task.WhenAll(first, second);
        Console.WriteLine($"after WhenAll: {L.Value}");
    }
}
