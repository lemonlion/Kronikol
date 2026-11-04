using System.Reflection;
using System.Runtime.CompilerServices;
using Kronikol.Tracking;
using Xunit;

[assembly: TrackSteps]
[assembly: CollectionBehavior(DisableTestParallelization = true)]

// The adapter's base class gives each fact an identity, so its steps are recorded and a report can be read back.
public class Shapes : Kronikol.xUnit3.DiagrammedComponentTest
{
    private static void Say(string line) => File.AppendAllText(Path.Combine(AppContext.BaseDirectory, "said.txt"), line + "\n");

    [Fact] public async Task An_async_task_step() => await new Driver().An_async_task();
    [Fact] public async Task An_async_task_of_int_step() => Assert.Equal(7, await new Driver().An_async_task_of_int());
    [Fact] public async Task An_async_value_task_step() => await new Driver().An_async_value_task();
    [Fact] public async Task An_async_value_task_of_int_step() => Assert.Equal(8, await new Driver().An_async_value_task_of_int());
    [Fact] public async Task A_failing_async_step() => await Assert.ThrowsAsync<InvalidOperationException>(() => new Driver().Throws_after_an_await());
    [Fact] public async Task A_bypassed_async_step() => Assert.Equal(0, await new Driver().Bypassed());
    [Fact] public async Task Steps_in_a_step() => await new Driver().Has_two_sub_steps();

    // C# 15, the default for net11.0.
    [Fact] public void A_step_returns_a_union() => Assert.NotNull(new Driver().Returns_a_union());
    [Fact] public void A_step_takes_a_union() => new Driver().Takes_a_union(new Dog("Rex"));
    [Fact] public async Task A_task_of_a_union() => Assert.NotNull(await new Driver().A_task_of_a_union());
    [Fact] public void A_step_returns_a_closed_class() => Assert.IsType<Shut>(new Driver().Returns_closed());
    [Fact] public void Labeled_break_and_continue() => Assert.Equal(2, new Driver().Labeled_jumps());
    [Fact] public void An_extension_indexers_accessor() => Assert.Equal(2, new[] { 1, 2, 3 }.AsEnumerable()[1]);
    [Fact] public void A_static_interface_member() => Assert.Equal(2.5, IMeasure.Step_to_metres(250));

    // The JIT on every method in the assembly, which reaches a path no fact takes.
    [Fact]
    public void Every_method_compiles()
    {
        foreach (var type in typeof(Driver).Assembly.GetTypes())
            foreach (var method in type.GetMethods(BindingFlags.DeclaredOnly | BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic))
                if (!method.IsAbstract && !method.ContainsGenericParameters && method.GetMethodBody() != null)
                    RuntimeHelpers.PrepareMethod(method.MethodHandle);
    }

    [Fact]
    public void What_the_compiler_wrote()
    {
        var m = typeof(Driver).GetMethod(nameof(Driver.An_async_task))!;
        Say($"An_async_task: impl {m.MethodImplementationFlags}, state machine {m.GetCustomAttribute<AsyncStateMachineAttribute>() != null}, runtime {Environment.Version}");
    }

    // The phase is an AsyncLocal. A stub writes it in its caller's flow, so it stays there after the step returns, and what the test
    // does next is in that phase. Written inside a method's own async frame, it would go when that frame returns.
    [Fact]
    public void A_sync_when_step_moves_the_phase_for_its_caller()
    {
        TestPhaseContext.Current = TestPhase.Setup;
        new Driver().A_sync_when();
        Say($"after a sync [WhenStep], the caller's phase: {TestPhaseContext.Current} (Setup before it)");
    }

    [Fact]
    public async Task An_async_when_step_moves_the_phase_for_its_caller()
    {
        TestPhaseContext.Current = TestPhase.Setup;
        await new Driver().An_async_when();
        Say($"after an async [WhenStep], the caller's phase: {TestPhaseContext.Current} (Setup before it)");
    }

    [Fact]
    public async Task An_async_when_step_with_no_await_moves_the_phase_for_its_caller()
    {
        TestPhaseContext.Current = TestPhase.Setup;
        await new Driver().An_async_when_with_no_await();
        Say($"after an async [WhenStep] that never suspends, the caller's phase: {TestPhaseContext.Current} (Setup before it)");
    }

    [Fact]
    public async Task Inside_an_async_when_step_the_phase_is_moved()
    {
        TestPhaseContext.Current = TestPhase.Setup;
        var inside = await new Driver().Reads_the_phase_inside();
        Say($"inside an async [WhenStep], after its await: {inside} (Setup before it)");
    }
}

public record class Cat(string Name);
public record class Dog(string Name);
public union Pet(Cat, Dog);

public closed record class Gate;
public record class Shut : Gate;

public interface IMeasure
{
    [Step] public static double Step_to_metres(double centimetres) => centimetres / 100;
}

public static class SequenceIndexer
{
    extension(IEnumerable<int> sequence)
    {
        public int this[int index]
        {
            [Step] get => sequence.ElementAt(index);
        }
    }
}

public class Driver
{
    public bool Off => true;

    [Step] public async Task An_async_task() { await Task.Delay(30); Inner(); }
    [Step] public async Task<int> An_async_task_of_int() { await Task.Delay(30); Inner(); return 7; }
    [Step] public async ValueTask An_async_value_task() { await Task.Delay(30); Inner(); }
    [Step] public async ValueTask<int> An_async_value_task_of_int() { await Task.Delay(30); Inner(); return 8; }
    [Step] public async Task Throws_after_an_await() { await Task.Delay(30); throw new InvalidOperationException("boom"); }
    [Step(SkipIf = nameof(Off))] public async Task<int> Bypassed() { await Task.Delay(30); throw new Exception("ran"); }
    [Step] public async Task Has_two_sub_steps() { await An_async_task(); await An_async_value_task(); }
    [Step] public void Inner() { }

    [WhenStep] public void A_sync_when() { }
    [WhenStep] public async Task An_async_when() { await Task.Delay(30); }
    [WhenStep] public async Task An_async_when_with_no_await() { }
    [WhenStep] public async Task<TestPhase> Reads_the_phase_inside() { await Task.Delay(30); return TestPhaseContext.Current; }

    [Step] public Pet Returns_a_union() => new Dog("Rex");
    [Step] public void Takes_a_union(Pet pet) { }
    [Step] public async Task<Pet> A_task_of_a_union() { await Task.Yield(); return new Cat("Tom"); }
    [Step] public Gate Returns_closed() => new Shut();

    [Step]
    public int Labeled_jumps()
    {
        var n = 0;
        outer: for (var i = 0; i < 3; i++)
        {
            for (var j = 0; j < 3; j++)
            {
                if (j == 1) continue outer;
                if (i == 2) break outer;
                n++;
            }
        }
        return n;
    }
}
