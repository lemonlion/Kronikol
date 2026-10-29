using Kronikol.AssertionTracking;
using Mono.Cecil;
using Mono.Cecil.Cil;
using static Kronikol.Tests.AssertionTracking.TestAssemblyBuilder;

namespace Kronikol.Tests.AssertionTracking;

/// <summary>
/// #211 (AWAITED_ASSERTION_VALUES_PLAN 4.1): an awaited assertion's values are read after its await, so a value read
/// through one of MoveNext's own locals is carried across the await in a field of the state machine. The shape catalogue
/// (<c>Fixtures/AwaitedShapes</c>, R211 and S27 to S50) shows that every build then draws Debug's notes and ends as it
/// does unwoven. These facts show the rest: what the weave does when a value's read fails, when it cannot carry a value,
/// and that a carried value is let go after its statement.
/// </summary>
public class AwaitedAssertionResumeTests
{
    private const string Types = """

        public sealed class OrderRequest
        {
            public string Id { get; init; } = "";
        }

        public sealed class OrdersClient
        {
            public async Task<string> GetOrderAsync(OrderRequest request)
            {
                await Task.Yield();
                throw new InvalidOperationException("not found");
            }
        }
        """;

    // As S38: the closure is in one of MoveNext's locals in both Release builds, so the weave carries it.
    private const string ClosureSource = """
        #nullable enable
        using System;
        using System.Threading.Tasks;
        using FluentAssertions;
        using Kronikol.Tracking;

        [assembly: TrackAssertions]

        public class Probe
        {
            private readonly OrdersClient _client = new();

            public async Task Run()
            {
                var request = new OrderRequest { Id = "not found" };
                Func<Task> act = () => _client.GetOrderAsync(request);
                await act.Should().ThrowAsync<InvalidOperationException>().WithMessage(request.Id);
            }
        }
        """ + Types;

    /// <summary>
    /// A value whose read throws leaves the note without values, and the assertion's verdict is its own (4.1 d). The
    /// read is made to fail by storing null where the weave carries the closure: before #211, a read that failed there
    /// was recorded as the assertion's failure and thrown in its place.
    /// </summary>
    [Theory]
    [InlineData(BuildConfiguration.Release)]
    [InlineData(BuildConfiguration.ReleaseDebugPlus)]
    public void A_value_whose_read_throws_leaves_the_note_without_values(BuildConfiguration configuration)
    {
        var path = Build($"Resume_ReadThrows_{configuration}", ClosureSource, configuration);
        WovenIl.Weave(new AssertionWeaver(), path);
        using (var assembly = AssemblyDefinition.ReadAssembly(path, new ReaderParameters { ReadWrite = true, ReadSymbols = true }))
        {
            var store = Instructions(assembly).Single(i => i.OpCode == OpCodes.Stfld && IsCarrier(i.Operand));
            store.Previous.OpCode = OpCodes.Ldnull;
            store.Previous.Operand = null;
            assembly.Write(new WriterParameters { WriteSymbols = true });
        }

        var (thrown, notes) = WovenRun.Run(path, "Probe", "Run");

        Assert.Null(thrown);
        Assert.Equal(["✓ Act should throw async ~<InvalidOperationException> with message request.Id"], notes.Select(Text));
    }

    private const string LetGoSource = """
        #nullable enable
        using System;
        using System.Threading.Tasks;
        using Kronikol.Tracking;
        using Shouldly;

        [assembly: TrackAssertions]

        public class Probe
        {
            private readonly OrdersClient _client = new();

            public static bool Alive { get; private set; }

            public async Task Run()
            {
                var request = new OrderRequest { Id = "missing" };
                var weak = new WeakReference(request);
                await Should.ThrowAsync<InvalidOperationException>(() => _client.GetOrderAsync(request));
                await Task.Yield();
                GC.Collect();
                GC.WaitForPendingFinalizers();
                GC.Collect();
                Alive = weak.IsAlive;
            }
        }
        """ + Types;

