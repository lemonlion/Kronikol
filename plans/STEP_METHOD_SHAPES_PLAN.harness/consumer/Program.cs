using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;
using Kronikol.Tracking;
using Shouldly;

[assembly: TrackSteps]
[assembly: TrackAssertions]

// The issue's two methods as a real build weaves them, then shapes the plan adds. A shape that takes the process with it
// runs alone: `consumer ref`.
Console.WriteLine($"runtime {Environment.Version}, assembly {typeof(Program).Assembly.GetName().Name}, {(args.Length > 0 ? args[0] : "all")}");
Console.WriteLine("references: " + string.Join(", ", References()));

if (args.Length > 0 && args[0] == "ref")
{
    await Run("in parameter", async d => d.Takes_an_in_parameter(5));
    await Run("ref parameter", async d => { var v = 1; d.Takes_a_ref_parameter(ref v); Console.WriteLine("  value " + v); });
    await Run("out parameter", async d => { d.Takes_an_out_parameter(out var v); Console.WriteLine("  value " + v); });
    return;
}

await Run("void", async d => d.Void());
await Run("async Task", async d => await d.Task());
await Run("int (issue)", async d => Console.WriteLine("  value " + d.A_number_is_chosen()));
await Run("async ValueTask (issue)", async d => await d.A_value_task_step());
await Run("async ValueTask<int>", async d => Console.WriteLine("  value " + await d.A_value_task_int()));
await Run("value + assertion", async d => Console.WriteLine("  value " + d.Value_with_assertion()));
await Run("Task with a synchronous throw", async d => await d.Throws_before_its_task());
await Run("iterator", async d => Console.WriteLine("  value " + d.An_iterator().Sum()));
await Run("async void", async d => { d.An_async_void(); await ShopDriver.Done.Task; });
await Run("a value step that throws, for its line number", async d => d.A_value_that_throws());

static async Task Run(string name, Func<ShopDriver, Task> body)
{
    var id = Guid.NewGuid().ToString("N");
    using var scope = TestIdentityScope.Begin(id, id);
    Console.WriteLine(name);
    try
    {
        await body(new ShopDriver());
    }
    catch (Exception ex)
    {
        Console.WriteLine($"  threw {ex.GetType().Name}: {ex.Message}");
        var frame = ex.StackTrace?.Split('\n').FirstOrDefault(l => l.Contains("ShopDriver"))?.Trim();
        if (frame != null)
            Console.WriteLine($"  {frame}");
    }
    foreach (var step in StepCollector.GetSteps(id))
        Console.WriteLine($"  step '{step.Text}' {step.Status} {step.Duration?.TotalMilliseconds:F0} ms, under it: {string.Join(", ", (step.SubSteps ?? []).Select(s => s.IsAssertion ? "(an assertion)" : s.Text))}");
    if (StepCollector.HasActiveStep(id))
        Console.WriteLine("  a step was left open");
}

static IEnumerable<string> References()
{
    using var pe = new PEReader(File.OpenRead(typeof(Program).Assembly.Location));
    var md = pe.GetMetadataReader();
    foreach (var handle in md.AssemblyReferences)
    {
        var r = md.GetAssemblyReference(handle);
        yield return $"{md.GetString(r.Name)} {r.Version}";
    }
}

public class ShopDriver
{
    public static readonly TaskCompletionSource Done = new();

    [Step] public void Void() { Thread.Sleep(20); Inner(); }
    [Step] public async Task Task() { await System.Threading.Tasks.Task.Delay(20); Inner(); }
    [Step] public int A_number_is_chosen() => 42;
    [Step] public async ValueTask A_value_task_step() { await System.Threading.Tasks.Task.Delay(100); Inner(); }
    [Step] public async ValueTask<int> A_value_task_int() { await System.Threading.Tasks.Task.Delay(100); Inner(); return 7; }
    [Step] public int Value_with_assertion() { var k = 1; k.ShouldBe(1); return 3; }
    [Step] public Task Throws_before_its_task() => throw new InvalidOperationException("thrown before any task");
    [Step] public IEnumerable<int> An_iterator() { Inner(); yield return 1; yield return 2; }
    [Step] public async void An_async_void() { await System.Threading.Tasks.Task.Delay(20); Inner(); Done.SetResult(); }
    [Step] public int A_value_that_throws() { throw new InvalidOperationException("from a value step"); }
    [Step] public void Takes_an_in_parameter(in int x) { Inner(); }
    [Step] public void Takes_a_ref_parameter(ref int x) { Inner(); x++; }
    [Step] public void Takes_an_out_parameter(out int x) { Inner(); x = 7; }
    [Step] public void Inner() { }
}
