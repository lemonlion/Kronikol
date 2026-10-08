using Kronikol.AssertionTracking;
using Microsoft.CodeAnalysis;
using Mono.Cecil;
using Mono.Cecil.Cil;

namespace Kronikol.Tests.AssertionTracking;

/// <summary>
/// The stack analysis (SHOULDLY_ASSERTIONS_PLAN section 3.2): one worklist pass per method that names each value on the
/// evaluation stack and the instruction that pushed it, in place of the three heuristics that read depth line by line.
/// A Release build keeps values on the stack across statements (F21, the Shouldly prototype's 9 of 11), and a weave
/// that cannot account for them writes IL the runtime rejects.
/// </summary>
public class StackAnalysisTests
{
    private const string Source = """
        using System;
        using System.Collections.Generic;

        public static class Shapes
        {
            public static int[] Array() => new[] { 1, 2 };

            public static string First(List<string> items)
            {
                var first = items[0];
                return first;
            }

            public static int NullJoin(bool flag) => Length(flag ? null : "a");

            private static int Length(string? text) => text?.Length ?? 0;

            public static string Caught()
            {
                try
                {
                    throw new InvalidOperationException("x");
                }
                catch (InvalidOperationException e)
                {
                    return e.Message;
                }
            }
        }
        """;

    private static MethodDefinition Method(string name)
    {
        var path = TestAssemblyBuilder.Build("StackShapes", Source, OptimizationLevel.Release);
        var module = ModuleDefinition.ReadModule(path, new ReaderParameters { InMemory = true });
        return module.GetType("Shapes").Methods.Single(m => m.Name == name);
    }

    private static string[] Types(StackAnalysis.Slot[]? slots) =>
        (slots ?? throw new InvalidOperationException("no stack")).Select(s => StackAnalysis.IsNull(s) ? "null" : s.Type!.FullName).ToArray();

    [Fact]
    public void The_stack_analysis_types_each_slot()
    {
        // newarr and dup: an array initialiser stores into a copy of the array it keeps on the stack.
        var array = Method("Array");
        var analysis = StackAnalysis.Analyze(array.Body);
        var store = array.Body.Instructions.First(i => i.OpCode == OpCodes.Stelem_I4);
        Assert.True(analysis.Consistent);
        Assert.Equal(["System.Int32[]", "System.Int32[]", "System.Int32", "System.Int32"], Types(analysis.StackBefore(store)));
        Assert.Same(array.Body.Instructions.First(i => i.OpCode == OpCodes.Newarr), analysis.StackBefore(store)![0].Producer);

        // A generic call's return, with its type argument put in: List<string>.get_Item returns !0.
        var first = Method("First");
        analysis = StackAnalysis.Analyze(first.Body);
        var afterCall = first.Body.Instructions.First(i => i.OpCode == OpCodes.Callvirt).Next;
        Assert.Equal("System.String", Types(analysis.StackBefore(afterCall)).Last());

        // A null joined with a reference takes the reference's type.
        var join = Method("NullJoin");
        analysis = StackAnalysis.Analyze(join.Body);
        var call = join.Body.Instructions.Single(i => i.Operand is MethodReference { Name: "Length" });
        Assert.Equal(["System.String"], Types(analysis.StackBefore(call)));

        // A catch handler starts with its exception on the stack.
        var caught = Method("Caught");
        analysis = StackAnalysis.Analyze(caught.Body);
        var handler = caught.Body.ExceptionHandlers.Single().HandlerStart;
        Assert.Equal(["System.InvalidOperationException"], Types(analysis.StackBefore(handler)));
    }

    private static string Fixture(string name, OptimizationLevel optimization) =>
        TestAssemblyBuilder.Build($"{name}_{optimization}", """
            using FluentAssertions;
            using Kronikol.Tracking;

            [assembly: TrackAssertions]

            public class Tests
            {
                public void Method()
                {
                    var total = 3;
                    total.Should().Be(3);
                }
            }
            """, optimization);

    /// <summary>
    /// Puts a path in front of the method's code that pushes the values <paramref name="pushes"/> name and branches into
    /// the assertion's statement: to its Should() call, or with <paramref name="afterShould"/> to the instruction after
    /// it (in Release the call is the statement's first instruction, the subject pushed by the line above). No
    /// compiler writes either.
    /// </summary>
    private static void BranchIntoTheStatement(string path, OpCode[] pushes, bool afterShould = false)
    {
        using var assembly = AssemblyDefinition.ReadAssembly(path, new ReaderParameters { ReadWrite = true, ReadSymbols = true });
        var method = assembly.MainModule.GetType("Tests").Methods.Single(m => m.Name == "Method");
        var il = method.Body.GetILProcessor();
        var should = method.Body.Instructions.First(i => i.Operand is MethodReference { Name: "Should" });
        var target = afterShould ? should.Next : should;
        var first = method.Body.Instructions[0];
        foreach (var push in pushes)
            il.InsertBefore(first, il.Create(push));
        il.InsertBefore(first, il.Create(OpCodes.Ldc_I4_0));
        il.InsertBefore(first, il.Create(OpCodes.Brtrue, target));
        foreach (var _ in pushes)
            il.InsertBefore(first, il.Create(OpCodes.Pop));
        assembly.Write(new WriterParameters { WriteSymbols = true });
    }

    [Theory]
    [InlineData(OptimizationLevel.Debug)]
    [InlineData(OptimizationLevel.Release)]
    public void A_statement_the_analysis_cannot_type_is_left_unwoven_and_counted(OptimizationLevel optimization)
    {
        // Two values on one path to the Should() call and one on the other: the depths cannot be reconciled.
        var path = Fixture("StackUnfollowable", optimization);
        BranchIntoTheStatement(path, [OpCodes.Ldc_I4_1, OpCodes.Ldc_I4_1]);

        var result = WovenIl.Weave(new AssertionWeaver(), path);

        Assert.Equal(0, result.WeavedCount);
        var unwoven = Assert.Single(result.Unwoven);
        Assert.StartsWith("Tests.Method, line ", unwoven);
        Assert.EndsWith(": the method's evaluation stack could not be followed", unwoven);
    }

    [Theory]
    [InlineData(OptimizationLevel.Debug)]
    [InlineData(OptimizationLevel.Release)]
    public void A_statement_a_branch_enters_part_way_is_left_unwoven(OptimizationLevel optimization)
    {
        // One value on each path, so the stack agrees (a null joins the assertion object Should() returns); but the
        // branch lands inside the statement, and a try around it would be entered by a branch (ILVerify's
        // BranchIntoTry, which the weave wrote before this check).
        var path = Fixture("StackEnteredPartWay", optimization);
        BranchIntoTheStatement(path, [OpCodes.Ldnull], afterShould: true);

        var result = WovenIl.Weave(new AssertionWeaver(), path);

        Assert.Equal(0, result.WeavedCount);
        Assert.EndsWith(": a branch from outside the statement lands inside it", Assert.Single(result.Unwoven));
    }
}