    /// <summary>
    /// The field that carried a closure across the await is cleared once the values are read, so the state machine does
    /// not keep what the closure holds alive for the rest of the method (4.1 c). Release builds only: Debug keeps the
    /// closure in a field of its own for the whole method.
    /// </summary>
    [Theory]
    [InlineData(BuildConfiguration.Release)]
    [InlineData(BuildConfiguration.ReleaseDebugPlus)]
    public void A_carried_closure_is_let_go_after_its_statement(BuildConfiguration configuration)
    {
        var path = Build($"Resume_LetGo_{configuration}", LetGoSource, configuration);
        WovenIl.Weave(new AssertionWeaver(), path);
        using (var assembly = AssemblyDefinition.ReadAssembly(path))
            Assert.Contains(Instructions(assembly), i => i.OpCode == OpCodes.Stfld && IsCarrier(i.Operand));

        var (thrown, notes) = WovenRun.Run(path, "Probe", "Run");

        Assert.Null(thrown);
        Assert.Equal(["✓ Should throw InvalidOperationException"], notes.Select(Text));
        var alive = AppDomain.CurrentDomain.GetAssemblies().Single(a => a.Location == path)
            .GetType("Probe")!.GetProperty("Alive")!.GetValue(null);
        Assert.Equal(false, alive);
    }

    // As S37: a plain local, read after the await.
    private const string LocalSource = """
        #nullable enable
        using System;
        using System.Threading.Tasks;
        using FluentAssertions;
        using Kronikol.Tracking;

        [assembly: TrackAssertions]

        public class Probe
        {
            private readonly OrdersClient _client = new();

            public async Task Run()
            {
                var message = "not found";
                Func<Task> act = () => _client.GetOrderAsync(new OrderRequest { Id = "missing" });
                await act.Should().ThrowAsync<InvalidOperationException>().WithMessage(message);
            }
        }
        """ + Types;

    /// <summary>
    /// Where the await is not the compiler's shape, a GetAwaiter() call and a store of the awaiter, the weave has nowhere
    /// to carry a value from, and leaves out every value it would read through a local of MoveNext, rather than read
    /// the local after the await, where a build that keeps it there reads zero (4.1 e). Debug, whose values are in
    /// fields, leaves out the same values, so every build draws the same note. The shape is made by a nop after the
    /// assertion's GetAwaiter() call.
    /// </summary>
    [Theory]
    [InlineData(BuildConfiguration.Debug)]
    [InlineData(BuildConfiguration.Release)]
    [InlineData(BuildConfiguration.ReleaseDebugPlus)]
    public void An_await_the_weave_cannot_carry_a_value_across_draws_no_value(BuildConfiguration configuration)
    {
        var path = Build($"Resume_NoCarry_{configuration}", LocalSource, configuration);
        using (var assembly = AssemblyDefinition.ReadAssembly(path, new ReaderParameters { ReadWrite = true, ReadSymbols = true }))
        {
            var moveNext = assembly.MainModule.GetTypes().Single(t => t.Name.StartsWith("<Run>d__", StringComparison.Ordinal))
                .Methods.Single(m => m.Name == "MoveNext");
            var getAwaiter = moveNext.Body.Instructions.Last(i => i.Operand is MethodReference { Name: "GetAwaiter" });
            var il = moveNext.Body.GetILProcessor();
            il.InsertAfter(getAwaiter, il.Create(OpCodes.Nop));
            assembly.Write(new WriterParameters { WriteSymbols = true });
        }

        WovenIl.Weave(new AssertionWeaver(), path);

        var (thrown, notes) = WovenRun.Run(path, "Probe", "Run");
        Assert.Null(thrown);
        Assert.Equal(["✓ Act should throw async ~<InvalidOperationException> with message message"], notes.Select(Text));
        using (var woven = AssemblyDefinition.ReadAssembly(path))
            Assert.DoesNotContain(Instructions(woven), i => i.OpCode == OpCodes.Stfld && IsCarrier(i.Operand));
    }

    private static IEnumerable<Instruction> Instructions(AssemblyDefinition assembly) =>
        assembly.MainModule.GetTypes().SelectMany(t => t.Methods).Where(m => m.HasBody).SelectMany(m => m.Body.Instructions);

    private static bool IsCarrier(object operand) =>
        operand is FieldReference field && field.Name.StartsWith("<>krk__carried", StringComparison.Ordinal);

    private static string Text(WovenRun.Note note) => (note.Passed ? "✓ " : "✗ ") + note.Label;
}
