using System.Runtime.CompilerServices;
using Kronikol.AssertionTracking;
using Microsoft.CodeAnalysis;
using Mono.Cecil;
using Mono.Cecil.Cil;

namespace Kronikol.Tests.AssertionTracking;

/// <summary>
/// The verification net (SHOULDLY_ASSERTIONS_PLAN section 3.2, Q8): every fixture this project weaves, in every build
/// it is compiled in, is read by ILVerify before and after the weave, and an error the weave added fails the fact
/// that wove it. A run proves only the methods it reaches; the verifier reads them all.
/// </summary>
public class WovenIlVerifiesTests
{
    [Fact]
    public void Every_woven_fixture_verifies()
    {
        // Each weave verifies itself through WovenIl, so this holds that no fixture is woven any other way: a direct
        // call to the weaver or the task would weave a fixture the net never reads.
        var directory = Path.GetDirectoryName(SourcePath())!;
        var bypasses = Directory.EnumerateFiles(directory, "*.cs", SearchOption.TopDirectoryOnly)
            .Where(file => Path.GetFileName(file) is not (nameof(WovenIl) + ".cs") and not (nameof(WovenIlVerifiesTests) + ".cs"))
            .SelectMany(file => File.ReadLines(file).Select((line, index) => (file, line, index)))
            .Where(l => l.line.Contains(".Weave(", StringComparison.Ordinal) && !l.line.Contains("WovenIl.Weave(", StringComparison.Ordinal)
                || l.line.Contains(".Execute()", StringComparison.Ordinal) && !l.line.Contains("var succeeded = task.Execute();", StringComparison.Ordinal))
            .Select(l => $"{Path.GetFileName(l.file)}:{l.index + 1}: {l.line.Trim()}")
            .ToList();

        Assert.True(bypasses.Count == 0, "A weave that ILVerify never reads:\n  " + string.Join("\n  ", bypasses));
        Assert.True(Directory.EnumerateFiles(directory, "*.cs").Sum(f => File.ReadAllText(f).Split("WovenIl.Weave(").Length - 1) >= 60,
            "the scan should see the suite's weaves");
    }

    [Theory]
    [InlineData(OptimizationLevel.Debug)]
    [InlineData(OptimizationLevel.Release)]
    public void The_net_catches_a_weave_that_leaves_a_value_on_the_stack(OptimizationLevel optimization)
    {
        var path = TestAssemblyBuilder.Build($"NetCatches{optimization}", """
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
        var unwoven = File.ReadAllBytes(path);
        WovenIl.Weave(new AssertionWeaver(), path);

        // A weave that forgot a spill: a value pushed before the try is still on the stack at its leave.
        using (var assembly = AssemblyDefinition.ReadAssembly(path, new ReaderParameters { ReadWrite = true, ReadSymbols = true }))
        {
            var method = assembly.MainModule.GetType("Tests").Methods.Single(m => m.Name == "Method");
            var tryStart = method.Body.ExceptionHandlers[0].TryStart;
            method.Body.GetILProcessor().InsertBefore(tryStart, Instruction.Create(OpCodes.Ldc_I4_7));
            assembly.Write(new WriterParameters { WriteSymbols = true });
        }

        var added = WovenIl.NewErrors(unwoven, File.ReadAllBytes(path), path);

        Assert.NotEmpty(added);
        Assert.All(added, error => Assert.StartsWith("Tests.Method: ", error));
    }

    [Theory]
    [InlineData(OptimizationLevel.Debug)]
    [InlineData(OptimizationLevel.Release)]
    public void An_error_the_compiler_wrote_is_not_counted(OptimizationLevel optimization)
    {
        // stackalloc is never verifiable, so the unwoven assembly has an error of its own in the method the weave
        // instruments; the net judges what the weave added.
        var path = TestAssemblyBuilder.Build($"NetCompilerError{optimization}", """
            using System;
            using FluentAssertions;
            using Kronikol.Tracking;

            [assembly: TrackAssertions]

            public class Tests
            {
                public void Method()
                {
                    Span<int> buffer = stackalloc int[2];
                    buffer[0] = 3;
                    buffer[0].Should().Be(3);
                }
            }
            """, optimization);
        var unwoven = File.ReadAllBytes(path);
        Assert.Contains(WovenIl.Verify(unwoven, path), error => error.Key.StartsWith("Tests.Method: ", StringComparison.Ordinal));

        var result = WovenIl.Weave(new AssertionWeaver(), path);

        Assert.Equal(1, result.WeavedCount);
        Assert.Empty(WovenIl.NewErrors(unwoven, File.ReadAllBytes(path), path));
    }

    private static string SourcePath([CallerFilePath] string path = "") => path;
}
